using System.Collections.Generic;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // 新游戏角色预设——每个预设同时包含角色背景和世界背景。
    // 玩家在 NewGameView 中直接选择一个预设（含全部信息），
    // 或选「自定义」填入自己的角色描述和世界描述。
    public static class NewGamePresets
    {
        public sealed class Preset
        {
            public string Id;
            public string Title;            // 显示名称，如"颍川寒门子弟"
            public string Era;              // 时代副标题
            public string Description;      // 卡片上的简短描述
            public string WorldBlurb;       // 喂给 WorldBuild LLM 调用的世界描述
            public string CharBlurb;        // 喂给 ExpandCharacter 的角色描述 blurb
            public string LocationName;     // 起始地点，传给 WorldFactory.ConfigurePlayer
            public int TraitId;             // 起始特质 ID
            public string TraitLabel;      // 特质显示名
            public bool IsCustom;           // 是否为自定义选项
        }

        public static readonly IReadOnlyList<Preset> All = new[]
        {
            new Preset
            {
                Id = "ng_yingchuan_scholar",
                Title = "颍川寒门子弟",
                Era = "东汉·中平元年（184）",
                Description = "寒门书香 · 志求学于陈氏门下",
                WorldBlurb = "东汉末年，中平元年，黄巾之乱方兴未艾。朝政日衰，宦官擅权，天下英雄蠢蠢欲动。烽烟从冀州蔓延至四方，乱世的序幕已然拉开。",
                CharBlurb = "出身寒微书香家，家中藏书数卷，父早逝赖母劳作度日。志在求学于陈氏门下。",
                LocationName = "颍川",
                TraitId = WorldFactory.TRAIT_STUDIOUS,
                TraitLabel = "好学",
            },
            new Preset
            {
                Id = "ng_siili_orphan",
                Title = "司隶仕宦遗孤",
                Era = "东汉·中平元年（184）",
                Description = "党锢遗泽 · 识大族之忌讳",
                WorldBlurb = "东汉末年，中平元年，黄巾之乱方兴未艾。朝政日衰，宦官擅权，天下英雄蠢蠢欲动。烽烟从冀州蔓延至四方，乱世的序幕已然拉开。",
                CharBlurb = "父辈曾任卫尉，党锢之祸后赋闲家中，家道中落。自幼随母居洛阳南郊，识大族忌讳。",
                LocationName = "洛阳",
                TraitId = WorldFactory.TRAIT_CAUTIOUS,
                TraitLabel = "谨慎",
            },
            new Preset
            {
                Id = "ng_guanzhong_frontier",
                Title = "关中边地后辈",
                Era = "东汉·中平元年（184）",
                Description = "凉州烽火 · 骑射胜于笔墨",
                WorldBlurb = "东汉末年，中平元年，黄巾之乱方兴未艾。朝政日衰，宦官擅权，天下英雄蠢蠢欲动。烽烟从冀州蔓延至四方，乱世的序幕已然拉开。",
                CharBlurb = "祖上戍边有功，落籍关中。凉州羌乱年年，自幼见惯烽火，骑射胜于笔墨，胸中自有一股悍气。",
                LocationName = "长安",
                TraitId = WorldFactory.TRAIT_DECISIVE,
                TraitLabel = "果决",
            },
            new Preset
            {
                Id = "ng_jingxiang_youth",
                Title = "荆襄江畔少年",
                Era = "东汉·中平元年（184）",
                Description = "富庶之地 · 中等士族子弟",
                WorldBlurb = "东汉末年，中平元年，黄巾之乱方兴未艾。朝政日衰，宦官擅权，天下英雄蠢蠢欲动。烽烟从冀州蔓延至四方，乱世的序幕已然拉开。",
                CharBlurb = "襄阳富庶之地的中等士族子弟，家有田宅，偶游江夏交结同辈，虽未涉险但已有抱负。",
                LocationName = "襄阳",
                TraitId = WorldFactory.TRAIT_STUDIOUS,
                TraitLabel = "好学",
            },
            new Preset
            {
                Id = "ng_yizhou_mountain",
                Title = "益州山野子弟",
                Era = "东汉·中平元年（184）",
                Description = "蜀道隔绝 · 山野豪族之后",
                WorldBlurb = "东汉末年，中平元年，黄巾之乱方兴未艾。朝政日衰，宦官擅权，天下英雄蠢蠢欲动。烽烟从冀州蔓延至四方，乱世的序幕已然拉开。",
                CharBlurb = "蜀地中等豪族旁支，家居成都近郊，少时入山习猎。蜀道隔绝，中原消息迟来，此番黄巾乱起方知天下已变。",
                LocationName = "成都",
                TraitId = WorldFactory.TRAIT_CAUTIOUS,
                TraitLabel = "谨慎",
            },
            new Preset
            {
                Id = "ng_custom",
                Title = "自定义角色",
                Era = "自定义",
                Description = "写出你自己的角色和世界故事",
                IsCustom = true,
            },
        };

        /// <summary>
        /// 根据 NewGamePreset 构建 CharacterPresets.Preset，
        /// 用于传给现有的 ExpandCharacter/StartStageCreate 链。
        /// </summary>
        public static CharacterPresets.Preset ToCharacterPreset(Preset ng)
        {
            return new CharacterPresets.Preset
            {
                Id = ng.Id,
                Title = ng.Title,
                LocationName = ng.LocationName,
                TraitId = ng.TraitId,
                TraitLabel = ng.TraitLabel,
                Blurb = ng.CharBlurb,
            };
        }
    }
}
