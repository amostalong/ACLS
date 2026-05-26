using System;
using System.Collections.Generic;

namespace ACLS.Sim
{
    [Serializable]
    public sealed class Location
    {
        public int Id;
        public string Name = "";
        public string Region = "";
        public int OwnerFactionId;        // 0 = unowned
        public int Prosperity = 50;        // simplified scalar 0..100
        public List<int> CharactersPresent = new List<int>();
    }
}
