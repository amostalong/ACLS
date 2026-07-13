using System;
using System.Collections.Generic;

namespace ACLS.Sim
{
    // Top-level simulation state. Pure POCO, no UnityEngine references.
    // Future save root: serialize this entire object graph.
    [Serializable]
    public sealed class World : IWorldReader
    {
        // 日期初始为 0,0,0 — 未初始化。世界构建阶段从 LLM 拿到 start_date 后写入。
        public GameDate Date = default(GameDate);
        public bool Paused = true;
        public int PlayerCharacterId;
        public int Gold = 100;

        public int NextCharacterId = 1;
        public int NextLocationId = 1;
        public int NextFactionId = 1;

        public List<Character> CharacterList = new List<Character>();
        public List<Location> LocationList = new List<Location>();
        public List<Faction> FactionList = new List<Faction>();

        public RelationGraph Relations = new RelationGraph();
        public List<PendingEvent> EventQueue = new List<PendingEvent>();

        // Cooldowns: (eventId, actorId) → date when next firing is allowed.
        // Stored as list of triplets so JsonUtility can round-trip it.
        [Serializable]
        public struct CooldownEntry
        {
            public string EventDefId;
            public int ActorId;
            public GameDate AvailableAt;
        }
        public List<CooldownEntry> Cooldowns = new List<CooldownEntry>();

        public List<string> Flags = new List<string>();

        // L1-L4 tiered world context built during new-game setup by LLM.
        public WorldStageData Stage = new WorldStageData();

        // 时代大势(主线)运行时状态:阶段名 + 触发锚点 + 前兆注入表。
        public EraTrendState EraTrend = new EraTrendState();

        // Narrative memory as string-JSON. Format: {"entries":[{"date":"...","event":"..."},...]}
        public string MemoryJson = "{}";

        [NonSerialized] private Dictionary<int, Character> charIndex;
        [NonSerialized] private Dictionary<int, Location> locIndex;
        [NonSerialized] private Dictionary<int, Faction> facIndex;

        public event Action OnDayTick;
        public event Action OnMonthTick;
        public event Action OnYearTick;
        public event Action<Character, Character> OnPlayerSwitched;  // (deceased, heir)
        public event Action OnPlayerSet;                              // initial setup (null → first player)
        public event Action<Character> OnGameOver;                    // last player char
        public event Action<PendingEvent> OnEventQueued;

        public void NotifyPlayerSet() => OnPlayerSet?.Invoke();

        // -------- accessors --------

        public Character GetCharacter(int id)
        {
            if (id == 0) return null;
            EnsureIndices();
            return charIndex.TryGetValue(id, out var c) ? c : null;
        }

        public Location GetLocation(int id)
        {
            if (id == 0) return null;
            EnsureIndices();
            return locIndex.TryGetValue(id, out var l) ? l : null;
        }

        public Faction GetFaction(int id)
        {
            if (id == 0) return null;
            EnsureIndices();
            return facIndex.TryGetValue(id, out var f) ? f : null;
        }

        public Character Player => GetCharacter(PlayerCharacterId);

        // -------- IWorldReader --------

        GameDate IWorldReader.CurrentDate => Date;
        int IWorldReader.Gold => Gold;
        Character IWorldReader.GetPlayerCharacter() => Player;
        int IWorldReader.GetOpinion(int fromId, int toId) => Relations.Opinion(fromId, toId);

        public IEnumerable<Character> AliveCharacters()
        {
            for (int i = 0; i < CharacterList.Count; i++)
                if (CharacterList[i].IsAlive) yield return CharacterList[i];
        }

        // -------- mutators --------

        public Character AddCharacter(Character c)
        {
            if (c.Id == 0) c.Id = NextCharacterId++;
            else if (c.Id >= NextCharacterId) NextCharacterId = c.Id + 1;
            CharacterList.Add(c);
            EnsureIndices();
            charIndex[c.Id] = c;
            return c;
        }

        public Location AddLocation(Location l)
        {
            if (l.Id == 0) l.Id = NextLocationId++;
            else if (l.Id >= NextLocationId) NextLocationId = l.Id + 1;
            LocationList.Add(l);
            EnsureIndices();
            locIndex[l.Id] = l;
            return l;
        }

        public Faction AddFaction(Faction f)
        {
            if (f.Id == 0) f.Id = NextFactionId++;
            else if (f.Id >= NextFactionId) NextFactionId = f.Id + 1;
            FactionList.Add(f);
            EnsureIndices();
            facIndex[f.Id] = f;
            return f;
        }

        public void EnqueueEvent(PendingEvent ev)
        {
            EventQueue.Add(ev);
            OnEventQueued?.Invoke(ev);
        }

        // -------- tick --------

        public void Tick()
        {
            var prev = Date;
            Date = Date.AddDays(1);
            OnDayTick?.Invoke();
            ProcessDeaths();
            if (!Date.IsSameMonthAs(prev)) OnMonthTick?.Invoke();
            if (!Date.IsSameYearAs(prev)) OnYearTick?.Invoke();
        }

        private void ProcessDeaths()
        {
            // Snapshot to a local list since KillCharacter may mutate state.
            int n = CharacterList.Count;
            for (int i = 0; i < n; i++)
            {
                var c = CharacterList[i];
                if (!c.IsAlive) continue;
                if (c.AgeAt(Date) >= c.LifespanYears)
                {
                    KillCharacter(c.Id);
                }
            }
        }

        public void KillCharacter(int id)
        {
            var c = GetCharacter(id);
            if (c == null || c.IsDead) return;
            c.IsDead = true;
            c.Death = Date;

            if (id == PlayerCharacterId)
            {
                if (TryFindHeir(id, out int heirId))
                {
                    var heir = GetCharacter(heirId);
                    PlayerCharacterId = heirId;
                    OnPlayerSwitched?.Invoke(c, heir);
                }
                else
                {
                    OnGameOver?.Invoke(c);
                }
            }
        }

        // 嫡长子继承制：在世儿子（按出生先后） → 在世兄弟 → 无 → Game Over。
        // 仅在玩家家族（IsHistorical==false）内查找。
        public bool TryFindHeir(int deadCharId, out int heirId)
        {
            heirId = 0;
            var dead = GetCharacter(deadCharId);
            if (dead == null) return false;

            Character best = null;
            foreach (var childId in dead.ChildrenIds)
            {
                var child = GetCharacter(childId);
                if (child == null || child.IsDead) continue;
                if (child.IsHistorical) continue;
                if (child.Sex != Sex.Male) continue;
                if (best == null || child.Birth < best.Birth) best = child;
            }
            if (best != null) { heirId = best.Id; return true; }

            // Brothers: same father, alive, in player family
            if (dead.FatherId != 0)
            {
                var father = GetCharacter(dead.FatherId);
                if (father != null)
                {
                    foreach (var sibId in father.ChildrenIds)
                    {
                        if (sibId == deadCharId) continue;
                        var sib = GetCharacter(sibId);
                        if (sib == null || sib.IsDead) continue;
                        if (sib.IsHistorical) continue;
                        if (sib.Sex != Sex.Male) continue;
                        if (best == null || sib.Birth < best.Birth) best = sib;
                    }
                }
            }
            if (best != null) { heirId = best.Id; return true; }

            return false;
        }

        // -------- flags --------

        public bool HasFlag(string f) => Flags != null && Flags.Contains(f);

        public void SetFlag(string f)
        {
            if (!string.IsNullOrEmpty(f) && !HasFlag(f)) Flags.Add(f);
        }

        public void ClearFlag(string f) => Flags?.Remove(f);

        // -------- cooldown --------

        public bool IsOnCooldown(string eventDefId, int actorId)
        {
            for (int i = 0; i < Cooldowns.Count; i++)
            {
                var e = Cooldowns[i];
                if (e.EventDefId == eventDefId && e.ActorId == actorId)
                    return Date < e.AvailableAt;
            }
            return false;
        }

        public void SetCooldown(string eventDefId, int actorId, GameDate availableAt)
        {
            for (int i = 0; i < Cooldowns.Count; i++)
            {
                if (Cooldowns[i].EventDefId == eventDefId && Cooldowns[i].ActorId == actorId)
                {
                    var e = Cooldowns[i];
                    e.AvailableAt = availableAt;
                    Cooldowns[i] = e;
                    return;
                }
            }
            Cooldowns.Add(new CooldownEntry { EventDefId = eventDefId, ActorId = actorId, AvailableAt = availableAt });
        }

        // -------- internals --------

        private void EnsureIndices()
        {
            if (charIndex == null || charIndex.Count != CharacterList.Count)
            {
                charIndex = new Dictionary<int, Character>(CharacterList.Count);
                for (int i = 0; i < CharacterList.Count; i++) charIndex[CharacterList[i].Id] = CharacterList[i];
            }
            if (locIndex == null || locIndex.Count != LocationList.Count)
            {
                locIndex = new Dictionary<int, Location>(LocationList.Count);
                for (int i = 0; i < LocationList.Count; i++) locIndex[LocationList[i].Id] = LocationList[i];
            }
            if (facIndex == null || facIndex.Count != FactionList.Count)
            {
                facIndex = new Dictionary<int, Faction>(FactionList.Count);
                for (int i = 0; i < FactionList.Count; i++) facIndex[FactionList[i].Id] = FactionList[i];
            }
        }
    }
}
