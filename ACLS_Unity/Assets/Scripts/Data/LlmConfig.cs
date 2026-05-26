using System;
using System.Collections.Generic;
using UnityEngine;

namespace ACLS.Data
{
    public enum LlmProvider
    {
        Anthropic,
        OpenAiCompatible,
    }

    [Serializable]
    public sealed class LlmProfile
    {
        [Tooltip("Inspector 里显示的名称，便于区分多个 Profile。")]
        public string ProfileName = "New Profile";

        [Tooltip("当前激活的 Profile。同时只能有一个为 true；Editor 自动维护互斥。")]
        public bool IsActive = false;

        public LlmProvider Provider = LlmProvider.Anthropic;

        [Tooltip("API key。只存在本地 asset 里，.gitignore 已排除。")]
        public string ApiKey = "";

        [Tooltip("模型 ID。示例：claude-haiku-4-5-20251001 / gpt-4o-mini / deepseek-chat / ...")]
        public string Model = "claude-haiku-4-5-20251001";

        [Tooltip("代理/中转 base URL。Anthropic 官方留空；走中转（如 https://your-proxy/claude）或 OpenAI 兼容时必填。末尾不带斜杠。")]
        public string BaseUrl = "";

        [Range(0, 8192)]
        [Tooltip("输出长度限制。0 表示使用默认值（8192）。")]
        public int MaxTokens = 0;

        [Tooltip("把完整请求/响应 echo 到 Console，用于调试 prompt。")]
        public bool VerboseLogging = true;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey)
                                  && !string.IsNullOrWhiteSpace(Model)
                                  && (Provider != LlmProvider.OpenAiCompatible
                                      || !string.IsNullOrWhiteSpace(BaseUrl));
    }

    // Container asset. GameBootstrap loads this from Resources/LlmConfig.asset
    // and reads the convenience properties, which delegate to the active profile.
    [CreateAssetMenu(fileName = "LlmConfig", menuName = "ACLS/LLM Config")]
    public sealed class LlmConfig : ScriptableObject
    {
        [Tooltip("所有可用的 LLM Profile 列表。")]
        public List<LlmProfile> Profiles = new List<LlmProfile>();

        // --- convenience accessors (GameBootstrap reads these, no changes needed there) ---

        public LlmProfile Active => Profiles?.Find(p => p.IsActive);

        public bool IsConfigured    => Active?.IsConfigured ?? false;
        public LlmProvider Provider => Active?.Provider     ?? LlmProvider.Anthropic;
        public string ApiKey        => Active?.ApiKey       ?? "";
        public string Model         => Active?.Model        ?? "";
        public string BaseUrl       => Active?.BaseUrl      ?? "";
        public int MaxTokens        => Active?.MaxTokens    ?? 4000;
        public bool VerboseLogging  => Active?.VerboseLogging ?? false;
    }
}
