using System;

namespace ACLS.Sim
{
    // 5-axis ROTK-style stats: 武 / 统 / 智 / 政 / 魅
    [Serializable]
    public struct Stats
    {
        public int Wu;     // 武 — combat
        public int Tong;   // 统 — strategy / command
        public int Zhi;    // 智 — intellect
        public int Zheng;  // 政 — politics / governance
        public int Mei;    // 魅 — charisma

        public Stats(int wu, int tong, int zhi, int zheng, int mei)
        {
            Wu = wu; Tong = tong; Zhi = zhi; Zheng = zheng; Mei = mei;
        }

        public int Get(StatAxis axis) => axis switch
        {
            StatAxis.Wu => Wu,
            StatAxis.Tong => Tong,
            StatAxis.Zhi => Zhi,
            StatAxis.Zheng => Zheng,
            StatAxis.Mei => Mei,
            _ => 0,
        };

        public Stats With(StatAxis axis, int value) => axis switch
        {
            StatAxis.Wu => new Stats(value, Tong, Zhi, Zheng, Mei),
            StatAxis.Tong => new Stats(Wu, value, Zhi, Zheng, Mei),
            StatAxis.Zhi => new Stats(Wu, Tong, value, Zheng, Mei),
            StatAxis.Zheng => new Stats(Wu, Tong, Zhi, value, Mei),
            StatAxis.Mei => new Stats(Wu, Tong, Zhi, Zheng, value),
            _ => this,
        };

        public Stats Adjust(StatAxis axis, int delta) => With(axis, Get(axis) + delta);

        public static Stats operator +(Stats a, Stats b) =>
            new Stats(a.Wu + b.Wu, a.Tong + b.Tong, a.Zhi + b.Zhi, a.Zheng + b.Zheng, a.Mei + b.Mei);
    }

    public enum StatAxis : byte { Wu = 0, Tong = 1, Zhi = 2, Zheng = 3, Mei = 4 }
}
