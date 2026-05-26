using System;
using System.Collections.Generic;

namespace ACLS.Sim
{
    [Serializable]
    public sealed class Faction
    {
        public int Id;
        public string Name = "";
        public int LeaderCharacterId;
        public List<int> OwnedLocationIds = new List<int>();
    }
}
