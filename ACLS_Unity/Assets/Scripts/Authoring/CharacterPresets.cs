using System.Collections.Generic;

namespace ACLS.Authoring
{
    // Four canned character backgrounds shown on the new-game creation modal.
    // Each preset bundles a starting location, a starting trait, and a short
    // blurb that the LLM consumes verbatim as flavor for the opening scene.
    //
    // Sex / name / courtesy are NOT in the preset — those are filled in by
    // the player on the modal. The blurb deliberately stays sex-neutral.
    public static class CharacterPresets
    {
        public sealed class Preset
        {
            public string Id;
            public string Title;
            public string LocationName;     // must match a Location.Name in WorldFactory
            public int TraitId;             // WorldFactory.TRAIT_*
            public string TraitLabel;       // for UI badge ("好学" / "谨慎" / "果决")
            public string Blurb;
        }

        public static readonly IReadOnlyList<Preset> All = new[]
        {
            new Preset
            {
                Id = "bg_yingchuan_scholar",
                Title = "颍川寒门子弟",
                LocationName = "颍川",
                TraitId = WorldFactory.TRAIT_STUDIOUS,
                TraitLabel = "好学",
                Blurb = "出身寒微书香家，家中藏书数卷，父早逝赖母劳作度日。志在求学于陈氏门下。",
            },
            new Preset
            {
                Id = "bg_siili_orphan",
                Title = "司隶仕宦遗孤",
                LocationName = "洛阳",
                TraitId = WorldFactory.TRAIT_CAUTIOUS,
                TraitLabel = "谨慎",
                Blurb = "父辈曾任卫尉，党锢之祸后赋闲家中，家道中落。自幼随母居洛阳南郊，识大族忌讳。",
            },
            new Preset
            {
                Id = "bg_guanzhong_frontier",
                Title = "关中边地后辈",
                LocationName = "长安",
                TraitId = WorldFactory.TRAIT_DECISIVE,
                TraitLabel = "果决",
                Blurb = "祖上戍边有功，落籍关中。凉州羌乱年年，自幼见惯烽火，骑射胜于笔墨，胸中自有一股悍气。",
            },
            new Preset
            {
                Id = "bg_jingxiang_youth",
                Title = "荆襄江畔少年",
                LocationName = "襄阳",
                TraitId = WorldFactory.TRAIT_STUDIOUS,
                TraitLabel = "好学",
                Blurb = "襄阳富庶之地的中等士族子弟，家有田宅，偶游江夏交结同辈，虽未涉险但已有抱负。",
            },
            new Preset
            {
                Id = "bg_yizhou_mountain",
                Title = "益州山野子弟",
                LocationName = "成都",
                TraitId = WorldFactory.TRAIT_CAUTIOUS,
                TraitLabel = "谨慎",
                Blurb = "蜀地中等豪族旁支，家居成都近郊，少时入山习猎。蜀道隔绝，中原消息迟来，此番黄巾乱起方知天下已变。",
            },
        };
    }
}
