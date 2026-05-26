using System;

namespace ACLS.Sim
{
    // Layered world description. Each tier is a string ready to drop into a
    // user prompt. Tiers carry their own BuildAt timestamps so SnapshotBuilder
    // knows when to refresh:
    //
    //   T1 「当下」      — 主角 + 此刻同地的角色 + 今日时空。      每日重建。
    //   T2 「个人圈」    — 家眷 + 出身地详细 + 邻地。              每日重建。
    //   T3 「关注圈」    — 重要历史人物近况 + 派系动态。            每月重建。
    //   T4 「时代大势」  — 历史阶段定义 + 重大节点预告。            每年重建。
    //
    // Default LLM call sends T1 + T2 as the "状态" prefix to user messages.
    // T3 / T4 are heavier and reserved for skill-specific calls that
    // genuinely need them (long-term planning, court intrigue, etc).
    [Serializable]
    public sealed class WorldSnapshot
    {
        public string Tier1 = "";
        public string Tier2 = "";
        public string Tier3 = "";
        public string Tier4 = "";

        public GameDate Tier1BuiltAt;
        public GameDate Tier2BuiltAt;
        public GameDate Tier3BuiltAt;
        public GameDate Tier4BuiltAt;
    }

    [Flags]
    public enum SnapshotTiers
    {
        None = 0,
        T1 = 1,
        T2 = 2,
        T3 = 4,
        T4 = 8,
        Default = T1 | T2,        // every regular LLM call
        Deep    = T1 | T2 | T3,   // skills that want world depth
        Full    = T1 | T2 | T3 | T4,
    }
}
