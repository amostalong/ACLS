using System.Threading;
using System.Threading.Tasks;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// LLM 可调用的工具接口。所有工具实现此接口后注册到 ToolRegistry，
    /// 即可在 LLM 对话中通过 function calling / tool_use 被调用。
    ///
    /// 实现示例见 CalculateTravelTool。
    /// </summary>
    public interface ILlmTool
    {
        /// <summary>工具名称，LLM 用此名称引用。下划线命名，如 "calculate_travel"。</summary>
        string Name { get; }

        /// <summary>工具描述，LLM 根据此描述决定何时调用。清晰说明用途和参数。</summary>
        string Description { get; }

        /// <summary>
        /// 参数的 JSON Schema（JObject）。
        /// Anthropic input_schema / OpenAI parameters 共用此格式。
        /// </summary>
        object InputSchema { get; }

        /// <summary>
        /// 执行工具。argsJson 为 LLM 传入的参数 JSON。
        /// 返回结果为字符串（工具执行结果，将作为文本返回给 LLM）。
        /// </summary>
        Task<string> ExecuteAsync(string argsJson, CancellationToken ct);
    }
}
