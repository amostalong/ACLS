using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 工具的序列化表示，用于传给 LLM API 的 tools 参数。
    /// 从 ILlmTool 自动生成，适用于 Anthropic 和 OpenAI 两种格式。
    /// </summary>
    public sealed class ToolDefinition
    {
        public string Name { get; }
        public string Description { get; }
        public JObject InputSchema { get; }

        public ToolDefinition(string name, string description, JObject inputSchema)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
        }

        /// <summary>从 ILlmTool 创建 ToolDefinition。</summary>
        public static ToolDefinition From(ILlmTool tool) =>
            new ToolDefinition(tool.Name, tool.Description, (JObject)tool.InputSchema);
    }
}
