using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACLS.Sim;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 时代大势(主线)阅读工具。LLM 在叙事/谋划时调用此工具读取当前阶段、近期锚点、已注入的前兆。
    ///
    /// 参数:
    ///   scope (string, 可选): "summary" / "upcoming" / "foreshadowing" / "all"。默认 "all"
    /// </summary>
    public sealed class ReadEraTrendTool : ILlmTool
    {
        private readonly World world;

        public ReadEraTrendTool(World world)
        {
            this.world = world;
        }

        public string Name => "read_era_trend";
        public string Description =>
            "读取时代大势(主线)信息。返回当前历史阶段、≤6月内即将触发的硬锚点、以及已注入但未触发的前兆。"
            + "叙事中需要呼应时代走向、或玩家询问'最近会出什么事'时调用。"
            + "scope: summary(当前阶段+最近锚点) | upcoming(6月内锚点列表) | foreshadowing(已注入前兆) | all(全部)";

        public object InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["scope"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "读取范围: summary / upcoming / foreshadowing / all。默认 all",
                    ["enum"] = new JArray { "summary", "upcoming", "foreshadowing", "all" },
                },
            },
            ["required"] = new JArray { },
        };

        public Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
        {
            string scope = "all";
            try
            {
                var args = JObject.Parse(argsJson ?? "{}");
                scope = ((string)args["scope"] ?? "all").Trim().ToLowerInvariant();
            }
            catch { }

            if (world == null)
                return Task.FromResult("世界尚未初始化。");

            var era = world.EraTrend;
            if (era == null)
                return Task.FromResult("时代大势系统未启用。");

            var sb = new StringBuilder();
            var today = world.Date;

            if (scope == "summary" || scope == "all")
            {
                sb.AppendLine($"【时代大势·概要】");
                sb.AppendLine($"- 当前日期：{today.ToLLMString()}");
                sb.AppendLine($"- 当前阶段：{(string.IsNullOrEmpty(era.CurrentStageName) ? "（无）" : era.CurrentStageName)}");
                sb.AppendLine($"- 已触发锚点数：{era.TriggeredAnchorIds.Count}");
            }

            if (scope == "upcoming" || scope == "all")
            {
                sb.AppendLine();
                sb.AppendLine("【≤6 月内未触发的硬锚点】");
                bool any = false;
                var anchors = era.ActiveAnchors ?? EraTrendAnchors.EmptyList;
                for (int i = 0; i < anchors.Count; i++)
                {
                    var a = anchors[i];
                    if (era.TriggeredAnchorIds.Contains(a.Id)) continue;
                    int daysTo = DaysBetween(today, a.TriggerDate);
                    if (daysTo < 0 || daysTo > 180) continue; // 只列 6 月内
                    any = true;
                    sb.AppendLine($"- [{a.TriggerDate.ToLLMString()}] (距今 {daysTo} 天) {a.Title}");
                    if (!string.IsNullOrWhiteSpace(a.Summary))
                        sb.AppendLine($"  概要：{a.Summary}");
                    if (a.FactionIds != null && a.FactionIds.Count > 0)
                        sb.AppendLine($"  涉及势力：{string.Join("、", a.FactionIds)}");
                }
                if (!any) sb.AppendLine("（无）");
            }

            if (scope == "foreshadowing" || scope == "all")
            {
                sb.AppendLine();
                sb.AppendLine("【已注入的前兆】");
                if (era.ForeshadowingInjected == null || era.ForeshadowingInjected.Count == 0)
                {
                    sb.AppendLine("（无）");
                }
                else
                {
                    int shown = 0;
                    for (int i = 0; i < era.ForeshadowingInjected.Count; i++)
                    {
                        var f = era.ForeshadowingInjected[i];
                        // 未来锚点的真前兆(锚点已触发后不再展示)，
                        // 但开局追补的(模板以"[开局追补]"开头)始终展示，供玩家拼出历史脉络。
                        if (!f.Template.StartsWith("[开局追补]") && era.TriggeredAnchorIds.Contains(f.AnchorId)) continue;
                        string tag = f.Template.StartsWith("[开局追补]") ? "[开局追补]" : $"[渗入{f.TargetLayer}]";
                        string body = f.Template.StartsWith("[开局追补]") ? f.Template.Substring("[开局追补]".Length) : f.Template;
                        sb.AppendLine($"- {tag} {f.AnchorId} (锚点前 {f.DaysBeforeAnchor} 日)");
                        sb.AppendLine($"  {body}");
                        shown++;
                    }
                    if (shown == 0) sb.AppendLine("（无）");
                }
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }

        private static int DaysBetween(GameDate a, GameDate b)
        {
            int days = 0;
            var cur = a;
            while (cur < b)
            {
                cur = cur.AddDays(1);
                days++;
                if (days > 4000) break;
            }
            return days;
        }
    }
}
