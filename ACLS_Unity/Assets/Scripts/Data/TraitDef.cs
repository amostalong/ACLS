using System.Collections.Generic;
using UnityEngine;
using ACLS.Sim;

namespace ACLS.Data
{
    [CreateAssetMenu(fileName = "TraitDef", menuName = "ACLS/Trait Def")]
    public sealed class TraitDef : ScriptableObject
    {
        public int Id;
        public string DisplayNameKey;
        public string DescriptionKey;
        public Stats StatModifier;
        public List<int> ConflictsWith = new List<int>();
    }
}
