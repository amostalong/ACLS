using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACLS.Llm.Tools;
using ACLS.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ACLS.Llm
{
    public sealed class OpenAiCompatibleClient : ILlmClient
    {
        private static readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        private readonly string baseUrl;
        private readonly string apiKey;
        private readonly string model;
        private readonly int maxTokens;
        private readonly bool verbose;

        public OpenAiCompatibleClient(string baseUrl, string apiKey, string model, int maxTokens, bool verbose)
        {
            this.baseUrl = (baseUrl ?? "").TrimEnd('/');
            this.apiKey = apiKey;
            this.model = model;
            this.maxTokens = maxTokens;
            this.verbose = verbose;
        }

        // ────── ILlmClient.CompleteAsync ──────

        public async Task<LlmResponse> CompleteAsync(string systemPrompt,
                                                     IReadOnlyList<ChatMessage> messages,
                                                     CancellationToken ct)
        {
            var openAiMessages = BuildOpenAiMessages(systemPrompt, messages, false);
            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                messages = openAiMessages,
                response_format = new { type = "json_object" },
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Log.Info(Log.Channels.Network, "[OpenAI] → {0}", Truncate(json, 300));

            return await SendOpenAi(json, ct);
        }

        // ────── ILlmClient.CompleteStreamAsync ──────

        public async Task<LlmResponse> CompleteStreamAsync(string systemPrompt,
                                                           IReadOnlyList<ChatMessage> messages,
                                                           Action<string> onTextDelta,
                                                           CancellationToken ct)
        {
            var openAiMessages = BuildOpenAiMessages(systemPrompt, messages, false);

            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                messages = openAiMessages,
                response_format = new { type = "json_object" },
                stream = true,
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Log.Info(Log.Channels.Network, "[OpenAI] → {0}", Truncate(json, 300));

            return await StreamOpenAi(json, onTextDelta, ct);
        }

        // ────── ILlmClient.CompleteStreamWithToolsAsync ──────

        public async Task<LlmResponse> CompleteStreamWithToolsAsync(
            string systemPrompt,
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            Action<string> onTextDelta,
            CancellationToken ct)
        {
            var openAiMessages = BuildOpenAiMessages(systemPrompt, messages, true);

            var bodyObj = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens <= 0 ? 8192 : maxTokens,
                ["messages"] = openAiMessages,
                ["stream"] = true,
            };

            if (tools != null && tools.Count > 0)
                bodyObj["tools"] = BuildOpenAiTools(tools);

            string json = bodyObj.ToString(Formatting.None);
            if (verbose) Log.Info(Log.Channels.Network, "[OpenAI] → (tools={0}) {1}", tools?.Count ?? 0, Truncate(json, 300));

            return await StreamOpenAi(json, onTextDelta, ct);
        }

        // ────── OpenAI 流式响应处理（含 tool_calls 检测） ──────

        private async Task<LlmResponse> StreamOpenAi(string requestJson,
                                                      Action<string> onTextDelta,
                                                      CancellationToken ct)
        {
            string url = baseUrl + "/v1/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (verbose) Log.Info(Log.Channels.Network, "[OpenAI] ← {0} {1}", (int)resp.StatusCode, Truncate(err, 300));
                LlmDebugLog.Add("OpenAI", requestJson, err);
                throw new HttpRequestException($"OpenAI 兼容 {(int)resp.StatusCode}: {Truncate(err, 500)}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var textSb = new StringBuilder();
            var toolCalls = new List<LlmToolCall>();
            // Track tool calls by index during streaming
            var toolCallAccum = new Dictionary<int, LlmToolCall>();

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                string data = line.Substring(5).Trim();
                if (data == "[DONE]") break;

                try
                {
                    var obj = JObject.Parse(data);
                    var choice = obj["choices"]?[0];
                    if (choice == null) continue;

                    var delta = choice["delta"];
                    if (delta == null) continue;

                    // Text content
                    string content = (string)delta["content"];
                    if (!string.IsNullOrEmpty(content))
                    {
                        textSb.Append(content);
                        onTextDelta?.Invoke(content);
                    }

                    // Tool calls
                    var toolCallsDelta = delta["tool_calls"] as JArray;
                    if (toolCallsDelta != null && toolCallsDelta.Count > 0)
                    {
                        foreach (var tc in toolCallsDelta)
                        {
                            int idx = tc["index"]?.Value<int>() ?? 0;
                            string id = (string)tc["id"] ?? "";

                            if (!toolCallAccum.ContainsKey(idx))
                                toolCallAccum[idx] = new LlmToolCall();

                            if (!string.IsNullOrEmpty(id))
                                toolCallAccum[idx].Id = id;

                            var func = tc["function"];
                            if (func != null)
                            {
                                string name = (string)func["name"] ?? "";
                                if (!string.IsNullOrEmpty(name))
                                    toolCallAccum[idx].Name = name;

                                string args = (string)func["arguments"] ?? "";
                                if (!string.IsNullOrEmpty(args))
                                    toolCallAccum[idx].Args += args;
                            }
                        }
                    }

                    // Check finish_reason
                    string finishReason = (string)choice["finish_reason"] ?? "";
                    if (finishReason == "tool_calls")
                    {
                        // Will collect tool calls after streaming ends
                    }
                }
                catch (JsonException)
                {
                }
            }

            // Collect accumulated tool calls
            if (toolCallAccum.Count > 0)
            {
                foreach (var kv in toolCallAccum.OrderBy(k => k.Key))
                    toolCalls.Add(kv.Value);
            }

            var result = new LlmResponse
            {
                Content = textSb.ToString(),
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            };

            if (verbose) Log.Info(Log.Channels.Network, "[OpenAI] ← {0} chars tools={1}",
                result.Content.Length, toolCalls.Count);

            return result;
        }

        // ────── 非流式请求 ──────

        private async Task<LlmResponse> SendOpenAi(string requestJson, CancellationToken ct)
        {
            string url = baseUrl + "/v1/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (verbose) Log.Info(Log.Channels.Network, "[OpenAI] ← {0} {1}", (int)resp.StatusCode, Truncate(respText, 300));
            LlmDebugLog.Add("OpenAI", requestJson, respText);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"OpenAI 兼容 {(int)resp.StatusCode}: {Truncate(respText, 500)}");

            JObject parsed;
            try { parsed = JObject.Parse(respText); }
            catch (JsonException ex) { throw new HttpRequestException("响应不是合法 JSON：" + ex.Message); }

            var choices = parsed["choices"] as JArray;
            if (choices == null || choices.Count == 0) throw new HttpRequestException("响应缺 choices 字段");

            var message = choices[0]?["message"];
            if (message == null) throw new HttpRequestException("响应缺 choices[0].message");

            string content = (string)message["content"] ?? "";
            var toolCalls = new List<LlmToolCall>();

            var toolCallsArray = message["tool_calls"] as JArray;
            if (toolCallsArray != null)
            {
                foreach (var tc in toolCallsArray)
                {
                    toolCalls.Add(new LlmToolCall
                    {
                        Id = (string)tc["id"] ?? "",
                        Name = (string)tc["function"]?["name"] ?? "",
                        Args = (string)tc["function"]?["arguments"] ?? "{}",
                    });
                }
            }

            var usage = new LlmUsage();
            var usageObj = parsed["usage"];
            if (usageObj != null)
            {
                usage.InputTokens = (int?)usageObj["prompt_tokens"] ?? 0;
                usage.OutputTokens = (int?)usageObj["completion_tokens"] ?? 0;
            }

            return new LlmResponse
            {
                Content = content,
                Usage = usage,
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            };
        }

        // ────── 消息序列化 ──────

        private JArray BuildOpenAiMessages(string systemPrompt, IReadOnlyList<ChatMessage> messages, bool forTools)
        {
            var result = new JArray();
            result.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

            if (messages == null) return result;

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];

                switch (msg.Role)
                {
                    case ChatRole.System:
                        result.Add(new JObject { ["role"] = "system", ["content"] = msg.Content });
                        break;

                    case ChatRole.User:
                        result.Add(new JObject { ["role"] = "user", ["content"] = msg.Content });
                        break;

                    case ChatRole.Assistant:
                        result.Add(new JObject { ["role"] = "assistant", ["content"] = msg.Content });
                        break;

                    case ChatRole.ToolCall:
                        // OpenAI: tool_calls go inside the assistant message
                        // Find the last assistant message and add tool_calls to it
                        if (result.Count > 0 && (string)result.Last["role"] == "assistant")
                        {
                            var last = (JObject)result.Last;
                            var tcArr = last["tool_calls"] as JArray;
                            if (tcArr == null)
                            {
                                tcArr = new JArray();
                                last["tool_calls"] = tcArr;
                            }
                            JObject input;
                            try { input = JObject.Parse(msg.Content); }
                            catch { input = new JObject(); }

                            tcArr.Add(new JObject
                            {
                                ["id"] = msg.ToolCallId,
                                ["type"] = "function",
                                ["function"] = new JObject
                                {
                                    ["name"] = msg.ToolName,
                                    ["arguments"] = msg.Content,
                                }
                            });
                        }
                        break;

                    case ChatRole.ToolResult:
                        result.Add(new JObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = msg.ToolCallId,
                            ["content"] = msg.Content,
                        });
                        break;
                }
            }

            return result;
        }

        // ────── 工具定义序列化 ──────

        private JArray BuildOpenAiTools(IReadOnlyList<ToolDefinition> tools)
        {
            var arr = new JArray();
            foreach (var t in tools)
            {
                arr.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.InputSchema,
                    }
                });
            }
            return arr;
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
