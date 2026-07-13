using System.Collections.Generic;
using UnityEngine;
using ACLS.Data;
using ACLS.Loc;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // World boot data. Pre-builds the historical scaffolding that's independent
    // of the player's chosen persona: factions, locations, traits, placeholder
    // events. The player Character is created later by ConfigurePlayer() once
    // the player submits the character creation modal.
    public static class WorldFactory
    {
        public const int TRAIT_CAUTIOUS = 1;     // 谨慎
        public const int TRAIT_DECISIVE = 2;     // 果决
        public const int TRAIT_STUDIOUS = 3;     // 好学

        // Starting cities the player can pick as 出身地. For historical presets
        // these match what the LLM can recognise in 184 CE; 颍川 is technically a
        // 郡 but commonly named in narrative as the local seat. For sci-fi presets
        // the additional entries serve as sim-layer anchors. All start owned by
        // 后汉朝廷 (or the nearest equivalent for non-historical settings).
        public static readonly string[] StartingCities =
        {
            "洛阳", "长安", "邺", "许", "颍川", "襄阳", "江陵", "成都",
            "新希望站", "太虚城"
        };

        public static World BuildPlaceholderWorld()
        {
            var world = new World { Date = default(GameDate), Paused = true, Gold = 100 };

            var hanCourt = world.AddFaction(new Faction { Name = "后汉朝廷" });

            foreach (var city in StartingCities)
            {
                var loc = world.AddLocation(new Location
                {
                    Name = city,
                    Region = RegionFor(city),
                    OwnerFactionId = hanCourt.Id,
                    Prosperity = 60,
                });
                hanCourt.OwnedLocationIds.Add(loc.Id);
            }

            // No player yet — CharacterCreationView calls ConfigurePlayer
            // after the player submits the creation modal.
            return world;
        }

        // Creates the player Character from chat-driven setup data, attaches
        // it to the chosen location, applies the chosen trait, and sets
        // PlayerCharacterId. Fires World.OnPlayerSet so UI views refresh.
        public static void ConfigurePlayer(
            World world,
            string name,
            string courtesy,
            Sex sex,
            int age,
            string locationName,
            int traitId)
        {
            // Resolve location. Falls back to first city if the requested name
            // is missing (defensive).
            int locId = 0;
            for (int i = 0; i < world.LocationList.Count; i++)
            {
                if (world.LocationList[i].Name == locationName)
                {
                    locId = world.LocationList[i].Id;
                    break;
                }
            }
            if (locId == 0 && world.LocationList.Count > 0) locId = world.LocationList[0].Id;

            // Birth 暂留空，等 WorldBuild 阶段 LLM 输出 start_date 后回填。
            var player = world.AddCharacter(new Character
            {
                Name = string.IsNullOrWhiteSpace(name) ? "无名" : name.Trim(),
                Courtesy = (courtesy ?? "").Trim(),
                Sex = sex,
                Birth = default(GameDate),
                InitialAge = Mathf.Max(1, age),
                Stats = StartingStats(traitId),
                Identity = new Identity { LocationId = locId, FactionId = 0 },
                IsHistorical = false,
                LifespanYears = Rng.Range(58, 78),
            });
            if (traitId != 0) player.Traits.Add(traitId);

            world.PlayerCharacterId = player.Id;

            var loc = world.GetLocation(locId);
            if (loc != null) loc.CharactersPresent.Add(player.Id);

            world.NotifyPlayerSet();
        }

        // Stat baselines slightly tilted by the player's chosen trait so the
        // pick has mechanical weight from turn one.
        private static Stats StartingStats(int traitId)
        {
            var s = new Stats(8, 8, 9, 8, 8);
            switch (traitId)
            {
                case TRAIT_CAUTIOUS: s = new Stats(7, 9, 11, 9, 8); break;
                case TRAIT_DECISIVE: s = new Stats(11, 9, 8, 8, 9); break;
                case TRAIT_STUDIOUS: s = new Stats(7, 8, 12, 9, 8); break;
            }
            return s;
        }

        private static string RegionFor(string city) => city switch
        {
            "洛阳" or "长安" => "司隶",
            "邺" => "冀州",
            "许" or "颍川" => "豫州",
            "襄阳" or "江陵" => "荆州",
            "成都" => "益州",
            "新希望站" => "边缘星区",
            "太虚城" => "中州",
            _ => "",
        };

        public static IEnumerable<TraitDef> BuildPlaceholderTraits()
        {
            yield return MakeTrait(TRAIT_CAUTIOUS, "trait.cautious.name", "trait.cautious.desc",
                new Stats(0, 1, 1, 0, 0));
            yield return MakeTrait(TRAIT_DECISIVE, "trait.decisive.name", "trait.decisive.desc",
                new Stats(1, 1, 0, 0, 0));
            yield return MakeTrait(TRAIT_STUDIOUS, "trait.studious.name", "trait.studious.desc",
                new Stats(0, 0, 2, 0, 0));
        }

        public static IEnumerable<GameEventDef> BuildPlaceholderEvents()
        {
            // Step-2 has the LLM driving the narrative, so the hand-authored
            // periodic events are dormant for now. Returning none keeps the
            // dispatcher quiet but the pipeline intact for Phase 3 reuse.
            yield break;
        }

        public static void RegisterPlaceholderLocalization()
        {
            // Traits
            L10n.Add("trait.cautious.name", "谨慎");
            L10n.Add("trait.cautious.desc", "三思而行，逢险事先谋退路。");
            L10n.Add("trait.decisive.name", "果决");
            L10n.Add("trait.decisive.desc", "刀切豆腐，临阵不惑。");
            L10n.Add("trait.studious.name", "好学");
            L10n.Add("trait.studious.desc", "嗜书如命，闲来手不释卷。");

            // System / HUD
            L10n.Add("hud.pause", "已暂停");
            L10n.Add("hud.running", "运行中");
            L10n.Add("hud.speed.slow", "慢");
            L10n.Add("hud.speed.normal", "中");
            L10n.Add("hud.speed.fast", "快");
            L10n.Add("title.unranked", "白身");
            L10n.Add("char.panel.age", "岁");
            L10n.Add("char.panel.traits", "特质：");
            L10n.Add("char.panel.spouse", "妻");
            L10n.Add("char.panel.father", "父");
            L10n.Add("char.panel.mother", "母");
            L10n.Add("char.panel.children", "子女");

            L10n.Add("inheritance.title", "继任");
            L10n.Add("inheritance.desc", "{deceased} 故去。家中长子 {heir} 接续门楣。");
            L10n.Add("inheritance.choice", "（继续）");

            L10n.Add("gameover.title", "家道中落");
            L10n.Add("gameover.desc", "{deceased} 故去，家中再无男丁可继。门楣自此凋零。");
            L10n.Add("gameover.choice", "（结束）");
        }

        private static TraitDef MakeTrait(int id, string nameKey, string descKey, Stats mod)
        {
            var t = ScriptableObject.CreateInstance<TraitDef>();
            t.Id = id;
            t.DisplayNameKey = nameKey;
            t.DescriptionKey = descKey;
            t.StatModifier = mod;
            return t;
        }
    }
}
