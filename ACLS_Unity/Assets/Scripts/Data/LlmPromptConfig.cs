using UnityEngine;

namespace ACLS.Data
{
    // Configurable LLM prompts via external .md TextAssets.
    // Place .md files anywhere in Assets/ and drag them into the Inspector.
    // At runtime the text is read from the TextAsset reference; the
    // ScriptableObject itself does not store the prompt text.
    [CreateAssetMenu(menuName = "ACLS/LLM Prompt Config")]
    public sealed class LlmPromptConfig : ScriptableObject
    {
        [Header("角色拓展提示词 .md 文件（角色创建后调用一次）")]
        [Tooltip("拖入一个 .md 文本文件。运行时读取其内容作为角色拓展 SystemPrompt。")]
        public TextAsset CharacterExpansionMd;

        [Header("常规叙事提示词 .md 文件（每轮 LLM 调用均附带）")]
        [Tooltip("拖入一个 .md 文本文件。运行时读取其内容作为常规叙事 SystemPrompt。")]
        public TextAsset SystemPromptMd;

        // Runtime accessors — read from the referenced TextAsset.
        public string CharacterExpansionPrompt => CharacterExpansionMd != null ? CharacterExpansionMd.text : "";
        public string SystemPrompt => SystemPromptMd != null ? SystemPromptMd.text : "";
    }
}
