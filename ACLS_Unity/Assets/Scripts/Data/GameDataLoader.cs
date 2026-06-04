using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace ACLS.Data
{
    /// <summary>
    /// 游戏数据库运行时加载器。
    /// 从 Resources/Content/ 加载三个独立的 SO（CharacterDB / FactionDB / LocationDB），
    /// 构建为 Dictionary&lt;string, string&gt;（名称 → 完整内容），
    /// 供 LLM 工具查询。
    ///
    /// 同时支持 LLM 运行中产生的动态实体（NPC/势力/地点），
    /// 以强类型 NpcEntry/FactionEntry/LocationEntry 存储，
    /// 内部序列化为 JSON，供 C# 反序列化使用。
    ///
    /// 用法：
    ///   GameDataLoader.Init();
    ///   string content = GameDataLoader.FindCharacter("阿虎头");
    ///   GameDataLoader.AddNpc(new NpcEntry { Name = "张铁柱", Role = "随从", ... });
    /// </summary>
    public static class GameDataLoader
    {
        private const string BasePath = "Content/";

        private static bool _loaded;

        // ---- 静态 SO 数据（markdown 文本） ----
        private static Dictionary<string, string> _characters;
        private static Dictionary<string, string> _factions;
        private static Dictionary<string, string> _locations;

        // ---- 动态实体数据（LLM 生成，JSON 序列化） ----
        private static Dictionary<string, NpcEntry> _npcEntries = new Dictionary<string, NpcEntry>();
        private static Dictionary<string, FactionEntry> _factionEntries = new Dictionary<string, FactionEntry>();
        private static Dictionary<string, LocationEntry> _locationEntries = new Dictionary<string, LocationEntry>();

        // ---- ID 计数器（AIGame 风格：N0001 / F0001 / L0001） ----
        private static int _nextNpcId = 1;
        private static int _nextFactionId = 1;
        private static int _nextLocationId = 1;

        /// <summary>初始化/重新加载。游戏启动时调用一次。</summary>
        public static void Init()
        {
            _characters = LoadDict<CharacterDB>("CharacterDB", "人物");
            _factions   = LoadDict<FactionDB>("FactionDB", "势力");
            _locations  = LoadDict<LocationDB>("LocationDB", "地点");
            _loaded = true;

            Debug.Log($"[GameDataLoader] ✅ 加载完成: 人物={_characters.Count} 势力={_factions.Count} 地点={_locations.Count}");
        }

        // ================================================================
        //  实体注册（由 WorldDataRegistrar 调用）
        // ================================================================

        /// <summary>注册一个 NPC 实体。同名已存在则跳过。</summary>
        public static void AddNpc(NpcEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) return;
            string key = entry.Name.Trim();
            if (_npcEntries.ContainsKey(key) || _characters.ContainsKey(key)) return; // 不覆盖
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = $"N{_nextNpcId++:D4}";
            entry.Name = key;
            _npcEntries[key] = entry;
        }

        /// <summary>注册一个势力实体。同名已存在则跳过。</summary>
        public static void AddFaction(FactionEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) return;
            string key = entry.Name.Trim();
            if (_factionEntries.ContainsKey(key) || _factions.ContainsKey(key)) return;
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = $"F{_nextFactionId++:D4}";
            entry.Name = key;
            _factionEntries[key] = entry;
        }

        /// <summary>注册一个地点实体。同名已存在则跳过。</summary>
        public static void AddLocation(LocationEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) return;
            string key = entry.Name.Trim();
            if (_locationEntries.ContainsKey(key) || _locations.ContainsKey(key)) return;
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = $"L{_nextLocationId++:D4}";
            entry.Name = key;
            _locationEntries[key] = entry;
        }

        // ================================================================
        //  查询（LLM 工具 / C# 代码使用）
        // ================================================================

        /// <summary>按名称查询人物文本。先查动态实体，再查静态 SO。</summary>
        public static string FindCharacter(string name)
        {
            EnsureLoaded();
            string key = name?.Trim() ?? "";
            // 动态实体：格式化为可读文本
            if (_npcEntries.TryGetValue(key, out var npc))
                return npc.ToLlmText();
            // 静态 SO
            _characters.TryGetValue(key, out var content);
            return content;
        }

        /// <summary>按名称查询势力文本。先查动态实体，再查静态 SO。</summary>
        public static string FindFaction(string name)
        {
            EnsureLoaded();
            string key = name?.Trim() ?? "";
            if (_factionEntries.TryGetValue(key, out var fac))
                return fac.ToLlmText();
            _factions.TryGetValue(key, out var content);
            return content;
        }

        /// <summary>按名称查询地点文本。先查动态实体，再查静态 SO。</summary>
        public static string FindLocation(string name)
        {
            EnsureLoaded();
            string key = name?.Trim() ?? "";
            if (_locationEntries.TryGetValue(key, out var loc))
                return loc.ToLlmText();
            _locations.TryGetValue(key, out var content);
            return content;
        }

        // ---- 强类型查询（供 C# 代码使用） ----

        /// <summary>按名称查询 NPC 实体。未找到返回 null。</summary>
        public static NpcEntry FindNpcRaw(string name)
        {
            EnsureLoaded();
            _npcEntries.TryGetValue(name?.Trim() ?? "", out var entry);
            return entry;
        }

        /// <summary>按名称查询势力实体。未找到返回 null。</summary>
        public static FactionEntry FindFactionRaw(string name)
        {
            EnsureLoaded();
            _factionEntries.TryGetValue(name?.Trim() ?? "", out var entry);
            return entry;
        }

        /// <summary>按名称查询地点实体。未找到返回 null。</summary>
        public static LocationEntry FindLocationRaw(string name)
        {
            EnsureLoaded();
            _locationEntries.TryGetValue(name?.Trim() ?? "", out var entry);
            return entry;
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
                Debug.LogWarning($"[GameDataLoader] ❌ 未找到 {path}.asset，请用菜单 ACLS > Import Game Data 导入");
                return new Dictionary<string, string>();
            }

            var field = typeof(T).GetField("Entries");
            var entries = field?.GetValue(db) as List<GameDataEntry>;
            if (entries == null)
            {
                Debug.LogWarning($"[GameDataLoader] ⚠ {assetName}.asset 的 Entries 字段为空");
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
