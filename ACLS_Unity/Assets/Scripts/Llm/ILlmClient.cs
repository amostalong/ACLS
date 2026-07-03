using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ACLS.Llm.Tools;

namespace ACLS.Llm
{
    public interface ILlmClient
    {
        // Returns the assistant content + token usage. Caller parses JSON /
        // does post-processing. Implementations should throw on HTTP errors
        // with a message suitable for surfacing to the user.
        // jsonObject: when true the provider is asked to constrain output to a
        // single JSON object (OpenAI response_format=json_object). Pass false for
        // free-form prose (narrator). Default true preserves legacy behavior.
        Task<LlmResponse> CompleteAsync(string systemPrompt,
                                        IReadOnlyList<ChatMessage> messages,
                                        CancellationToken ct,
                                        bool jsonObject = true);

        Task<LlmResponse> CompleteStreamAsync(string systemPrompt,
                                              IReadOnlyList<ChatMessage> messages,
                                              System.Action<string> onTextDelta,
                                              CancellationToken ct,
                                              bool jsonObject = true);

        /// <summary>
        /// 带工具调用（tool_use / function calling）的流式调用。
        /// 与 CompleteStreamAsync 的区别：
        ///   - 请求体包含 tools 定义
        ///   - 响应可能包含 tool_use 内容块（在 LlmResponse.ToolCalls 中返回）
        ///   - onTextDelta 只推送文本 delta，不推送 tool_use 块
        ///
        /// 调用方负责检查 resp.HasToolCalls，执行工具后追加消息再调此方法。
        /// </summary>
        Task<LlmResponse> CompleteStreamWithToolsAsync(
            string systemPrompt,
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            Action<string> onTextDelta,
            CancellationToken ct,
            bool jsonObject = true);
    }
}
