using System.Collections.Generic;
using ACLS.Sim;

namespace ACLS.Authoring
{
    /// <summary>
    /// 新游戏角色预设。数据源为 PresetDatabaseSO（Assets/Content/Config/PresetDatabase.asset）。
    /// </summary>
    public static class NewGamePresets
    {
        public sealed class Preset
        {
            public string Id;
            public string Title;
            public string Era;
            public string Description;
            public string WorldBlurb;
            public string CharBlurb;
            public string LocationName;
            public int TraitId;
            public string TraitLabel;
            public bool IsCustom;

            // 预设角色数据（玩家）
            public string CharName;
            public string CharCourtesy;
            public int CharAge;
            public CharSex CharSex;
            public string CharBackgroundStory;
            public string CharValues;
            public string CharCurrentGoal;
            public string CharSecret;
        }

        private static PresetDatabaseSO _db;
        private static IReadOnlyList<Preset> _cached;

        private static PresetDatabaseSO Database
        {
            get
            {
                if (_db == null)
                    _db = ContentLoader.LoadSync<PresetDatabaseSO>(
                        "Assets/Content/Config/PresetDatabase.asset",
                        "Config/PresetDatabase");
                return _db;
            }
        }

        public static IReadOnlyList<Preset> All
        {
            get
            {
                if (_cached != null) return _cached;
                var db = Database;
                if (db == null || db.Presets == null || db.Presets.Count == 0)
                {
                    _cached = System.Array.Empty<Preset>();
                    return _cached;
                }
                var list = new List<Preset>();
                foreach (var e in db.Presets)
                {
                    if ((e.Lists & PresetList.NewGame) == 0) continue;
                    list.Add(new Preset
                    {
                        Id            = e.Id,
                        Title         = e.Title,
                        Era           = e.Era,
                        Description   = e.Description,
                        WorldBlurb    = e.WorldBlurb,
                        CharBlurb     = e.CharBlurb,
                        LocationName  = e.LocationName,
                        TraitId       = e.TraitId,
                        TraitLabel    = e.TraitLabel,
                        IsCustom      = e.IsCustom,
                        CharName            = e.CharName,
                        CharCourtesy        = e.CharCourtesy,
                        CharAge             = e.CharAge,
                        CharSex             = e.CharSex,
                        CharBackgroundStory = e.CharBackgroundStory,
                        CharValues          = e.CharValues,
                        CharCurrentGoal     = e.CharCurrentGoal,
                        CharSecret          = e.CharSecret,
                    });
                }
                _cached = list;
                return _cached;
            }
        }

        public static CharacterPresets.Preset ToCharacterPreset(Preset ng)
        {
            return new CharacterPresets.Preset
            {
                Id           = ng.Id,
                Title        = ng.Title,
                LocationName = ng.LocationName,
                TraitId      = ng.TraitId,
                TraitLabel   = ng.TraitLabel,
                Blurb        = ng.CharBlurb,
                CharName            = ng.CharName,
                CharCourtesy        = ng.CharCourtesy,
                CharAge             = ng.CharAge,
                CharSex             = ng.CharSex,
                CharBackgroundStory = ng.CharBackgroundStory,
                CharValues          = ng.CharValues,
                CharCurrentGoal     = ng.CharCurrentGoal,
                CharSecret          = ng.CharSecret,
            };
        }
    }
}
