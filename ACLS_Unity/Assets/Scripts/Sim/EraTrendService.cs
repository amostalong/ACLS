using System.Collections.Generic;

namespace ACLS.Sim
{
    // 时代大势推进器。
    //
    // 职责:
    //   1. 每日/每次 World.Tick 后调用 AdvanceTo(date)
    //   2. 扫描所有未触发的硬锚点,按距今天数:
    //      - 到日 → 标记触发,记入 Timeline
    //      - 距 ≤ DaysBefore → 注入前兆(写盘去重)
    //   3. 更新 World.EraTrend.CurrentStageName 为当前所处阶段
    //
    // 不直接修改 L1/L2/L3 文本,而是把"前兆条目"作为结构化数据写出。
    // 上层(L1 builder / prompt assembler)按需读取并拼到对应 layer。
    public sealed class EraTrendService
    {
        private readonly World world;
        private readonly List<EraAnchorDef> anchors;
        private bool didFirstAdvance = false;

        public EraTrendService(World world, List<EraAnchorDef> anchors)
        {
            this.world = world;
            this.anchors = anchors ?? new List<EraAnchorDef>();
        }

        // 主入口:每日 tick 时调用一次。
        public void AdvanceTo(GameDate today)
        {
            if (world?.EraTrend == null) return;
            var state = world.EraTrend;

            // 启动补偿:首次调用时,如果 today 已在某些锚点的"前兆窗口"内或之后,
            // 把那些"本来该在过去注入"的前兆一次性追补(标 IsBackfill=true)。
            // 避免游戏开局时前兆全部丢失。
            if (!didFirstAdvance)
            {
                didFirstAdvance = true;
                BackfillMissedForeshadowing(state, today);
            }

            // 1) 更新当前阶段名(取已触发的最后一个锚点 stage name,否则空)
            state.CurrentStageName = ResolveCurrentStage(today);

            // 2) 扫描锚点:触发 + 前兆
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i];
                var trigger = a.TriggerDate;

                // 已过触发日 且 未记录 → 标记触发
                if (today >= trigger && !state.TriggeredAnchorIds.Contains(a.Id))
                {
                    state.TriggeredAnchorIds.Add(a.Id);
                    state.Timeline.Add(new TimelineLog
                    {
                        Date = today,
                        Kind = "anchor_triggered",
                        AnchorId = a.Id,
                        Detail = a.Title,
                    });
                }

                // 未触发 → 检查前兆(仅在窗口起点的那一天注入)
                if (today < trigger && a.Foreshadowing != null)
                {
                    for (int j = 0; j < a.Foreshadowing.Count; j++)
                    {
                        var rule = a.Foreshadowing[j];
                        int daysToTrigger = DaysBetween(today, trigger);
                        if (daysToTrigger != rule.DaysBefore) continue;
                        if (HasForeshadowing(state, a.Id, rule.TargetLayer, rule.DaysBefore)) continue;

                        AddForeshadowing(state, today, a.Id, rule);
                    }
                }
            }
        }

        // 启动补偿:对所有"今天已达或已过"锚点,一次性补注入所有前兆。
        // 标记 IsBackfill=1,LLM 可据此判断"是开局补入 vs 游戏中注入"。
        private void BackfillMissedForeshadowing(EraTrendState state, GameDate today)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i];
                if (today < a.TriggerDate) continue; // 还未到期的,正常逻辑会处理
                if (a.Foreshadowing == null) continue;
                for (int j = 0; j < a.Foreshadowing.Count; j++)
                {
                    var rule = a.Foreshadowing[j];
                    if (HasForeshadowing(state, a.Id, rule.TargetLayer, rule.DaysBefore)) continue;
                    AddForeshadowing(state, today, a.Id, rule, isBackfill: true);
                }
            }
        }

        private static void AddForeshadowing(EraTrendState state, GameDate today,
            string anchorId, ForeshadowingRule rule, bool isBackfill = false)
        {
            state.ForeshadowingInjected.Add(new ForeshadowingEntry
            {
                AnchorId = anchorId,
                TargetLayer = rule.TargetLayer,
                DaysBeforeAnchor = rule.DaysBefore,
                Template = isBackfill ? "[开局追补] " + rule.Template : rule.Template,
                InjectedAt = today,
            });
            state.Timeline.Add(new TimelineLog
            {
                Date = today,
                Kind = isBackfill ? "foreshadowing_backfilled" : "foreshadowing_injected",
                AnchorId = anchorId,
                Detail = $"[{rule.TargetLayer}] {rule.Template}",
            });
        }

        // 取当前所处阶段:遍历所有已触发的硬锚点,取 trigger 最大的那个 stage name。
        // 若一个都没触发,返回距今最近(且未触发)的 stage 名作为"下一阶段预告"。
        private string ResolveCurrentStage(GameDate today)
        {
            EraAnchorDef latest = null;
            EraAnchorDef nextUpcoming = null;
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i];
                if (today >= a.TriggerDate)
                {
                    if (latest == null || a.TriggerDate > latest.TriggerDate) latest = a;
                }
                else
                {
                    if (nextUpcoming == null || a.TriggerDate < nextUpcoming.TriggerDate) nextUpcoming = a;
                }
            }
            if (latest != null) return latest.StageName;
            return nextUpcoming != null ? $"（未起）{nextUpcoming.StageName}" : "";
        }

        private static bool HasForeshadowing(EraTrendState state, string anchorId, string layer, int daysBefore)
        {
            for (int i = 0; i < state.ForeshadowingInjected.Count; i++)
            {
                var f = state.ForeshadowingInjected[i];
                if (f.AnchorId == anchorId && f.TargetLayer == layer && f.DaysBeforeAnchor == daysBefore) return true;
            }
            return false;
        }

        private static int DaysBetween(GameDate a, GameDate b)
        {
            // b >= a 时,b - a 的天数
            int days = 0;
            var cur = a;
            while (cur < b)
            {
                cur = cur.AddDays(1);
                days++;
                if (days > 4000) break; // 防御
            }
            return days;
        }
    }
}
