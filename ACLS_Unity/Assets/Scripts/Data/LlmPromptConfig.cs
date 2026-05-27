using UnityEngine;
using UnityEngine.Serialization;

namespace ACLS.Data
{
    // Configurable LLM prompts via external .md TextAssets.
    // Place .md files anywhere in Assets/ and drag them into the Inspector.
    // At runtime the text is read from the TextAsset reference; the
    // ScriptableObject itself does not store the prompt text.
    [CreateAssetMenu(menuName = "ACLS/LLM Prompt Config")]
    public sealed class LlmPromptConfig : ScriptableObject
    {
        [FormerlySerializedAs("CharacterExpansionMd")]
        [Header("世界创建 .md 文件, 首次创建使用")]
        [Tooltip("拖入一个 .md 文本文件。运行时读取其内容作为角色拓展 SystemPrompt。")]
        public TextAsset WorldCreatePromptMd;

        [Header("常规叙事提示词 .md 文件（每轮 LLM 调用均附带）")]
        [Tooltip("拖入一个 .md 文本文件。运行时读取其内容作为常规叙事 SystemPrompt。")]
        public TextAsset SystemPromptMd;

        // Runtime accessors — read from the referenced TextAsset.
        public string WorldCreatePrompt => WorldCreatePromptMd != null ? WorldCreatePromptMd.text : "";
        public string SystemPrompt => SystemPromptMd != null ? SystemPromptMd.text : "";
    }
}
