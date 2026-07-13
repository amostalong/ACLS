using System.Collections.Generic;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Routes system-level effects parsed from an LLM response into the
    // game world.  Separates user-visible narration from world mutations.
    public sealed class EffectRouter
    {
        private readonly World world;

        public EffectRouter(World world)
        {
            this.world = world;
        }

        // Applies all system effects from a dialogue result.
        // Returns true if any state-affecting mutation happened.
        public bool Apply(DialogueResult result)
        {
            if (result?.Effects == null || result.Effects.Count == 0) return false;

            var ops = EffectParser.ParseAll(result.Effects, world);
            var player = world?.Player;
            if (player == null) return false;

            for (int i = 0; i < ops.Count; i++)
            {
                Effects.ApplyOne(ops[i], world, player);
            }

            // 时间推进：优先用 LLM 的绝对 Date，其次用 DaysPassed 逐天 Tick。
            // DaysPassed > 30 视为 LLM 异常跳跃，丢弃，避免一次推进几月。
            // world.Date.Year == 0 表示剧本起始日期还未由 LLM 初始化，拒绝推进。
            if (world == null || world.Date.Year <= 0) return ops.Count > 0;
            if (result.Date.HasValue && result.Date.Value > world.Date)
            {
                world.Date = result.Date.Value;
            }
            else if (result.DaysPassed > 0 && result.DaysPassed <= 30)
            {
                for (int d = 0; d < result.DaysPassed; d++)
                {
                    if (world.EventQueue.Count > 0) break;
                    world.Tick();
                }
            }

            return ops.Count > 0 || result.Date.HasValue || result.DaysPassed > 0;
        }

        // Interprets a suggested state string and returns the matching enum.
        public static DialogueStateType? ParseSuggestedState(string suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion)) return null;
            return DialogueStateType.StagePlay;
        }
    }
}
