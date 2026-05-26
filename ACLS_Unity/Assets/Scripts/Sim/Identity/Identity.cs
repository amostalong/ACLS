using System;

namespace ACLS.Sim
{
    // ID convention: 0 means "none / unset" everywhere in the sim.
    // JsonUtility doesn't support nullable ints, so we use 0 as the sentinel.
    [Serializable]
    public sealed class Identity
    {
        public int TitleId;     // 0 = 白身 / no title
        public int LocationId;  // 0 = no specific location
        public int FactionId;   // 0 = unaffiliated

        public Identity Clone() => new Identity
        {
            TitleId = TitleId,
            LocationId = LocationId,
            FactionId = FactionId,
        };
    }
}
