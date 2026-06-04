using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using ACLS.Sim;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 记忆阅读工具。LLM 在构建 L1 场景或推进剧情时调用此工具了解最近发生的事件。
    /// </summary>
    public sealed class ReadMemoryTool : ILlmTool
    {
        private readonly World world;

        public ReadMemoryTool(World world)
        {
            this.world = world;
        }

        public string Name => "read_memory";
        public string Description => "读取最近的叙事记忆。返回最近发生的事件列表（含日期），"
            + "帮助理解当前剧情进展。构建L1场景前应当调用此工具了解近期脉络。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["count"] = new JObject
                {
                    ["type"] = "integer",
                    ["description"] = "要读取的最近事件条数，默认10，最大50",
                },
            },
            ["required"] = new JArray { },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            int count = 10;
            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                if (args["count"] != null)
                    count = (int)args["count"];
            }
            catch { }

            if (count <= 0) count = 10;
            if (count > 50) count = 50;

            string result = GameMemory.ReadRecent(world, count);
            return Task.FromResult(result);
        }
    }
}
