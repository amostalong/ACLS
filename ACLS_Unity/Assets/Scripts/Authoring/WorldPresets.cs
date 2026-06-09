using System.Collections.Generic;

namespace ACLS.Authoring
{
    /// <summary>
    /// 世界设定预设。数据源为 PresetDatabaseSO（Assets/Content/Config/PresetDatabase.asset）。
    /// </summary>
    public static class WorldPresets
    {
        public sealed class Preset
        {
            public string Id;
            public string Title;
            public string Era;
            public string Description;
            public string Blurb;
            public bool IsCustom;
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
                    if ((e.Lists & PresetList.World) == 0) continue;
                    list.Add(new Preset
                    {
                        Id          = e.Id,
                        Title       = e.Title,
                        Era         = e.Era,
                        Description = e.Description,
                        Blurb       = e.WorldBlurb,
                        IsCustom    = e.IsCustom,
                    });
                }
                _cached = list;
                return _cached;
            }
        }
    }
}
