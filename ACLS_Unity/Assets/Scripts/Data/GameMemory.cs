using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ACLS.Data
{
    /// <summary>
    /// 运行时游戏记忆容器。
    /// 持有 LLM 构建的全部动态实体数据（L2 chars/factions/places/events/opportunities），
    /// 同时也是存档的序列化单元。
    ///
    /// 查询时优先查动态数据，找不到再 fallback 到 GameDataLoader 的静态 SO 数据。
    /// </summary>
    [Serializable]
    public sealed class GameMemory
    {
        // ──── L2 实体列表（与 WorldBuildReply 的 L2 结构对齐） ────
        public List<CharEntry> Chars = new List<CharEntry>();
        public List<FactionEntry> Factions = new List<FactionEntry>();
        public List<PlaceEntry> Places = new List<PlaceEntry>();
        public List<EventEntry> ActiveEvents = new List<EventEntry>();
        public List<string> Opportunities = new List<string>();

        // ──── 注册（去重：同名忽略） ────

        public void AddChar(CharEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.name)) return;
            string key = entry.name.Trim();
            if (FindChar(key) != null) return;
            entry.name = key;
            Chars.Add(entry);
        }

        public void AddFaction(FactionEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.name)) return;
            string key = entry.name.Trim();
            if (FindFactionEntry(key) != null) return;
            entry.name = key;
            Factions.Add(entry);
        }

        public void AddPlace(PlaceEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.name)) return;
            string key = entry.name.Trim();
            if (FindPlaceEntry(key) != null) return;
            entry.name = key;
            Places.Add(entry);
        }

        // ──── 查询（返回格式化文本，供 LLM 工具使用） ────

        public string FindCharacter(string name)
        {
            var entry = FindChar(name);
            if (entry != null) return entry.ToLlmText();
            // fallback 到静态 SO
            return GameDataLoader.FindCharacterStatic(name);
        }

        public string FindFaction(string name)
        {
            var entry = FindFactionEntry(name);
            if (entry != null) return entry.ToLlmText();
            return GameDataLoader.FindFactionStatic(name);
        }

        public string FindPlace(string name)
        {
            var entry = FindPlaceEntry(name);
            if (entry != null) return entry.ToLlmText();
            return GameDataLoader.FindLocationStatic(name);
        }

        // ──── NPC 丰富化更新（Step 5 后调用） ────
        public void ApplyNpcExpansion(string name, string backgroundStory, string values, string currentGoal, string secret)
        {
            var entry = FindChar(name);
            if (entry == null) return;
            if (!string.IsNullOrWhiteSpace(backgroundStory)) entry.background_story = backgroundStory;
            if (!string.IsNullOrWhiteSpace(values)) entry.values = values;
            if (!string.IsNullOrWhiteSpace(currentGoal)) entry.current_goal = currentGoal;
            if (!string.IsNullOrWhiteSpace(secret)) entry.secret = secret;
        }

        // ──── 内部查找（返回原始对象） ────

        private CharEntry FindChar(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            for (int i = 0; i < Chars.Count; i++)
            {
                if (string.Equals(Chars[i].name, key, StringComparison.OrdinalIgnoreCase))
                    return Chars[i];
            }
            return null;
        }

        private FactionEntry FindFactionEntry(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            for (int i = 0; i < Factions.Count; i++)
            {
                if (string.Equals(Factions[i].name, key, StringComparison.OrdinalIgnoreCase))
                    return Factions[i];
            }
            return null;
        }

        private PlaceEntry FindPlaceEntry(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            for (int i = 0; i < Places.Count; i++)
            {
                if (string.Equals(Places[i].name, key, StringComparison.OrdinalIgnoreCase))
                    return Places[i];
            }
            return null;
        }

        // ──── 单例（运行时可访问，存档时替换） ────

        [JsonIgnore]
        public static GameMemory Instance { get; internal set; } = new GameMemory();
    }
}
