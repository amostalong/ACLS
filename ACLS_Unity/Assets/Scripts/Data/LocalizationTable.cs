using System;
using UnityEngine;

namespace ACLS.Data
{
    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "ACLS/Localization Table")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Key;
            [TextArea(1, 4)] public string Text;
        }

        public Entry[] Entries;
    }
}
