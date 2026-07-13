using System.Collections.Generic;

namespace ACLS.Sim
{
    // 硬锚点(史实)的纯数据定义。
    // 与 EraTrendAnchorSO 解耦:此处先用代码内表打底,未来可迁到 SO 资产。
    //
    // 时间锚点(184 年起)按 game-design.md 10.2 列出:
    //   184 春:黄巾起义
    //   184 末:张角病死
    //   188:刘焉改刺史为州牧
    //   189:灵帝崩,何进死,董卓入京
    //   190:关东联军讨董
    //
    // 注:东汉历法 184 实际起于光和七年 / 中平元年正月。年号以中平计。
    public static class EraTrendAnchors
    {
        // 以 Preset.Id 为键的锚点表。剧本未提供表 → EraTrend 系统关闭（读工具输出“（无）”，不报错）。
        // 未列出预设都默认返回空表（例如 wld_custom 自定义剧本）。
        private static readonly Dictionary<string, List<EraAnchorDef>> _byPreset = new Dictionary<string, List<EraAnchorDef>>
        {
            // 三国剧本：中平元年（184）以后的史实主线
            ["wld_sanguo_184"] = Sanguo184,
            ["ng_yingchuan_scholar"] = Sanguo184,
        };

        // 读取指定剧本的硬锚点表。未命中返回空列表（EraTrendService 会跳过所有锚点）。
        public static List<EraAnchorDef> Get(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return Empty;
            return _byPreset.TryGetValue(presetId, out var list) ? list : Empty;
        }

        private static readonly List<EraAnchorDef> Empty = new List<EraAnchorDef>();
        internal static List<EraAnchorDef> EmptyList => Empty;

        // 向后兼容：三国锚点直接公开访问（旧代码 / 测试可继续用 EraTrendAnchors.Hard）。
        public static List<EraAnchorDef> Hard => Sanguo184;

        // 三国剧本（184–190）主线锚点表
        private static readonly List<EraAnchorDef> Sanguo184 = new List<EraAnchorDef>
        {
            new EraAnchorDef
            {
                Id = "anchor_huangjin_onset",
                StageName = "中平元年·黄巾乱起",
                TriggerYear = 184, TriggerMonth = 2, TriggerDay = 1,
                Title = "黄巾起义",
                Summary = "巨鹿张角率太平道众起事,八州并发,天下震动。",
                FactionIds = { "faction_zhangjiao", "faction_huangjin" },
                Foreshadowing = new List<ForeshadowingRule>
                {
                    new ForeshadowingRule { TargetLayer = "L3", DaysBefore = 90, Template = "近日冀州有大贤施符水济民,信众日增,里闾议论纷纷。" },
                    new ForeshadowingRule { TargetLayer = "L2", DaysBefore = 45, Template = "邻县出现太平道传教人,口称'苍天已死,黄天当立'。" },
                    new ForeshadowingRule { TargetLayer = "L1", DaysBefore = 14, Template = "市集上有人窃议'岁在甲子,天下大吉',神色紧张。" },
                },
            },
            new EraAnchorDef
            {
                Id = "anchor_zhangjiao_dead",
                StageName = "中平元年·黄巾溃灭",
                TriggerYear = 184, TriggerMonth = 11, TriggerDay = 1,
                Title = "张角病逝",
                Summary = "张角于秋冬间病亡,黄巾军旋即败于皇甫嵩、朱儁。",
                FactionIds = { "faction_zhangjiao" },
                Foreshadowing = new List<ForeshadowingRule>
                {
                    new ForeshadowingRule { TargetLayer = "L3", DaysBefore = 60, Template = "传闻张角病势沉重,太平道内部争位已起。" },
                    new ForeshadowingRule { TargetLayer = "L2", DaysBefore = 20, Template = "前方军报频传:皇甫嵩连破波才、彭脱,黄巾势蹙。" },
                },
            },
            new EraAnchorDef
            {
                Id = "anchor_liuyan_zhizhou",
                StageName = "中平五年·刘焉牧蜀",
                TriggerYear = 188, TriggerMonth = 6, TriggerDay = 1,
                Title = "刘焉改刺史为州牧",
                Summary = "太常刘焉上言改州牧,自领益州牧,州郡权重之始。",
                FactionIds = { "faction_liuyan" },
                Foreshadowing = new List<ForeshadowingRule>
                {
                    new ForeshadowingRule { TargetLayer = "L3", DaysBefore = 90, Template = "朝议州牧之制,益州士民翘首以盼明府。" },
                    new ForeshadowingRule { TargetLayer = "L2", DaysBefore = 30, Template = "刘焉将入蜀的消息已传至州内,各地豪强暗中备礼。" },
                },
            },
            new EraAnchorDef
            {
                Id = "anchor_dongzhuo_rujing",
                StageName = "中平六年·董卓入京",
                TriggerYear = 189, TriggerMonth = 9, TriggerDay = 1,
                Title = "灵帝驾崩、董卓入京",
                Summary = "灵帝崩,少帝立。何进与宦官同归于尽,董卓率西凉兵入洛阳,废少帝,立献帝。",
                FactionIds = { "faction_dongzhuo", "faction_han_court" },
                Foreshadowing = new List<ForeshadowingRule>
                {
                    new ForeshadowingRule { TargetLayer = "L3", DaysBefore = 120, Template = "灵帝沉疴未起,朝中党人、宦官暗流涌动。" },
                    new ForeshadowingRule { TargetLayer = "L2", DaysBefore = 60, Template = "何进召董卓入京勤王的消息已外泄,洛阳士族惶然。" },
                    new ForeshadowingRule { TargetLayer = "L1", DaysBefore = 14, Template = "市井传言'西凉兵将至',百姓争相储粮。" },
                },
            },
            new EraAnchorDef
            {
                Id = "anchor_guandong_lianjun",
                StageName = "初平元年·关东联军",
                TriggerYear = 190, TriggerMonth = 2, TriggerDay = 1,
                Title = "关东联军讨董",
                Summary = "袁绍为盟主,关东诸郡起兵讨董。",
                FactionIds = { "faction_yuanshao", "faction_dongzhuo" },
                Foreshadowing = new List<ForeshadowingRule>
                {
                    new ForeshadowingRule { TargetLayer = "L3", DaysBefore = 45, Template = "渤海太守袁绍发檄传告州郡,声讨董卓。" },
                    new ForeshadowingRule { TargetLayer = "L2", DaysBefore = 15, Template = "本地豪族已聚议响应,劝募粮草。" },
                },
            },
        };
    }

    // 单个硬锚点的数据定义。
    [System.Serializable]
    public sealed class EraAnchorDef
    {
        public string Id;
        public string StageName;
        public int TriggerYear;
        public int TriggerMonth;
        public int TriggerDay;
        public string Title;
        public string Summary;
        public List<string> FactionIds = new List<string>();
        public List<ForeshadowingRule> Foreshadowing = new List<ForeshadowingRule>();

        public GameDate TriggerDate => new GameDate(TriggerYear, TriggerMonth, TriggerDay);
    }

    // 前兆注入规则。
    [System.Serializable]
    public struct ForeshadowingRule
    {
        public string TargetLayer;     // "L1" / "L2" / "L3"
        public int DaysBefore;         // 距锚点触发日还有多少天注入
        public string Template;        // 注入文本(规则表里写死)
    }
}
