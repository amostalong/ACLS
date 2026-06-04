using System;

namespace ACLS.Llm
{
    public enum ChatRole { System, User, Assistant, ToolCall, ToolResult }

    [Serializable]
    public sealed class ChatMessage
    {
        // ---- 通用字段 ----
        public ChatRole Role;
        public string Content = "";    // 文本消息内容 / ToolCall 的参数字符串 / ToolResult 的结果字符串
        public DateTime At;

        // ---- ToolCall 相关 ----
        public string ToolCallId = ""; // LLM 返回的 tool_use id（如 "toolu_xxx"）
        public string ToolName = "";   // 工具名（ToolCall 角色时设置）

        public ChatMessage() { }

        public ChatMessage(ChatRole role, string content)
        {
            Role = role;
            Content = content ?? "";
            At = DateTime.Now;
        }

        /// <summary>创建工具调用消息（来自 LLM 的 tool_use）。</summary>
        public static ChatMessage ForToolCall(string toolCallId, string toolName, string argsJson)
        {
            return new ChatMessage(ChatRole.ToolCall, argsJson ?? "{}")
            {
                ToolCallId = toolCallId,
                ToolName = toolName,
            };
        }

        /// <summary>创建工具结果消息（返回给 LLM）。</summary>
        public static ChatMessage ForToolResult(string toolCallId, string result)
        {
            return new ChatMessage(ChatRole.ToolResult, result ?? "")
            {
                ToolCallId = toolCallId,
            };
        }
    }
}
