using System;

namespace ACLS.Sim
{
    public enum EventTriggerKind { Periodic, Dated, Conditional }

    public enum EventScope { PlayerOnly, AnyHistoricalChar, Global }

    [Serializable]
    public struct EventTrigger
    {
        public EventTriggerKind Kind;

        // Periodic: probability roll every PeriodMonths months on each scoped actor.
        public int PeriodMonths;
        public int ChancePercent;       // 0..100

        // Dated: fires the day Year/Month/Day matches.
        public int DatedYear;
        public int DatedMonth;
        public int DatedDay;
    }
}
