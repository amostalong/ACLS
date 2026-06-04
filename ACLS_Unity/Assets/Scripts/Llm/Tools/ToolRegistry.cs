using System.Collections.Generic;
using System.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 工具注册中心。所有 LLM 可调用的工具在此注册，运行时按名查找。
    /// 单例模式，全局唯一实例。
    /// </summary>
    public sealed class ToolRegistry
    {
        public static readonly ToolRegistry Instance = new ToolRegistry();

        private readonly Dictionary<string, ILlmTool> _tools =
            new Dictionary<string, ILlmTool>();

        private ToolRegistry() { }  // singleton

        /// <summary>注册一个工具。同名工具会覆盖。</summary>
        public void Register(ILlmTool tool)
        {
            if (string.IsNullOrWhiteSpace(tool?.Name)) return;
            _tools[tool.Name] = tool;
        }

        /// <summary>按名称查找工具，未找到返回 null。</summary>
        public ILlmTool Get(string name)
        {
            _tools.TryGetValue(name, out var tool);
            return tool;
        }

        /// <summary>所有已注册工具的列表。</summary>
        public IReadOnlyList<ILlmTool> All => _tools.Values.ToList();

        /// <summary>转换为 API 可用的 ToolDefinition 列表。</summary>
        public List<ToolDefinition> GetAllDefinitions() =>
            _tools.Values.Select(ToolDefinition.From).ToList();

        /// <summary>清空所有注册（主要用于测试）。</summary>
        public void Clear() => _tools.Clear();
    }
}
