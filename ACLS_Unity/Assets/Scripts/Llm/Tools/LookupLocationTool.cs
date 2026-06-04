using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 地点查询工具。LLM 在叙事中需要了解某个地点的详细描述时调用。
    ///
    /// 参数：
    ///   name (string, 必填): 地点名称，如"武阳"、"安汉"、"金银沟"
    /// </summary>
    public sealed class LookupLocationTool : ILlmTool
    {
        public string Name => "lookup_location";
        public string Description => "查询东汉地点的详细档案，包括所属郡县、地理特征、人文风貌等。"
            + "当叙事中涉及某个地点，或玩家询问某个地方的情况时调用。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "地点名称，如\u201C武阳\u201D、\u201C安汉\u201D、\u201C金银沟\u201D",
                },
            },
            ["required"] = new JArray { "name" },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            string name = "";
            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                name = (string)args["name"] ?? "";
            }
            catch
            {
                return Task.FromResult("参数解析失败，请提供 name（地点名称）。");
            }

            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult("缺少必要参数：name（地点名称）。");

            var result = GameDataLoader.FindLocation(name.Trim());
            if (result == null)
                return Task.FromResult($"未找到地点「{name.Trim()}」的档案。");

            return Task.FromResult(result);
        }
    }
}
