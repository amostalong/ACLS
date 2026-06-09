using System.Collections.Generic;
using ACLS.Data;
using ACLS.Logging;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // 将 GameMemory 中的 LLM 生成实体回填到 Sim 层（World），
    // 使 NPC、势力、地点可以通过 name→ID 被 EffectParser 等机制索引。
    //
    // 调用时机：流水线步骤修改 GameMemory 之后。
    // 按 name 去重，同名实体不重复创建。
    public static class SimLayerSync
    {
        public static void Sync(World world)
        {
            var gm = GameMemory.Instance;
            if (gm == null) return;

            // Pass 1: places → locations（先于 faction/char，因为后者可能需要引用）
            SyncPlaces(world, gm);
            // Pass 2: factions
            SyncFactions(world, gm);
            // Pass 3: chars → characters（依赖 location/faction 已就位）
            SyncCharacters(world, gm);
        }

        // ──── Places ────

        private static void SyncPlaces(World world, GameMemory gm)
        {
            foreach (var pe in gm.Places)
                SyncPlace(world, pe);
        }

        internal static void SyncPlace(World world, PlaceEntry pe)
        {
            if (string.IsNullOrWhiteSpace(pe.name)) return;
            if (FindLocationByName(world, pe.name) != null) return;

            world.AddLocation(new Location
            {
                Name = pe.name.Trim(),
                Region = "",
                Prosperity = 50,
            });
            Log.Info(Log.Channels.Sim, "[SimSync] +Location: {0}", pe.name);
        }

        // ──── Factions ────

        private static void SyncFactions(World world, GameMemory gm)
        {
            foreach (var fe in gm.Factions)
                SyncFaction(world, fe);
        }

        internal static void SyncFaction(World world, FactionEntry fe)
        {
            if (string.IsNullOrWhiteSpace(fe.name)) return;
            if (FindFactionByName(world, fe.name) != null) return;

            world.AddFaction(new Faction
            {
                Name = fe.name.Trim(),
                LeaderCharacterId = 0,
            });
            Log.Info(Log.Channels.Sim, "[SimSync] +Faction: {0}", fe.name);
        }

        // ──── Characters ────

        private static void SyncCharacters(World world, GameMemory gm)
        {
            foreach (var ce in gm.Chars)
                SyncCharacter(world, ce);
        }

        internal static void SyncCharacter(World world, CharEntry ce)
        {
            if (string.IsNullOrWhiteSpace(ce.name)) return;
            if (FindCharacterByName(world, ce.name) != null) return;

            // 解析 location 字符串 → LocationId
            int locId = 0;
            if (!string.IsNullOrWhiteSpace(ce.location))
            {
                var loc = FindLocationByName(world, ce.location);
                if (loc != null) locId = loc.Id;
            }

            var c = world.AddCharacter(new Character
            {
                Name = ce.name.Trim(),
                Sex = Rng.Chance(50) ? Sex.Male : Sex.Female,
                Birth = RandomBirth(world.Date),
                Stats = RandomNpcStats(),
                Identity = new Identity { LocationId = locId, FactionId = 0 },
                IsHistorical = false,
                LifespanYears = Rng.Range(55, 78),
            });

            // 写入 location 的在场列表
            if (locId != 0)
            {
                var loc = world.GetLocation(locId);
                loc?.CharactersPresent.Add(c.Id);
            }

            Log.Info(Log.Channels.Sim, "[SimSync] +Character: {0} loc={1} id={2}", ce.name, locId, c.Id);
        }

        // ──── Name-based lookup ────

        internal static Character FindCharacterByName(World world, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            for (int i = 0; i < world.CharacterList.Count; i++)
            {
                if (world.CharacterList[i].Name == key) return world.CharacterList[i];
            }
            return null;
        }

        internal static Location FindLocationByName(World world, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            for (int i = 0; i < world.LocationList.Count; i++)
            {
                if (world.LocationList[i].Name == key) return world.LocationList[i];
            }
            return null;
        }

        internal static Faction FindFactionByName(World world, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            for (int i = 0; i < world.FactionList.Count; i++)
            {
                if (world.FactionList[i].Name == key) return world.FactionList[i];
            }
            return null;
        }

        private static Stats RandomNpcStats()
        {
            return new Stats(
                Rng.Range(4, 14),
                Rng.Range(4, 14),
                Rng.Range(4, 14),
                Rng.Range(4, 14),
                Rng.Range(4, 14)
            );
        }

        private static GameDate RandomBirth(GameDate now)
        {
            int year = now.Year - Rng.Range(20, 45);
            int month = Rng.Range(1, 12);
            int maxDay = month == 2 ? 28 : (month <= 7 ? (month % 2 == 1 ? 31 : 30) : (month % 2 == 0 ? 31 : 30));
            int day = Rng.Range(1, maxDay);
            return new GameDate(year, month, day);
        }
    }
}
