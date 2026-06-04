using System;
using System.Collections.Generic;
using ACLS.Logging;
using UnityEngine;

namespace ACLS.Data
{
    /// <summary>
    /// 游戏数据库运行时加载器。
    /// 从 Resources/Content/ 加载三个独立的 SO（CharacterDB / FactionDB / LocationDB），
    /// 构建为 Dictionary&lt;string, string&gt;（名称 → 完整内容），
    /// 供 LLM 工具查询。
    ///
    /// 动态实体数据由 GameMemory 管理，查询时 GameMemory 先查自身，
    /// 找不到再 fallback 到此处的静态 SO 数据。
    ///
    /// 用法：
    ///   GameDataLoader.Init();
    ///   string content = GameDataLoader.FindCharacterStatic("阿虎头");
    /// </summary>
    public static class GameDataLoader
    {
        private const string BasePath = "Content/";

        private static bool _loaded;

        // ---- 静态 SO 数据（markdown 文本） ----
        private static Dictionary<string, string> _characters;
        private static Dictionary<string, string> _factions;
        private static Dictionary<string, string> _locations;

        /// <summary>初始化/重新加载。游戏启动时调用一次。</summary>
        public static void Init()
        {
            _characters = LoadDict<CharacterDB>("CharacterDB", "人物");
            _factions   = LoadDict<FactionDB>("FactionDB", "势力");
            _locations  = LoadDict<LocationDB>("LocationDB", "地点");
            _loaded = true;

            Log.Info(Log.Channels.Content, "静态 SO 加载完成: 人物={0} 势力={1} 地点={2}", _characters.Count, _factions.Count, _locations.Count);
        }

        // ================================================================
        //  静态 SO 查询（由 GameMemory fallback 调用）
        // ================================================================

        /// <summary>按名称查询静态人物文本。仅供 GameMemory fallback 使用。</summary>
        public static string FindCharacterStatic(string name)
        {
            EnsureLoaded();
            _characters.TryGetValue(name?.Trim() ?? "", out var content);
            return content;
        }

        /// <summary>按名称查询静态势力文本。仅供 GameMemory fallback 使用。</summary>
        public static string FindFactionStatic(string name)
        {
            EnsureLoaded();
            _factions.TryGetValue(name?.Trim() ?? "", out var content);
            return content;
        }

        /// <summary>按名称查询静态地点文本。仅供 GameMemory fallback 使用。</summary>
        public static string FindLocationStatic(string name)
        {
            EnsureLoaded();
            _locations.TryGetValue(name?.Trim() ?? "", out var content);
            return content;
        }

        /// <summary>是否已加载。</summary>
        public static bool IsLoaded => _loaded;

        // ---- 内部 ----

        private static Dictionary<string, string> LoadDict<T>(string assetName, string label) where T : ScriptableObject
        {
            string path = BasePath + assetName;
            var db = Resources.Load<T>(path);
            if (db == null)
            {
                Log.Warn(Log.Channels.Content, "未找到 {0}.asset，请用菜单 ACLS > Import Game Data 导入", path);
                return new Dictionary<string, string>();
            }

            var field = typeof(T).GetField("Entries");
            var entries = field?.GetValue(db) as List<GameDataEntry>;
            if (entries == null)
            {
                Log.Warn(Log.Channels.Content, "{0}.asset 的 Entries 字段为空", assetName);
                return new Dictionary<string, string>();
            }

            var dict = new Dictionary<string, string>();
            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(e.Name) && !dict.ContainsKey(e.Name))
                    dict[e.Name] = e.Content ?? "";
            }
            return dict;
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Init();
        }
    }
}
