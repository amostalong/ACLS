namespace ACLS.Sim
{
    // Tiny placeholder name generator. Replace with culture-aware version later.
    public static class Names
    {
        public struct NamePair
        {
            public string Given;    // full name incl. surname, e.g. "张潜"
            public string Courtesy; // 字, e.g. "子渊"
        }

        // 暗字: 字 echoes 名's meaning without repeating the character.
        private static readonly NamePair[] PlayerPool =
        {
            // 中原/司隶
            new NamePair { Given = "张潜", Courtesy = "子渊" }, // 潜龙/渊底 — 《易》潜龙勿用
            new NamePair { Given = "张澄", Courtesy = "子清" }, // 澄=水清；清=澄澈互扣
            new NamePair { Given = "张翊", Courtesy = "子辅" }, // 翊=辅翼；辅=佐助义近
            // 荆楚水乡
            new NamePair { Given = "张渚", Courtesy = "子泽" }, // 渚=水中沙洲；泽=泽国湖泊
            new NamePair { Given = "张霁", Courtesy = "子澜" }, // 霁=雨后天晴；澜=江面波浪
            // 蜀地山野
            new NamePair { Given = "张峻", Courtesy = "子岳" }, // 峻=峻岭；岳=高山
            new NamePair { Given = "张嵩", Courtesy = "子巍" }, // 嵩=高山；巍=巍峨
            // 关中/西凉边地
            new NamePair { Given = "张烈", Courtesy = "子戎" }, // 烈=刚烈；戎=戎马边地
            new NamePair { Given = "张骏", Courtesy = "伯驥" }, // 骏/驥 — 良马/千里马
            // 通用
            new NamePair { Given = "张韬", Courtesy = "子略" }, // 韬略/谋略互扣
        };

        public static NamePair RandomPlayerName() =>
            PlayerPool[Rng.Range(0, PlayerPool.Length - 1)];

        private static readonly string[] MaleGiven = {
            "明", "诚", "睿", "翊", "昱", "瑾", "嵩", "弈", "桓", "钧",
            "彦", "毅", "韬", "靖", "霖", "煜", "昭", "涣", "翰", "昶",
        };
        private static readonly string[] FemaleGiven = {
            "婉", "瑶", "琬", "婵", "妍", "媛", "蓉", "莹", "璇", "琳",
            "莺", "嫣", "茜", "宛", "倩", "盼", "若", "薇", "芸", "湘",
        };
        private static readonly string[] CourtesyChars = {
            "子", "孟", "仲", "叔", "季", "公", "文", "武", "玄", "元",
        };
        private static readonly string[] CourtesyTails = {
            "昭", "明", "诚", "瑾", "钧", "翊", "弈", "和", "顺", "正",
        };

        public static string RandomGiven(Sex sex) =>
            (sex == Sex.Male ? MaleGiven : FemaleGiven)[Rng.Range(0, (sex == Sex.Male ? MaleGiven : FemaleGiven).Length - 1)];

        public static string RandomCourtesy() =>
            CourtesyChars[Rng.Range(0, CourtesyChars.Length - 1)] +
            CourtesyTails[Rng.Range(0, CourtesyTails.Length - 1)];

        // Best-effort: take the first character of fullName as the surname.
        // Adequate for player's fictional clan; historical characters set Name explicitly.
        public static string ExtractSurname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            return fullName.Substring(0, 1);
        }
    }
}
