using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACLS.Sim;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 玩家状态阅读工具。LLM 在构建 L1 场景时调用此工具获取玩家当前状态。
    /// </summary>
    public sealed class ReadPlayerStateTool : ILlmTool
    {
        private readonly World world;

        public ReadPlayerStateTool(World world)
        {
            this.world = world;
        }

        public string Name => "read_player_state";
        public string Description => "读取玩家的当前状态，包括姓名、年龄、性格、所在地、当前目标、背景故事等。"
            + "构建L1场景前应先调用此工具了解玩家信息。";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["field"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "可选：指定读取特定字段（name/location/date/goal/stats/all）。默认 all",
                },
            },
            ["required"] = new JArray { },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            string field = "all";
            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                field = (string)args["field"] ?? "all";
            }
            catch { }

            if (world == null)
                return Task.FromResult("世界尚未初始化。");

            var player = world.Player;
            if (player == null)
                return Task.FromResult("玩家尚未创建。");

            var sb = new StringBuilder();

            field = field.Trim().ToLowerInvariant();

            if (field == "all" || field == "name")
            {
                sb.AppendLine($"姓名：{player.Name}（{player.Courtesy}）");
                sb.AppendLine($"性别：{(player.Sex == Sex.Male ? "男" : "女")}");
                sb.AppendLine($"年龄：{player.AgeAt(world.Date)} 岁");
                if (!string.IsNullOrWhiteSpace(player.CurrentGoal))
                    sb.AppendLine($"当前目标：{player.CurrentGoal}");
            }

            if (field == "all" || field == "location")
            {
                var loc = world.GetLocation(player.Identity?.LocationId ?? 0);
                string locName = loc?.Name ?? "未知";
                sb.AppendLine($"所在位置：{locName}");
            }

            if (field == "all" || field == "date")
            {
                sb.AppendLine($"当前日期：{world.Date}（{world.Date.Year} 年 {world.Date.Month} 月 {world.Date.Day} 日）");
                sb.AppendLine($"岁差：{world.Date.YearsSince(new GameDate(184, 1, 1))} 年");
            }

            if (field == "all" || field == "stats")
            {
                sb.AppendLine($"武力={player.Stats.Wu} 统率={player.Stats.Tong} 智略={player.Stats.Zhi} 政治={player.Stats.Zheng} 魅力={player.Stats.Mei}");
                sb.AppendLine($"金钱：{world.Gold} 钱");
            }

            if (field == "all" || field == "goal")
            {
                if (!string.IsNullOrWhiteSpace(player.CurrentGoal))
                    sb.AppendLine($"当前目标：{player.CurrentGoal}");
                if (!string.IsNullOrWhiteSpace(player.Secret))
                    sb.AppendLine($"秘密：{player.Secret}");
                if (!string.IsNullOrWhiteSpace(player.Values))
                    sb.AppendLine($"价值观：{player.Values}");
            }

            if (field == "all")
            {
                if (!string.IsNullOrWhiteSpace(player.BackgroundStory))
                {
                    sb.AppendLine();
                    sb.AppendLine("背景故事：");
                    sb.AppendLine(player.BackgroundStory);
                }
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }
    }
}
