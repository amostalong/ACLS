using System;
using System.Collections.Generic;

namespace ACLS.Llm.Tools
{
    /// <summary>
    /// 东汉地名距离计算器。
    /// 纯数据驱动，包含地名坐标表和行军速度表。
    /// </summary>
    public static class TravelCalculator
    {
        // ──── 地名坐标（x, y 均为汉里，以洛阳为原点 (0,0)） ────
        // 数据来源：《中国历史地图集》《后汉书·郡国志》
        private static readonly Dictionary<string, (int X, int Y)> Locations =
            new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase)
        {
            // === 司隶 ===
            { "洛阳",     (  0,   0) },
            { "长安",     (-500,  0) },
            { "弘农",     (-120,  0) },

            // === 豫州 ===
            { "颍川",     ( 130, -80) },
            { "阳翟",     ( 130, -80) },
            { "许县",     ( 150, -90) },
            { "汝南",     ( 200, -120) },

            // === 兖州 ===
            { "陈留",     ( 100, -30) },

            // === 冀州 ===
            { "邺城",     ( 60, -160) },
            { "信都",     ( 80, -220) },
            { "巨鹿",     ( 130, -230) },

            // === 荆州 ===
            { "襄阳",     ( 250, -280) },
            { "江陵",     ( 280, -360) },
            { "南阳",     ( 190, -200) },
            { "宛城",     ( 190, -200) },

            // === 益州（今四川） ===
            { "成都",     (-280, -250) },
            { "武阳",     (-300, -270) },
            { "南安",     (-340, -300) },
            { "犍为",     (-320, -280) },
            { "僰道",     (-380, -330) },
            { "江州",     (-200, -340) },
            { "阆中",     (-220, -230) },
            { "巴郡",     (-200, -340) },
            { "安汉",     (-210, -300) },
            { "广汉",     (-250, -230) },
            { "梓潼",     (-220, -190) },
            { "剑阁",     (-190, -170) },
            { "葭萌",     (-200, -180) },
            { "绵竹",     (-260, -230) },
            { "雒县",     (-270, -240) },
            { "郫县",     (-290, -250) },
            { "临邛",     (-310, -260) },
            { "汶山",     (-280, -200) },
            { "汉嘉",     (-320, -240) },

            // === 汉中 ===
            { "汉中",     (-150, -120) },
            { "南郑",     (-150, -120) },

            // === 凉州 ===
            { "陇西",     (-400,  -80) },
            { "武威",     (-600, -120) },
            { "张掖",     (-700, -130) },
            { "酒泉",     (-800, -150) },
            { "敦煌",     (-900, -170) },

            // === 扬州 ===
            { "建业",     ( 500, -320) },
            { "吴郡",     ( 560, -340) },
            { "会稽",     ( 600, -400) },
            { "柴桑",     ( 380, -340) },
            { "寿春",     ( 300, -130) },
            { "合肥",     ( 330, -200) },

            // === 青徐 ===
            { "临淄",     ( 280, -280) },
            { "琅琊",     ( 300, -220) },
            { "彭城",     ( 270, -170) },
            { "下邳",     ( 310, -180) },
            { "郯城",     ( 320, -210) },

            // === 并州 ===
            { "晋阳",     ( 40, -380) },
            { "上党",     ( 60, -300) },

            // === 幽州 ===
            { "蓟城",     ( 100, -500) },
            { "涿郡",     ( 90, -470) },
            { "辽东",     ( 500, -500) },
        };

        // ──── 行军速度（汉里/日） ────
        private static readonly Dictionary<string, double> Speeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "步行",     40.0 },
            { "急行",     60.0 },
            { "骑马",     130.0 },
            { "快马",     200.0 },
            { "驿马",     300.0 },
            { "乘船",     80.0 },
            { "顺水",     120.0 },
            { "逆水",     40.0 },
            { "车队",     25.0 },
        };

        // 默认速度
        private const double DefaultSpeed = 130.0; // 骑马

        /// <summary>计算两地距离（汉里）。一方未知时返回 -1。</summary>
        public static int GetDistance(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return -1;

            if (!Locations.TryGetValue(from.Trim(), out var a)) return -1;
            if (!Locations.TryGetValue(to.Trim(), out var b)) return -1;

            // 曼哈顿距离调整——古代道路近似直角路径
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            double direct = Math.Sqrt(dx * dx + dy * dy);

            // 实际道路约是直线距离的 1.2-1.4 倍
            return (int)Math.Round(direct * 1.3 / 10.0) * 10;
        }

        /// <summary>计算行程天数。返回格式如 "约3-4日"。</summary>
        public static string GetTravelTime(string from, string to, string mode = "骑马")
        {
            int dist = GetDistance(from, to);
            if (dist < 0)
                return $"无法计算：未知地名（{from} → {to}）";

            if (!Speeds.TryGetValue(mode?.Trim() ?? "", out var speed))
                speed = DefaultSpeed;

            double daysExact = dist / speed;
            int daysMin = Math.Max(1, (int)Math.Floor(daysExact));
            int daysMax = Math.Max(1, (int)Math.Ceiling(daysExact));

            if (daysMin == daysMax)
                return $"约{daysMin}日";
            return $"约{daysMin}-{daysMax}日";
        }

        /// <summary>
        /// 获取行程详情文本，包含距离和时间。
        /// 用于 LLM tool result 返回。
        /// </summary>
        public static string GetTravelDetail(string from, string to, string mode = "骑马")
        {
            int dist = GetDistance(from, to);
            if (dist < 0)
                return $"无法计算「{from}」到「{to}」的距离：地名不在已知表中。";

            if (!Speeds.TryGetValue(mode?.Trim() ?? "", out var speed))
            {
                mode = "骑马";
                speed = DefaultSpeed;
            }

            double daysExact = dist / speed;
            int daysMin = Math.Max(1, (int)Math.Floor(daysExact));
            int daysMax = Math.Max(1, (int)Math.Ceiling(daysExact));

            string timeStr = daysMin == daysMax ? $"约{daysMin}日" : $"约{daysMin}-{daysMax}日";

            return $"从「{from}」到「{to}」，{mode}：\n"
                 + $"· 距离：约{dist}汉里（1汉里≈415m，约{(int)(dist * 0.415 / 10) * 10}公里）\n"
                 + $"· 耗时：{timeStr}\n"
                 + $"· 速度：约{speed}汉里/日";
        }

        /// <summary>检查地名是否在表中。</summary>
        public static bool IsKnown(string location) =>
            !string.IsNullOrWhiteSpace(location) &&
            Locations.ContainsKey(location.Trim());

        /// <summary>获取所有已知地名列表。</summary>
        public static IEnumerable<string> KnownLocations => Locations.Keys;
    }
}
