using System.Collections.Generic;

namespace ACLS.Authoring
{
    /// <summary>
    /// 角色预设。数据源为 PresetDatabaseSO（Assets/Content/Config/PresetDatabase.asset）。
    /// Sex / name / courtesy 不在此预设中——由玩家在 UI 填写。
    /// </summary>
    public static class CharacterPresets
    {
        public sealed class Preset
        {
            public string Id;
            public string Title;
            public string LocationName;
            public int TraitId;
            public string TraitLabel;
            public string Blurb;

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
                    if ((e.Lists & PresetList.Character) == 0) continue;
                    list.Add(new Preset
                    {
                        Id           = e.Id,
                        Title        = e.Title,
                        LocationName = e.LocationName,
                        TraitId      = e.TraitId,
                        TraitLabel   = e.TraitLabel,
                        Blurb        = e.CharBlurb,
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
    }
}
