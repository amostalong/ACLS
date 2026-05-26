using System;
using UnityEngine;
using ACLS.Sim;

namespace ACLS.Data
{
    [CreateAssetMenu(fileName = "GameEvent", menuName = "ACLS/Game Event")]
    public sealed class GameEventDef : ScriptableObject
    {
        [Tooltip("Stable string id (used by save/cooldown bookkeeping). Keep ASCII.")]
        public string Id;

        public string TitleKey;
        [TextArea(2, 6)] public string DescriptionKey;

        public EventScope Scope = EventScope.PlayerOnly;
        public EventTrigger Trigger;
        public ConditionExpr[] Conditions;
        public ChoiceDef[] Choices;

        [Tooltip("Months before the same actor can fire this event again. 0 = no cooldown.")]
        public int CooldownMonths = 0;
    }

    [Serializable]
    public sealed class ChoiceDef
    {
        public string TextKey;
        public ConditionExpr[] VisibleIf;
        public EffectOp[] Effects;
        public string ResultKey;
    }
}
