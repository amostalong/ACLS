using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        // baseUrl: optional. Empty/null → official api.anthropic.com. Pass a
        // relay/proxy root (e.g. "https://your-proxy.example.com/claude") to
        // route requests through it. The "/v1/messages" path is appended.
        public AnthropicClient(string baseUrl, string apiKey, string model, int maxTokens, bool verbose)
        {
            string root = string.IsNullOrWhiteSpace(baseUrl) ? DefaultRoot : baseUrl.TrimEnd('/');
            this.endpoint = root + "/v1/messages";
            this.apiKey = apiKey;
            this.model = model;
            this.maxTokens = maxTokens;
            this.verbose = verbose;
        }

        public async Task<LlmResponse> CompleteAsync(string systemPrompt,
                                                     IReadOnlyList<ChatMessage> messages,
                                                     CancellationToken ct)
        {
            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                system = systemPrompt,
                messages = messages.Select(m => new
                {
                    role = m.Role == ChatRole.Assistant ? "assistant" : "user",
                    content = m.Content,
                }).ToArray(),
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Debug.Log($"[Anthropic] → endPoint {endpoint}");
            if (verbose) Debug.Log($"[Anthropic] → ApiVersion {ApiVersion}");
            if (verbose) Debug.Log($"[Anthropic] → {json}");

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("anthropic-version", ApiVersion);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (verbose) Debug.Log($"[Anthropic] ← {(int)resp.StatusCode} {respText}");
            LlmDebugLog.Add("Anthropic", json, respText);

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Anthropic {(int)resp.StatusCode}: {Truncate(respText, 500)}");
            }

            JObject parsed;
            try { parsed = JObject.Parse(respText); }
            catch (JsonException ex) { throw new HttpRequestException("Anthropic 响应不是合法 JSON：" + ex.Message); }

            // content is an array of {type, text} blocks; concatenate text blocks.
            var content = parsed["content"] as JArray;
            if (content == null || content.Count == 0) throw new HttpRequestException("Anthropic 响应缺 content 字段");
            var sb = new StringBuilder();
            foreach (var block in content)
            {
                if ((string)block["type"] == "text") sb.Append((string)block["text"]);
            }

            var usage = new LlmUsage();
            var usageObj = parsed["usage"];
            if (usageObj != null)
            {
                usage.InputTokens = (int?)usageObj["input_tokens"] ?? 0;
                usage.OutputTokens = (int?)usageObj["output_tokens"] ?? 0;
            }

            return new LlmResponse { Content = sb.ToString(), Usage = usage };
        }

        public async Task<LlmResponse> CompleteStreamAsync(string systemPrompt,
                                                           IReadOnlyList<ChatMessage> messages,
                                                           System.Action<string> onTextDelta,
                                                           CancellationToken ct)
        {
            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                system = systemPrompt,
                messages = messages.Select(m => new
                {
                    role = m.Role == ChatRole.Assistant ? "assistant" : "user",
                    content = m.Content,
                }).ToArray(),
                stream = true,
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Debug.Log($"[Anthropic] → endPoint {endpoint}");
            if (verbose) Debug.Log($"[Anthropic] → ApiVersion {ApiVersion}");
            if (verbose) Debug.Log($"[Anthropic] → {json}");

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("anthropic-version", ApiVersion);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (verbose) Debug.Log($"[Anthropic] ← {(int)resp.StatusCode} {err}");
                LlmDebugLog.Add("Anthropic", json, err);
                throw new HttpRequestException($"Anthropic {(int)resp.StatusCode}: {Truncate(err, 500)}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var sb = new StringBuilder();
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                string data = line.Substring(5).Trim();
                if (string.IsNullOrWhiteSpace(data)) continue;

                try
                {
                    var obj = JObject.Parse(data);
                    string type = (string)obj["type"] ?? "";
                    string delta = type switch
                    {
                        "content_block_start" => (string)obj["content_block"]?["text"],
                        "content_block_delta" => (string)obj["delta"]?["text"],
                        _ => null,
                    };
                    if (!string.IsNullOrEmpty(delta))
                    {
                        sb.Append(delta);
                        onTextDelta?.Invoke(delta);
                    }
                }
                catch (JsonException)
                {
                }
            }

            var usage = new LlmUsage();
            return new LlmResponse { Content = sb.ToString(), Usage = usage };
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
