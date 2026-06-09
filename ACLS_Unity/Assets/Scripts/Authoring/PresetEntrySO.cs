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

        [Header("列表归属")]
        public PresetList Lists = PresetList.NewGame;
    }
}
