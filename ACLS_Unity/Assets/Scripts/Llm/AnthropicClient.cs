using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    public sealed class AnthropicClient : ILlmClient
    {
        private const string DefaultRoot = "https://api.anthropic.com";
        private const string ApiVersion = "2023-06-01";

        private static readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        private readonly string endpoint;
        private readonly string apiKey;
        private readonly string model;
        private readonly int maxTokens;
        private readonly bool verbose;

        public AnthropicClient(string baseUrl, string apiKey, string model, int maxTokens, bool verbose)
        {
            string root = string.IsNullOrWhiteSpace(baseUrl) ? DefaultRoot : baseUrl.TrimEnd('/');
            this.endpoint = root + "/v1/messages";
            this.apiKey = apiKey;
            this.model = model;
            this.maxTokens = maxTokens;
            this.verbose = verbose;
        }

        // ────── ILlmClient.CompleteAsync ──────

        public async Task<LlmResponse> CompleteAsync(string systemPrompt,
                                                     IReadOnlyList<ChatMessage> messages,
                                                     CancellationToken ct,
                                                     bool jsonObject = true)
        {
            // jsonObject is reserved: Anthropic has no response_format. JSON
            // constraint is enforced via prompt / tool schema instead. Flag
            // accepted to satisfy ILlmClient; not yet wired.
            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                system = systemPrompt,
                messages = BuildAnthropicMessages(messages),
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Log.Info(Log.Channels.Network, "[Anthropic] → {0}", json);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("anthropic-version", ApiVersion);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (verbose) Log.Info(Log.Channels.Network, "[Anthropic] ← {0} {1}", (int)resp.StatusCode, Truncate(respText, 300));
            LlmDebugLog.Add("Anthropic", json, respText);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Anthropic {(int)resp.StatusCode}: {Truncate(respText, 500)}");

            return ParseAnthropicResponse(respText);
        }

        // ────── ILlmClient.CompleteStreamAsync ──────

        public async Task<LlmResponse> CompleteStreamAsync(string systemPrompt,
                                                           IReadOnlyList<ChatMessage> messages,
                                                           Action<string> onTextDelta,
                                                           CancellationToken ct,
                                                           bool jsonObject = true)
        {
            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                system = systemPrompt,
                messages = BuildAnthropicMessages(messages),
                stream = true,
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Log.Info(Log.Channels.Network, "[Anthropic] → {0}", Truncate(json, 8196));

            return await StreamAnthropic(json, onTextDelta, ct);
        }

        // ────── ILlmClient.CompleteStreamWithToolsAsync ──────

        public async Task<LlmResponse> CompleteStreamWithToolsAsync(
            string systemPrompt,
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            Action<string> onTextDelta,
            CancellationToken ct,
            bool jsonObject = true)
        {
            // Build the request body with tools
            var bodyObj = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens <= 0 ? 8192 : maxTokens,
                ["system"] = systemPrompt,
                ["messages"] = BuildAnthropicMessagesJArray(messages),
                ["stream"] = true,
            };

            if (tools != null && tools.Count > 0)
                bodyObj["tools"] = BuildAnthropicTools(tools);

            string json = bodyObj.ToString(Formatting.None);
            if (verbose) Log.Info(Log.Channels.Network, "[Anthropic] → (tools={0}) {1}", tools?.Count ?? 0, Truncate(json, 4096));

            return await StreamAnthropic(json, onTextDelta, ct);
        }

        // ────── 流式响应处理（含 tool_use 检测） ──────

        private async Task<LlmResponse> StreamAnthropic(string requestJson,
                                                         Action<string> onTextDelta,
                                                         CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("anthropic-version", ApiVersion);
            req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            bool firstChunk = true;
            int chunkCount = 0;

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (verbose) Log.Info(Log.Channels.Network, "[Anthropic] ← {0} {1}", (int)resp.StatusCode, Truncate(err, 300));
                LlmDebugLog.Add("Anthropic", requestJson, err);
                throw new HttpRequestException($"Anthropic {(int)resp.StatusCode}: {Truncate(err, 500)}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var textSb = new StringBuilder();
            var toolCalls = new List<LlmToolCall>();
            var usage = new LlmUsage();
            string stopReason = "";

            // Track in-progress content blocks by index
            var textBlocks = new Dictionary<int, StringBuilder>();
            var toolBlocks = new Dictionary<int, LlmToolCall>();    // index → accumulating tool call

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                string data = line.Substring(5).Trim();
                if (string.IsNullOrWhiteSpace(data)) continue;

                chunkCount++;
                if (firstChunk)
                {
                    firstChunk = false;
                    Log.Info(Log.Channels.Network, "[Anthropic] TTFT: {0:F2}s", swTotal.Elapsed.TotalSeconds);
                }

                try
                {
                    var obj = JObject.Parse(data);
                    string type = (string)obj["type"] ?? "";
                    int index = obj["index"]?.Value<int>() ?? 0;

                    switch (type)
                    {
                        case "content_block_start":
                        {
                            var block = obj["content_block"] as JObject;
                            if (block == null) break;
                            string blockType = (string)block["type"] ?? "";

                            if (blockType == "text")
                            {
                                textBlocks[index] = new StringBuilder();
                            }
                            else if (blockType == "tool_use")
                            {
                                var tc = new LlmToolCall
                                {
                                    Id = (string)block["id"] ?? "",
                                    Name = (string)block["name"] ?? "",
                                    Args = block["input"]?.ToString(Formatting.None) ?? "{}",
                                };
                                toolBlocks[index] = tc;
                            }
                            break;
                        }

                        case "content_block_delta":
                        {
                            var delta = obj["delta"] as JObject;
                            if (delta == null) break;
                            string deltaType = (string)delta["type"] ?? "";

                            if (deltaType == "text_delta")
                            {
                                string text = (string)delta["text"] ?? "";
                                if (!string.IsNullOrEmpty(text))
                                {
                                    textSb.Append(text);
                                    onTextDelta?.Invoke(text);
                                }
                            }
                            else if (deltaType == "input_json_delta")
                            {
                                string partial = (string)delta["partial_json"] ?? "";
                                if (!string.IsNullOrEmpty(partial) && toolBlocks.TryGetValue(index, out var tc))
                                {
                                    // For tool_use, the full input is accumulated
                                    // We rebuild it each time since partial_json is additive
                                    // Actually for Anthropic, partial_json is concatenated pieces
                                    // We'll use a StringBuilder to accumulate
                                    if (!textBlocks.ContainsKey(index))
                                    {
                                        // Reuse textBlocks dict as accumulators for tool input JSON
                                        textBlocks[index] = new StringBuilder();
                                    }
                                    textBlocks[index].Append(partial);
                                }
                            }
                            break;
                        }

                        case "message_delta":
                        {
                            var msgDelta = obj["delta"] as JObject;
                            if (msgDelta != null)
                                stopReason = (string)msgDelta["stop_reason"] ?? "";
                            var usageObj = obj["usage"] as JObject;
                            if (usageObj != null)
                            {
                                usage.InputTokens = (int?)usageObj["input_tokens"] ?? usage.InputTokens;
                                usage.OutputTokens = (int?)usageObj["output_tokens"] ?? usage.OutputTokens;
                            }
                            break;
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            // Assemble tool calls from accumulated blocks
            foreach (var kv in toolBlocks)
            {
                var tc = kv.Value;
                // If we have accumulated JSON via input_json_delta, use that
                if (textBlocks.TryGetValue(kv.Key, out var acc) && acc.Length > 0)
                {
                    tc.Args = acc.ToString();
                }
                toolCalls.Add(tc);
            }

            if (toolCalls.Count > 0)
            {
                foreach (var tc in toolCalls)
                    Log.Info(Log.Channels.Network, "[Anthropic] ← tool_use: {0} id={1} args={2}", tc.Name, tc.Id, tc.Args);
            }

            var result = new LlmResponse
            {
                Content = textSb.ToString(),
                Usage = usage,
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
                StopReason = stopReason,
            };

            swTotal.Stop();
            Log.Info(Log.Channels.Network, "[Anthropic] ← {0} chars tools={1} stop={2} | {3:F2}s total, {4} chunks",
                result.Content.Length, toolCalls.Count, stopReason, swTotal.Elapsed.TotalSeconds, chunkCount);

            return result;
        }

        // ────── 非流式响应解析 ──────

        private LlmResponse ParseAnthropicResponse(string respText)
        {
            JObject parsed;
            try { parsed = JObject.Parse(respText); }
            catch (JsonException ex) { throw new HttpRequestException("Anthropic 响应不是合法 JSON：" + ex.Message); }

            var content = parsed["content"] as JArray;
            if (content == null || content.Count == 0)
                throw new HttpRequestException("Anthropic 响应缺 content 字段");

            var textSb = new StringBuilder();
            var toolCalls = new List<LlmToolCall>();

            foreach (var block in content)
            {
                string blockType = (string)block["type"] ?? "";
                if (blockType == "text")
                {
                    textSb.Append((string)block["text"] ?? "");
                }
                else if (blockType == "tool_use")
                {
                    toolCalls.Add(new LlmToolCall
                    {
                        Id = (string)block["id"] ?? "",
                        Name = (string)block["name"] ?? "",
                        Args = block["input"]?.ToString(Formatting.None) ?? "{}",
                    });
                }
            }

            var usage = new LlmUsage();
            var usageObj = parsed["usage"];
            if (usageObj != null)
            {
                usage.InputTokens = (int?)usageObj["input_tokens"] ?? 0;
                usage.OutputTokens = (int?)usageObj["output_tokens"] ?? 0;
            }

            return new LlmResponse
            {
                Content = textSb.ToString(),
                Usage = usage,
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
                StopReason = (string)parsed["stop_reason"] ?? "",
            };
        }

        // ────── 消息序列化 ──────

        /// <summary>
        /// 将内部 ChatMessage 列表转为 Anthropic API 格式的消息数组。
        /// 处理 ToolCall → tool_use 内容块、ToolResult → tool_result 内容块的合并。
        /// </summary>
        private JArray BuildAnthropicMessagesJArray(IReadOnlyList<ChatMessage> messages)
        {
            var result = new JArray();
            if (messages == null) return result;

            // Convert flat message list to Anthropic format.
            // This groups consecutive ToolCall messages into a single assistant message,
            // and consecutive ToolResult messages into a single user message.
            JObject currentAssistant = null;  // accumulating assistant with tool_use blocks
            JArray currentAssistantContent = null;
            JObject currentToolResultUser = null;  // accumulating user with tool_result blocks
            JArray currentToolResultContent = null;

            void FlushToolResult()
            {
                if (currentToolResultUser != null)
                {
                    result.Add(currentToolResultUser);
                    currentToolResultUser = null;
                    currentToolResultContent = null;
                }
            }

            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];

                switch (msg.Role)
                {
                    case ChatRole.System:
                        // Skip — system prompt is handled separately
                        break;

                    case ChatRole.User:
                        FlushAssistant(ref currentAssistant, ref currentAssistantContent, result);
                        FlushToolResult();
                        result.Add(new JObject
                        {
                            ["role"] = "user",
                            ["content"] = msg.Content,
                        });
                        break;

                    case ChatRole.Assistant:
                        FlushAssistant(ref currentAssistant, ref currentAssistantContent, result);
                        FlushToolResult();
                        // Start a new assistant message — might get tool_use appended
                        currentAssistant = new JObject { ["role"] = "assistant" };
                        currentAssistantContent = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "text",
                                ["text"] = msg.Content,
                            }
                        };
                        currentAssistant["content"] = currentAssistantContent;
                        break;

                    case ChatRole.ToolCall:
                        FlushToolResult();
                        // Append as tool_use block to the current assistant message
                        if (currentAssistantContent == null)
                        {
                            currentAssistant = new JObject { ["role"] = "assistant" };
                            currentAssistantContent = new JArray();
                            currentAssistant["content"] = currentAssistantContent;
                        }

                        JObject input;
                        try { input = JObject.Parse(msg.Content); }
                        catch { input = new JObject(); }

                        currentAssistantContent.Add(new JObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = msg.ToolCallId,
                            ["name"] = msg.ToolName,
                            ["input"] = input,
                        });
                        break;

                    case ChatRole.ToolResult:
                        FlushAssistant(ref currentAssistant, ref currentAssistantContent, result);
                        // Accumulate consecutive ToolResults into a single user message
                        if (currentToolResultContent == null)
                        {
                            currentToolResultUser = new JObject { ["role"] = "user" };
                            currentToolResultContent = new JArray();
                            currentToolResultUser["content"] = currentToolResultContent;
                        }
                        currentToolResultContent.Add(new JObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = msg.ToolCallId,
                            ["content"] = msg.Content,
                        });
                        break;
                }
            }

            FlushAssistant(ref currentAssistant, ref currentAssistantContent, result);
            FlushToolResult();
            return result;
        }

        private static void FlushAssistant(ref JObject assistant, ref JArray content, JArray result)
        {
            if (assistant != null)
            {
                result.Add(assistant);
                assistant = null;
                content = null;
            }
        }

        /// <summary>
        /// 简单的消息序列化（用于不支持工具调用的请求——只合并 ToolCall/ToolResult 为文本）。
        /// </summary>
        private object[] BuildAnthropicMessages(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null) return Array.Empty<object>();
            return messages
                .Where(m => m.Role != ChatRole.ToolCall && m.Role != ChatRole.ToolResult)
                .Select(m => new
                {
                    role = m.Role == ChatRole.Assistant ? "assistant" : "user",
                    content = m.Content,
                })
                .Cast<object>()
                .ToArray();
        }

        // ────── 工具定义序列化 ──────

        private JArray BuildAnthropicTools(IReadOnlyList<ToolDefinition> tools)
        {
            var arr = new JArray();
            foreach (var t in tools)
            {
                arr.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = t.InputSchema,
                });
            }
            return arr;
        }

        // ────── 工具 ──────

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
