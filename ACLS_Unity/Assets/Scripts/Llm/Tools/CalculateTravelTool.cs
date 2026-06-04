using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 计算两地间距离和行军时间的工具。
    /// LLM 在叙事中涉及赶路、行军时调用此工具获取准确数据。
    ///
    /// 参数：
    ///   from (string, 必填): 出发地
    ///   to   (string, 必填): 目的地
    ///   mode (string, 可选): 交通方式——骑马/步行/急行/快马/驿马/乘船/顺水/逆水/车队。默认骑马
    /// </summary>
    public sealed class CalculateTravelTool : ILlmTool
    {
        public string Name => "calculate_travel";
        public string Description => "计算东汉两个地点之间的距离（汉里）和行军时间。"
            + "当玩家询问\u201C走多久\u201D\u201C多远\u201D或叙事中需要赶路时调用。"
            + "mode 可选值：骑马（默认）、步行、急行、快马、驿马、乘船、顺水、逆水、车队。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["from"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "出发地地名，如\u201C成都\u201D、\u201C武阳\u201D、\u201C洛阳\u201D",
                },
                ["to"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "目的地的名，如\u201C江州\u201D、\u201C长安\u201D",
                },
                ["mode"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "交通方式：骑马（默认）、步行、急行、快马、驿马、乘船、顺水、逆水、车队",
                    ["enum"] = new JArray { "骑马", "步行", "急行", "快马", "驿马", "乘船", "顺水", "逆水", "车队" },
                },
            },
            ["required"] = new JArray { "from", "to" },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            string from = "";
            string to = "";
            string mode = "骑马";

            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                from = (string)args["from"] ?? "";
                to = (string)args["to"] ?? "";
                mode = (string)args["mode"] ?? "骑马";
            }
            catch
            {
                return Task.FromResult("参数解析失败，请提供 from（出发地）和 to（目的地）。");
            }

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return Task.FromResult("缺少必要参数：from（出发地）和 to（目的地）。");

            var result = TravelCalculator.GetTravelDetail(from.Trim(), to.Trim(), mode.Trim());
            return Task.FromResult(result);
        }
    }
}
