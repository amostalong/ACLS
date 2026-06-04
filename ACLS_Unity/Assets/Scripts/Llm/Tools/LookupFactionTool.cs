using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 势力查询工具。LLM 在叙事中需要了解某个势力的成员、目标、状态时调用。
    ///
    /// 参数：
    ///   name (string, 必填): 势力名称，如"武阳青氏"、"阆中马氏"、"五斗米道·张修"
    /// </summary>
    public sealed class LookupFactionTool : ILlmTool
    {
        public string Name => "lookup_faction";
        public string Description => "查询三国势力的详细档案，包括成员构成、目标、资源、关系等。"
            + "当玩家问起某个势力、或叙事中涉及势力互动时调用。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "势力名称，如\u201C武阳青氏\u201D、\u201C阆中马氏\u201D、\u201C五斗米道·张修\u201D",
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
                return Task.FromResult("参数解析失败，请提供 name（势力名称）。");
            }

            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult("缺少必要参数：name（势力名称）。");

            var result = GameDataLoader.FindFaction(name.Trim());
            if (result == null)
                return Task.FromResult($"未找到势力「{name.Trim()}」的档案。");

            return Task.FromResult(result);
        }
    }
}
