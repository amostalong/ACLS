using System;

namespace ACLS.Sim
{
    [Serializable]
    public sealed class PendingEvent
    {
        public string EventDefId;       // GameEventDef.Id
        public int ActorCharacterId;
        public int TargetCharacterId;   // optional
        public int LocationId;          // optional
    }
}
