using System;

namespace ACLS.Sim
{
    public enum ConditionKind
    {
        ActorAgeAtLeast,    // IntArg = years
        ActorAgeAtMost,     // IntArg = years
        ActorHasTrait,      // IntArg = TraitDef.Id
        ActorMissingTrait,  // IntArg = TraitDef.Id
        ActorIsPlayer,
        ActorIsHistorical,
        ActorIsAlive,
        ActorFactionEquals, // IntArg = factionId
        ActorTitleEquals,   // IntArg = titleId
        DateAtOrAfter,      // IntArg/IntArg2/IntArg3 = year/month/day
        DateBefore,         // ditto
        Probability,        // IntArg = percent (0..100); roll each evaluation
        ActorSexIs,         // IntArg = (int)Sex
    }

    [Serializable]
    public struct ConditionExpr
    {
        public ConditionKind Kind;
        public int IntArg;
        public int IntArg2;
        public int IntArg3;
        public bool Negate;
    }
}
