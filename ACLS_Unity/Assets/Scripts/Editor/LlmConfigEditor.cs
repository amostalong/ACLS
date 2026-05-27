using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using ACLS.Data;
using ACLS.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ACLS.Editor
{
    [CustomEditor(typeof(LlmConfig))]
    public sealed class LlmConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty profilesProp;

        private static readonly string[] FieldNames =
        {
            "ProfileName", "IsActive", "Provider", "ApiKey",
            "Model", "BaseUrl", "MaxTokens", "VerboseLogging",
        };

        // --- test state ---
        private enum TestStage { Idle, Running, Success, Error }
        private TestStage _testStage = TestStage.Idle;
        private string _testResultContent = "";
        private string _testError = "";
        private LlmUsage _testUsage;
        private Task<TestResult> _pendingTest;
        private CancellationTokenSource _testCts;
        private DateTime _testStartTime;
        private Vector2 _testScrollPos;
        private bool _testFoldout = true;

        private sealed class TestResult
        {
            public string Content;
            public LlmUsage Usage;
        }

        private void OnEnable()
        {
            profilesProp = serializedObject.FindProperty("Profiles");
        }

        private void OnDisable()
        {
            CancelTest();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            int count = profilesProp.arraySize;

            // --- draw each profile manually so we can intercept IsActive ---
            for (int i = 0; i < count; i++)
            {
                var profile = profilesProp.GetArrayElementAtIndex(i);
                var isActiveProp = profile.FindPropertyRelative("IsActive");
                var profileNameProp = profile.FindPropertyRelative("ProfileName");

                string label = string.IsNullOrWhiteSpace(profileNameProp?.stringValue)
                    ? $"Profile {i}"
                    : profileNameProp.stringValue;
                if (isActiveProp.boolValue) label = "▶ " + label;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // foldout header with remove button on the right
                EditorGUILayout.BeginHorizontal();
                profile.isExpanded = EditorGUILayout.Foldout(profile.isExpanded, label, toggleOnLabelClick: true);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    profilesProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                EditorGUILayout.EndHorizontal();

                if (profile.isExpanded)
                {
                    EditorGUI.indentLevel++;

                    foreach (var fieldName in FieldNames)
                    {
                        var prop = profile.FindPropertyRelative(fieldName);
                        if (prop == null) continue;

                        if (fieldName == "IsActive")
                        {
                            // Intercept: toggling on clears all others.
                            bool wasActive = prop.boolValue;
                            EditorGUI.BeginChangeCheck();
                            bool nowActive = EditorGUILayout.Toggle("激活", wasActive);
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (nowActive && !wasActive)
                                {
                                    for (int j = 0; j < profilesProp.arraySize; j++)
                                    {
                                        if (j != i)
                                            profilesProp.GetArrayElementAtIndex(j)
                                                        .FindPropertyRelative("IsActive").boolValue = false;
                                    }
                                }
                                prop.boolValue = nowActive;
                            }
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(prop);
                        }
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            // --- add button ---
            if (GUILayout.Button("＋ Add Profile"))
            {
                profilesProp.arraySize++;
                var newProfile = profilesProp.GetArrayElementAtIndex(profilesProp.arraySize - 1);
                newProfile.FindPropertyRelative("ProfileName").stringValue = "New Profile";
                newProfile.FindPropertyRelative("IsActive").boolValue = false;
                newProfile.FindPropertyRelative("ApiKey").stringValue = "";
                newProfile.FindPropertyRelative("Model").stringValue = "claude-haiku-4-5-20251001";
                newProfile.FindPropertyRelative("BaseUrl").stringValue = "";
                newProfile.FindPropertyRelative("MaxTokens").intValue = 4000;
                newProfile.FindPropertyRelative("VerboseLogging").boolValue = true;
                newProfile.isExpanded = true;
            }

            // --- status bar ---
            EditorGUILayout.Space(4);
            var cfg = (LlmConfig)target;
            var active = cfg.Active;
            if (active != null)
                EditorGUILayout.HelpBox($"激活：{active.ProfileName}  ·  {active.Model}", MessageType.Info);
            else
                EditorGUILayout.HelpBox("没有激活的 Profile，运行时 LLM 将不可用。", MessageType.Warning);

            // ================================================================
            //  LLM 连接测试区
            // ================================================================
            EditorGUILayout.Space(8);
            _testFoldout = EditorGUILayout.Foldout(_testFoldout, "\uD83E\uDD4A 连接测试", true, EditorStyles.foldoutHeader);
            if (_testFoldout)
            {
                EditorGUI.indentLevel++;

                GUILayout.BeginHorizontal();

                bool canTest = active != null && active.IsConfigured && _testStage != TestStage.Running;
                EditorGUI.BeginDisabledGroup(!canTest);
                if (GUILayout.Button("测试激活的 Profile", GUILayout.Height(24)))
                {
                    StartTest();
                }
                EditorGUI.EndDisabledGroup();

                if (_testStage == TestStage.Running)
                {
                    var elapsed = (DateTime.UtcNow - _testStartTime).TotalSeconds;
                    EditorGUILayout.LabelField($"\u23F3 测试中\u2026 {elapsed:F0}s", GUILayout.Width(140));
                    if (GUILayout.Button("取消", GUILayout.Width(50)))
                        CancelTest();
                }

                GUILayout.EndHorizontal();

                if (!canTest && active == null)
                {
                    EditorGUILayout.HelpBox("请先激活一个 Profile 并填写必要字段。", MessageType.Info);
                }
                else if (!canTest && active != null && !active.IsConfigured)
                {
                    EditorGUILayout.HelpBox(
                        active.Provider == LlmProvider.OpenAiCompatible
                            ? "OpenAI 兼容模式需要填写 ApiKey、Model 和 BaseUrl。"
                            : "Anthropic 模式需要填写 ApiKey 和 Model。",
                        MessageType.Warning);
                }

                // --- result display ---
                if (_testStage == TestStage.Success)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        $"\u2705 连接成功  \u00B7  输入 {_testUsage.InputTokens}  \u00B7  输出 {_testUsage.OutputTokens} tokens",
                        MessageType.Info);

                    _testScrollPos = EditorGUILayout.BeginScrollView(_testScrollPos,
                        GUILayout.MinHeight(60), GUILayout.MaxHeight(200));
                    EditorGUILayout.TextArea(_testResultContent, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndScrollView();
                }
                else if (_testStage == TestStage.Error)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox("\u274C 连接失败", MessageType.Error);

                    _testScrollPos = EditorGUILayout.BeginScrollView(_testScrollPos,
                        GUILayout.MinHeight(40), GUILayout.MaxHeight(120));
                    EditorGUILayout.SelectableLabel(_testError, EditorStyles.wordWrappedLabel,
                        GUILayout.MinHeight(30));
                    EditorGUILayout.EndScrollView();
                }

                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ----------------------------------------------------------------
        //  Test lifecycle
        // ----------------------------------------------------------------

        private void StartTest()
        {
            CancelTest();

            var cfg = (LlmConfig)target;
            var profile = cfg.Active;
            if (profile == null || !profile.IsConfigured) return;

            _testStage = TestStage.Running;
            _testResultContent = "";
            _testError = "";
            _testUsage = default;
            _testStartTime = DateTime.UtcNow;
            _testCts = new CancellationTokenSource();
            _testCts.CancelAfter(TimeSpan.FromSeconds(35));

            // Capture values at click time so mutation during flight is harmless.
            var captured = (Provider: profile.Provider,
                            ApiKey: profile.ApiKey,
                            Model: profile.Model,
                            BaseUrl: profile.BaseUrl);

            var ct = _testCts.Token;

            _pendingTest = Task.Run(() => RunTestCoreAsync(captured, ct), ct);
            EditorApplication.update += PollTest;
        }

        private void CancelTest()
        {
            if (_testCts != null)
            {
                _testCts.Cancel();
                _testCts.Dispose();
                _testCts = null;
            }

            if (_pendingTest != null)
            {
                EditorApplication.update -= PollTest;
                _pendingTest = null;
            }

            if (_testStage == TestStage.Running)
                _testStage = TestStage.Idle;
        }

        private void PollTest()
        {
            if (_pendingTest == null || !_pendingTest.IsCompleted) return;

            EditorApplication.update -= PollTest;

            if (_pendingTest.IsFaulted)
            {
                _testStage = TestStage.Error;
                var ex = _pendingTest.Exception;
                _testError = ex?.InnerException != null
                    ? $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
                    : ex?.Message ?? "\u672A\u77E5\u9519\u8BEF";
            }
            else if (_pendingTest.IsCanceled)
            {
                _testStage = TestStage.Idle;
            }
            else
            {
                _testStage = TestStage.Success;
                _testResultContent = _pendingTest.Result.Content;
                _testUsage = _pendingTest.Result.Usage;
            }

            _pendingTest = null;
            Repaint();
        }

        // ----------------------------------------------------------------
        //  Test HTTP request (pure background thread, no Unity/ACLS runtime deps)
        // ----------------------------------------------------------------

        private static async Task<TestResult> RunTestCoreAsync(
            (LlmProvider Provider, string ApiKey, string Model, string BaseUrl) p,
            CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            if (p.Provider == LlmProvider.Anthropic)
                return await CallAnthropicAsync(http, p.ApiKey, p.Model, p.BaseUrl, ct);
            else
                return await CallOpenAiCompatibleAsync(http, p.ApiKey, p.Model, p.BaseUrl, ct);
        }

        private static async Task<TestResult> CallAnthropicAsync(
            HttpClient http, string apiKey, string model, string baseUrl, CancellationToken ct)
        {
            string endpoint = string.IsNullOrWhiteSpace(baseUrl)
                ? "https://api.anthropic.com/v1/messages"
                : baseUrl.TrimEnd('/') + "/v1/messages";

            var bodyObj = new
            {
                model,
                max_tokens = 256,
                system = "\u4F60\u662F\u4E00\u4E2A\u52A9\u624B\u3002",
                messages = new[]
                {
                    new { role = "user",
                           content = "\u8BF7\u7528\u4E00\u53E5\u8BDD\u56DE\u590D\u201C\u4F60\u597D\uFF0C\u8FDE\u63A5\u6D4B\u8BD5\u201D\uFF0C\u5E76\u8BF4\u660E\u4F60\u7684\u6A21\u578B\u540D\u79F0\u3002" }
                }
            };

            string json = JsonConvert.SerializeObject(bodyObj);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            string respText = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {Truncate(respText, 500)}");

            var parsed = JObject.Parse(respText);
            var content = parsed["content"] as JArray;
            if (content == null || content.Count == 0)
                throw new HttpRequestException("响应缺 content 字段");

            var sb = new StringBuilder();
            foreach (var block in content)
                if ((string)block["type"] == "text")
                    sb.Append((string)block["text"]);

            var usage = new LlmUsage();
            if (parsed["usage"] != null)
            {
                usage.InputTokens = (int?)parsed["usage"]["input_tokens"] ?? 0;
                usage.OutputTokens = (int?)parsed["usage"]["output_tokens"] ?? 0;
            }

            return new TestResult { Content = sb.ToString(), Usage = usage };
        }

        private static async Task<TestResult> CallOpenAiCompatibleAsync(
            HttpClient http, string apiKey, string model, string baseUrl, CancellationToken ct)
        {
            string url = baseUrl.TrimEnd('/') + "/v1/chat/completions";

            var bodyObj = new
            {
                model,
                max_tokens = 256,
                messages = new[]
                {
                    new { role = "system",
                           content = "\u4F60\u662F\u4E00\u4E2A\u52A9\u624B\u3002\u8BF7\u7528 JSON \u683C\u5F0F\u56DE\u590D\u3002" },
                    new { role = "user",
                           content = $"\u56DE\u590D JSON\uFF1A{{\"status\":\"ok\",\"model\":\"{model}\"}}" }
                },
                response_format = new { type = "json_object" }
            };

            string json = JsonConvert.SerializeObject(bodyObj);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            string respText = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {Truncate(respText, 500)}");

            var parsed = JObject.Parse(respText);
            var choices = parsed["choices"] as JArray;
            if (choices == null || choices.Count == 0)
                throw new HttpRequestException("响应缺 choices 字段");

            string content = (string)choices[0]?["message"]?["content"];
            if (content == null)
                throw new HttpRequestException("响应缺 choices[0].message.content");

            var usage = new LlmUsage();
            if (parsed["usage"] != null)
            {
                usage.InputTokens = (int?)parsed["usage"]["prompt_tokens"] ?? 0;
                usage.OutputTokens = (int?)parsed["usage"]["completion_tokens"] ?? 0;
            }

            return new TestResult { Content = content, Usage = usage };
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "\u2026";
    }
}
