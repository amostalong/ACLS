using System;
using UnityEngine;

namespace ACLS.Authoring
{
    /// <summary>
    /// 预设所属列表。Flags 允许一个预设同时出现在多个列表中。
    /// </summary>
    [Flags]
    public enum PresetList : byte
    {
        None       = 0,
        NewGame    = 1 << 0,
        World      = 1 << 1,
        Character  = 1 << 2,
    }

    /// <summary>
    /// 单个预设的 ScriptableObject。
    /// 字段覆盖 NewGamePresets、CharacterPresets、WorldPresets 三类预设的全部属性。
    /// </summary>
    public sealed class PresetEntrySO : ScriptableObject
    {
        [Header("基础")]
        public string Id;
        public string Title;
        public string Era;
        public string Description;
        public bool IsCustom;

        [Header("世界")]
        public string WorldBlurb;

        [Header("角色")]
        public string CharBlurb;
        public string LocationName;
        public int TraitId;
        public string TraitLabel;

        [Header("预设角色（玩家）")]
        [Tooltip("预设的默认姓名。空 = 不预填，玩家必须自己填。")]
        public string CharName;
        [Tooltip("预设的默认字。空 = 留空。")]
        public string CharCourtesy;
        [Tooltip("玩家角色默认年龄。0 = 不强制，沿用 22。")]
        public int CharAge;
        [Tooltip("预设性别。None = 玩家自选。")]
        public CharSex CharSex = CharSex.None;

        [Header("预设角色 CHAR 数据（注入 CharEntry）")]
        [TextArea(2, 6)]
        public string CharBackgroundStory;
        [TextArea(1, 3)]
        public string CharValues;
        [TextArea(1, 3)]
        public string CharCurrentGoal;
        [TextArea(1, 3)]
        public string CharSecret;

        [Header("列表归属")]
        public PresetList Lists = PresetList.NewGame;
    }

    public enum CharSex : byte
    {
        None   = 0,
        Male   = 1,
        Female = 2,
    }
}
