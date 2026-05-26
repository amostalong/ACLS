using System;

namespace ACLS.Sim
{
    public enum EffectKind
    {
        // IntArg = StatAxis as int, IntArg2 = delta
        AdjustStat,

        // IntArg2 = delta
        AdjustGold,

        // IntArg = TraitDef.Id
        AddTrait,
        RemoveTrait,

        // IntArg = target charId, IntArg2 = delta, IntArg3 = direction (0 = actor→target, 1 = target→actor, 2 = both)
        AdjustOpinion,

        // IntArg = titleId, IntArg2 = locationId, IntArg3 = factionId (each 0 = leave unchanged, -1 = clear)
        ChangeIdentity,

        // IntArg = locationId
        MoveLocation,

        // IntArg = char id to kill (0 = actor)
        KillCharacter,

        // IntArg = TraitDef.Id (for newborn), IntArg2 = sex (0 male, 1 female; -1 random)
        SpawnChild,

        // IntArg = spouse charId (0 = generate fictional spouse)
        Marry,

        // StringArg = message
        Log,

        // StringArg = flag key
        SetFlag,
        ClearFlag,
    }

    [Serializable]
    public struct EffectOp
    {
        public EffectKind Kind;
        public int IntArg;
        public int IntArg2;
        public int IntArg3;
        public string StringArg;
    }
}
