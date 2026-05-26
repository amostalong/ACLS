using System.Collections.Generic;

namespace ACLS.Sim
{
    public static class Conditions
    {
        public static bool EvalAll(IList<ConditionExpr> conditions, World world, Character actor)
        {
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Count; i++)
            {
                bool ok = EvalOne(conditions[i], world, actor);
                if (conditions[i].Negate) ok = !ok;
                if (!ok) return false;
            }
            return true;
        }

        private static bool EvalOne(ConditionExpr c, World world, Character actor)
        {
            switch (c.Kind)
            {
                case ConditionKind.ActorAgeAtLeast:
                    return actor != null && actor.AgeAt(world.Date) >= c.IntArg;
                case ConditionKind.ActorAgeAtMost:
                    return actor != null && actor.AgeAt(world.Date) <= c.IntArg;
                case ConditionKind.ActorHasTrait:
                    return actor != null && actor.Traits.Contains(c.IntArg);
                case ConditionKind.ActorMissingTrait:
                    return actor != null && !actor.Traits.Contains(c.IntArg);
                case ConditionKind.ActorIsPlayer:
                    return actor != null && actor.Id == world.PlayerCharacterId;
                case ConditionKind.ActorIsHistorical:
                    return actor != null && actor.IsHistorical;
                case ConditionKind.ActorIsAlive:
                    return actor != null && actor.IsAlive;
                case ConditionKind.ActorFactionEquals:
                    return actor != null && actor.Identity != null && actor.Identity.FactionId == c.IntArg;
                case ConditionKind.ActorTitleEquals:
                    return actor != null && actor.Identity != null && actor.Identity.TitleId == c.IntArg;
                case ConditionKind.DateAtOrAfter:
                    return world.Date >= new GameDate(c.IntArg, c.IntArg2, c.IntArg3);
                case ConditionKind.DateBefore:
                    return world.Date < new GameDate(c.IntArg, c.IntArg2, c.IntArg3);
                case ConditionKind.Probability:
                    return Rng.Chance(c.IntArg);
                case ConditionKind.ActorSexIs:
                    return actor != null && (int)actor.Sex == c.IntArg;
                default:
                    return false;
            }
        }
    }
}
