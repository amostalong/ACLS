using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 人物查询工具。LLM 在叙事中需要了解某个角色的背景、性格、所知信息时调用。
    ///
    /// 参数：
    ///   name (string, 必填): 人物名称，如"阿虎头"、"林雪焉"、"清虎"
    /// </summary>
    public sealed class LookupCharacterTool : ILlmTool
    {
        public string Name => "lookup_character";
        public string Description => "查询三国人物的详细档案，包括身份、位置、关系值、行迹、目标、所知信息等。"
            + "当玩家问起某人、或叙事中需要了解NPC背景时调用。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["name"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "人物名称，如\u201C阿虎头\u201D、\u201C林雪焉\u201D、\u201C清虎\u201D",
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
                return Task.FromResult("参数解析失败，请提供 name（人物名称）。");
            }

            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult("缺少必要参数：name（人物名称）。");

            var result = GameDataLoader.FindCharacter(name.Trim());
            if (result == null)
                return Task.FromResult($"未找到人物「{name.Trim()}」的档案。");

            return Task.FromResult(result);
        }
    }
}
