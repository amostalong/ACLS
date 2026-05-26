using System;
using System.Collections.Generic;

namespace ACLS.Sim
{
    [Serializable]
    public sealed class Character
    {
        public int Id;
        public string Name = "";
        public string Courtesy = "";   // 字
        public Sex Sex;
        public GameDate Birth;
        public GameDate Death;          // meaningful only when IsDead
        public bool IsDead;
        public Stats Stats;
        public List<int> Traits = new List<int>();
        public int FatherId;
        public int MotherId;
        public int SpouseId;
        public List<int> ChildrenIds = new List<int>();
        public Identity Identity = new Identity();
        public bool IsHistorical;
        public int LifespanYears = 70;  // dies on the LifespanYears-th birthday unless killed earlier

        // Expanded background (populated by LLM after character creation)
        public string BackgroundStory = "";
        public string Values = "";
        public string CurrentGoal = "";
        public string Secret = "";
        public List<string> Connections = new List<string>();
        public List<string> KnownFacts = new List<string>();
        public List<string> OwnedItems = new List<string>();

        public bool IsAlive => !IsDead;

        public int AgeAt(GameDate date) => date.YearsSince(Birth);
    }
}
