using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using ACLS.Sim;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 记忆写入工具。LLM 在每次叙事回合结束后调用此工具记录重要事件。
    /// 每次调用追加一条记录。日期格式如"中平二年·七月十九·午后"。
    /// </summary>
    public sealed class WriteMemoryTool : ILlmTool
    {
        private readonly World world;

        public WriteMemoryTool(World world)
        {
            this.world = world;
        }

        public string Name => "write_memory";
        public string Description => "记录一条叙事记忆。每次事件/决策/重要变化发生后调用，"
            + "将关键信息存入记忆供后续参考。date为游戏内日期，event为事件简述。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["date"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "游戏内日期，如\u201C中平二年·七月十九·午后\u201D",
                },
                ["event"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "事件简述，1-3句话概括发生了什么",
                },
            },
            ["required"] = new JArray { "date", "event" },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            string date = "";
            string eventText = "";

            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                date = (string)args["date"] ?? "";
                eventText = (string)args["event"] ?? "";
            }
            catch
            {
                return Task.FromResult("参数解析失败，请提供 date（日期）和 event（事件）。");
            }

            if (string.IsNullOrWhiteSpace(date))
                return Task.FromResult("缺少必要参数：date（日期）。");
            if (string.IsNullOrWhiteSpace(eventText))
                return Task.FromResult("缺少必要参数：event（事件）。");

            GameMemory.Append(world, date.Trim(), eventText.Trim());
            return Task.FromResult($"✅ 已记录记忆：{date} — {eventText}");
        }
    }
}
