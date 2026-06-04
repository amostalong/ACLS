using System;
using System.Collections.Generic;

namespace ACLS.Llm
{
    [Serializable]
    public struct LlmUsage
    {
        public int InputTokens;
        public int OutputTokens;

        public int Total => InputTokens + OutputTokens;
        public bool HasData => InputTokens > 0 || OutputTokens > 0;

        public static LlmUsage operator +(LlmUsage a, LlmUsage b) => new LlmUsage
        {
            InputTokens = a.InputTokens + b.InputTokens,
            OutputTokens = a.OutputTokens + b.OutputTokens,
        };
    }

    /// <summary>LLM 调用返回的工具调用请求。</summary>
    public sealed class LlmToolCall
    {
        public string Id = "";      // "toolu_xxx" (Anthropic) / "call_xxx" (OpenAI)
        public string Name = "";    // 工具名
        public string Args = "{}";  // 参数 JSON 字符串
    }

    /// <summary>LLM 调用的完整响应。</summary>
    public sealed class LlmResponse
    {
        /// <summary>累计的文本内容。有 tool_use 时可能为空。</summary>
        public string Content = "";

        /// <summary>Token 用量。</summary>
        public LlmUsage Usage;

        /// <summary>LLM 发起的工具调用列表（如果 LLM 请求调工具）。</summary>
        public List<LlmToolCall> ToolCalls = null;

        /// <summary>是否包含工具调用请求。</summary>
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;

        /// <summary>停止原因。由各客户端在流式结束时设置。</summary>
        public string StopReason = "";
    }
}
