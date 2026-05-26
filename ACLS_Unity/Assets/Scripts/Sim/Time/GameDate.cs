using System;

namespace ACLS.Sim
{
    // Solar calendar with fixed 28-day February (no leap years). Acceptable
    // anachronism for a game set in 184 CE — keeps date arithmetic trivial.
    [Serializable]
    public struct GameDate : IComparable<GameDate>, IEquatable<GameDate>
    {
        public int Year;
        public int Month;
        public int Day;

        public GameDate(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public GameDate AddDays(int days)
        {
            int y = Year, m = Month, d = Day + days;
            while (d > DaysInMonth(m))
            {
                d -= DaysInMonth(m);
                m++;
                if (m > 12) { m = 1; y++; }
            }
            while (d < 1)
            {
                m--;
                if (m < 1) { m = 12; y--; }
                d += DaysInMonth(m);
            }
            return new GameDate(y, m, d);
        }

        public int YearsSince(GameDate older)
        {
            int years = Year - older.Year;
            if (Month < older.Month || (Month == older.Month && Day < older.Day)) years--;
            return years;
        }

        public bool IsSameMonthAs(GameDate other) => Year == other.Year && Month == other.Month;
        public bool IsSameYearAs(GameDate other) => Year == other.Year;

        public int CompareTo(GameDate other)
        {
            int c = Year.CompareTo(other.Year);
            if (c != 0) return c;
            c = Month.CompareTo(other.Month);
            if (c != 0) return c;
            return Day.CompareTo(other.Day);
        }

        public bool Equals(GameDate other) => Year == other.Year && Month == other.Month && Day == other.Day;
        public override bool Equals(object obj) => obj is GameDate d && Equals(d);
        public override int GetHashCode() => unchecked(Year * 397 ^ Month * 31 ^ Day);
        public override string ToString() => $"{Year}年{Month}月{Day}日";

        public static bool operator <(GameDate a, GameDate b) => a.CompareTo(b) < 0;
        public static bool operator >(GameDate a, GameDate b) => a.CompareTo(b) > 0;
        public static bool operator <=(GameDate a, GameDate b) => a.CompareTo(b) <= 0;
        public static bool operator >=(GameDate a, GameDate b) => a.CompareTo(b) >= 0;
        public static bool operator ==(GameDate a, GameDate b) => a.Equals(b);
        public static bool operator !=(GameDate a, GameDate b) => !a.Equals(b);

        private static int DaysInMonth(int month)
        {
            switch (month)
            {
                case 1: case 3: case 5: case 7: case 8: case 10: case 12: return 31;
                case 4: case 6: case 9: case 11: return 30;
                case 2: return 28;
                default: throw new ArgumentOutOfRangeException(nameof(month));
            }
        }
    }
}
