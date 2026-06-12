using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ACLS.Llm
{
    public static class NarrationChoicesTextParser
    {
        public enum EffectTagState
        {
            Missing,
            Yes,
            No,
        }

        private static readonly Regex DelimiterRegex = new Regex(@"^\s*---\s*$", RegexOptions.Multiline);
        private static readonly Regex ChoiceLineRegex = new Regex(@"^\s*(\d+)[\.、\)）]\s*(.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex EffectTagRegex = new Regex(@"^\s*@effect\s+(yes|no)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static string ExtractNarrationForStreaming(string raw)
        {
            string text = Normalize(raw);
            if (string.IsNullOrEmpty(text)) return "";

            var m = DelimiterRegex.Match(text);
            if (!m.Success)
                return text.TrimEnd();

            return text.Substring(0, m.Index).TrimEnd();
        }

        public static bool TryParse(string raw, out string narration,
            out List<LlmReply.Choice> choices, out EffectTagState effectTag, out string error)
        {
            narration = "";
            choices = new List<LlmReply.Choice>();
            effectTag = EffectTagState.Missing;
            error = null;

            string text = Normalize(raw);
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "LLM 返回为空";
                return false;
            }

            var effectMatch = EffectTagRegex.Match(text);
            if (effectMatch.Success)
            {
                string flag = effectMatch.Groups[1].Value.Trim().ToLowerInvariant();
                effectTag = flag == "yes" ? EffectTagState.Yes : EffectTagState.No;
                text = text.Remove(effectMatch.Index, effectMatch.Length).TrimEnd();
            }

            var delimiter = DelimiterRegex.Match(text);
            if (!delimiter.Success)
            {
                error = "未找到选项分隔线 ---";
                return false;
            }

            narration = text.Substring(0, delimiter.Index).Trim();
            if (string.IsNullOrWhiteSpace(narration))
            {
                error = "旁白正文为空";
                return false;
            }

            string choiceSection = text.Substring(delimiter.Index + delimiter.Length).Trim();
            if (string.IsNullOrWhiteSpace(choiceSection))
            {
                error = "选项区为空";
                return false;
            }

            string[] lines = choiceSection.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                var m = ChoiceLineRegex.Match(line);
                if (!m.Success) continue;

                string label = m.Groups[2].Value.Trim();
                if (label.Length == 0) continue;

                choices.Add(new LlmReply.Choice
                {
                    Label = label,
                    OutcomeNarration = "",
                    DaysPassed = 0,
                    Effects = new List<LlmReply.EffectSpec>(),
                });
            }

            if (choices.Count == 0)
            {
                error = "未解析到任何选项";
                return false;
            }

            if (choices.Count > 4)
                choices.RemoveRange(4, choices.Count - 4);

            return true;
        }

        public static bool TryParse(string raw, out string narration,
            out List<LlmReply.Choice> choices, out string error)
        {
            return TryParse(raw, out narration, out choices, out _, out error);
        }

        private static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = raw.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0) text = text.Substring(firstNewline + 1);
                int closingFence = text.LastIndexOf("```");
                if (closingFence >= 0) text = text.Substring(0, closingFence);
                text = text.Trim();
            }

            return text;
        }
    }
}
