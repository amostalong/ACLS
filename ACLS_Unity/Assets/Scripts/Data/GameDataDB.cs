using System;
using System.Collections.Generic;
using UnityEngine;

namespace ACLS.Data
{
    /// <summary>
    /// 三国游戏数据条目（人物/势力/地点共用）。
    /// 每个条目对应 AIGame memory/CHARS/ FACTIONS/ PLACES/ 的一个 .md 文件。
    /// </summary>
    [Serializable]
    public sealed class GameDataEntry
    {
        /// <summary>编号，如 "C0001" "F0001" "P0001"</summary>
        public string Id = "";

        /// <summary>名称，如 "阿虎头" "安汉·李家" "武阳"</summary>
        public string Name = "";

        /// <summary>完整 markdown 内容，直接来自 .md 文件</summary>
        [TextArea(5, 100)]
        public string Content = "";
    }

    // ──── 三个独立的数据库 SO ────

    /// <summary>人物数据库。</summary>
    [CreateAssetMenu(fileName = "CharacterDB", menuName = "ACLS/Data/Character DB", order = 1)]
    public sealed class CharacterDB : ScriptableObject
    {
        public List<GameDataEntry> Entries = new List<GameDataEntry>();
    }

    /// <summary>势力数据库。</summary>
    [CreateAssetMenu(fileName = "FactionDB", menuName = "ACLS/Data/Faction DB", order = 2)]
    public sealed class FactionDB : ScriptableObject
    {
        public List<GameDataEntry> Entries = new List<GameDataEntry>();
    }

    /// <summary>地点数据库。</summary>
    [CreateAssetMenu(fileName = "LocationDB", menuName = "ACLS/Data/Location DB", order = 3)]
    public sealed class LocationDB : ScriptableObject
    {
        public List<GameDataEntry> Entries = new List<GameDataEntry>();
    }
}
