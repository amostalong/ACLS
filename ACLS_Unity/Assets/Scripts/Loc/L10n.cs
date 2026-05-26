using System.Collections.Generic;
using System.Text;
using ACLS.Data;

namespace ACLS.Loc
{
    // Static front for player-visible strings. Missing keys fall back to the
    // key itself, which surfaces gaps loudly during development.
    public static class L10n
    {
        private static Dictionary<string, string> map = new Dictionary<string, string>();

        public static void Clear() => map.Clear();

        public static void LoadFromTable(LocalizationTable table)
        {
            if (table == null || table.Entries == null) return;
            for (int i = 0; i < table.Entries.Length; i++)
            {
                var e = table.Entries[i];
                if (!string.IsNullOrEmpty(e.Key)) map[e.Key] = e.Text;
            }
        }

        public static void Add(string key, string text)
        {
            if (!string.IsNullOrEmpty(key)) map[key] = text;
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return map.TryGetValue(key, out var v) ? v : key;
        }

        // Substitutes "{name}" style placeholders. Args list: ("name", "曹操"), ("age", "30").
        public static string T(string key, params (string name, string value)[] args)
        {
            string s = T(key);
            if (args == null || args.Length == 0) return s;
            var sb = new StringBuilder(s);
            for (int i = 0; i < args.Length; i++)
            {
                sb.Replace("{" + args[i].name + "}", args[i].value ?? "");
            }
            return sb.ToString();
        }
    }
}
