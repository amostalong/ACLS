using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task<LlmResponse> CompleteAsync(string systemPrompt,
                                                     IReadOnlyList<ChatMessage> messages,
                                                     CancellationToken ct)
        {
            // OpenAI shape: system as messages[0], then user/assistant turns.
            var openAiMessages = new List<object> { new { role = "system", content = systemPrompt } };
            openAiMessages.AddRange(messages.Select(m => new
            {
                role = m.Role switch
                {
                    ChatRole.Assistant => "assistant",
                    ChatRole.System => "system",
                    _ => "user",
                },
                content = m.Content,
            }).Cast<object>());

            var body = new
            {
                model = model,
                max_tokens = maxTokens <= 0 ? 8192 : maxTokens,
                messages = openAiMessages,
                response_format = new { type = "json_object" },
            };

            string json = JsonConvert.SerializeObject(body);
            if (verbose) Debug.Log($"[OpenAI] → {json}");

            string url = baseUrl + "/v1/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(true);
            string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(true);

            if (verbose) Debug.Log($"[OpenAI] ← {(int)resp.StatusCode} {respText}");
            LlmDebugLog.Add("OpenAI", json, respText);

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"OpenAI 兼容 {(int)resp.StatusCode}: {Truncate(respText, 500)}");
            }

            JObject parsed;
            try { parsed = JObject.Parse(respText); }
            catch (JsonException ex) { throw new HttpRequestException("响应不是合法 JSON：" + ex.Message); }

            var choices = parsed["choices"] as JArray;
            if (choices == null || choices.Count == 0) throw new HttpRequestException("响应缺 choices 字段");
            string content = (string)choices[0]?["message"]?["content"];
            if (content == null) throw new HttpRequestException("响应缺 choices[0].message.content");

            var usage = new LlmUsage();
            var usageObj = parsed["usage"];
            if (usageObj != null)
            {
                usage.InputTokens = (int?)usageObj["prompt_tokens"] ?? 0;
                usage.OutputTokens = (int?)usageObj["completion_tokens"] ?? 0;
            }

            return new LlmResponse { Content = content, Usage = usage };
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
