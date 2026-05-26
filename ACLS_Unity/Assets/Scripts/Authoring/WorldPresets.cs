using System.Collections.Generic;

namespace ACLS.Authoring
{
    // World setting presets shown on the world-selection modal.
    // Parallel to CharacterPresets: the player picks the world first,
    // then creates their actor within it.
    public static class WorldPresets
    {
        public sealed class Preset
        {
            public string Id;
            public string Title;
            public string Era;
            public string Description;  // short subtitle shown on the card
            public string Blurb;        // fed verbatim to LLM for world build
            public bool IsCustom;       // if true, player types their own blurb
        }

        public static readonly IReadOnlyList<Preset> All = new[]
        {
            new Preset
            {
                Id          = "wld_sanguo_184",
                Title       = "三国乱世",
                Era         = "东汉·中平元年（184）",
                Description = "黄巾初起，英雄辈出",
                Blurb       = "东汉末年，中平元年，黄巾之乱方兴未艾。朝政日衰，宦官擅权，天下英雄蠢蠢欲动。" +
                              "烽烟从冀州蔓延至四方，乱世的序幕已然拉开。",
            },
            new Preset
            {
                Id          = "wld_tang_kaiyuan",
                Title       = "大唐开元",
                Era         = "唐·开元年间（713-741）",
                Description = "盛唐极盛，长安繁华",
                Blurb       = "盛唐极盛，开元年间长安繁华如梦。诗酒流行，商旅往来，四夷宾服。" +
                              "然安史之乱的阴云，尚在遥远的地平线之外。",
            },
            new Preset
            {
                Id          = "wld_song_bianjing",
                Title       = "北宋汴京",
                Era         = "宋·北宋中期",
                Description = "市井繁华，文人风流",
                Blurb       = "北宋汴京车水马龙，市井繁华，科举盛行，士大夫风流一时。" +
                              "然西夏与辽在北方虎视眈眈，边患未曾平息。",
            },
            new Preset
            {
                Id          = "wld_custom",
                Title       = "自定义世界",
                Era         = "自定义",
                Description = "用文字描述你想生活的世界",
                IsCustom    = true,
            },
        };
    }
}
