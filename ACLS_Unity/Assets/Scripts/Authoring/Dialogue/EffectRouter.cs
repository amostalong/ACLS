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

            // Advance time.
            if (result.DaysPassed > 0)
            {
                for (int d = 0; d < result.DaysPassed; d++)
                {
                    if (world.EventQueue.Count > 0) break;
                    world.Tick();
                }
            }

            // 如果 LLM 回复中带了日期，以此为准（覆盖 days_passed 的推进结果）
            if (result.Date.HasValue)
                world.Date = result.Date.Value;

            return ops.Count > 0 || result.DaysPassed > 0 || result.Date.HasValue;
        }

        // Interprets a suggested state string and returns the matching enum.
        public static DialogueStateType? ParseSuggestedState(string suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion)) return null;
            return DialogueStateType.StagePlay;
        }
    }
}
