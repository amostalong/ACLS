using System.Collections.Generic;
using UnityEngine;

namespace ACLS.Authoring
{
    /// <summary>
    /// 预设数据库 ScriptableObject。持有所有 PresetEntrySO 的引用。
    /// 静态类 NewGamePresets / CharacterPresets / WorldPresets 从此加载数据。
    /// </summary>
    public sealed class PresetDatabaseSO : ScriptableObject
    {
        public List<PresetEntrySO> Presets = new();
    }
}
