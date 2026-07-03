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

        // Matches a standalone delimiter line: "---" / "--" / "——" / "—————" / etc.
        // Allows trailing inline whitespace (spaces/tabs) on the same line, but NOT
        // newlines — those are matched separately to keep the captured span tight.
        // Common LLM deviations: full-width em dash, double hyphen, varying counts.
        private static readonly Regex DelimiterRegex = new Regex(
            @"^[ \t]*(?:-{2,6}|—{1,3})[ \t]*$",
            RegexOptions.Multiline);

        // Matches a choice line with a 1-2 digit number (or circled digit) followed by
        // ".", "、", ")", "）" — supports both ASCII and CJK variants the LLM may emit.
        private static readonly Regex ChoiceLineRegex = new Regex(
            @"^\s*(\d{1,2}|[①-⑩])[\.、\)）]\s*(.+?)\s*$",
            RegexOptions.Compiled);
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

            // Strip the @effect tag if it is present anywhere in the response.
            // (We don't anchor to end; some LLMs drop the effect tag entirely,
            // others place it before the choices — both are accepted.)
            var effectMatch = EffectTagRegex.Match(text);
            if (effectMatch.Success)
            {
                string flag = effectMatch.Groups[1].Value.Trim().ToLowerInvariant();
                effectTag = flag == "yes" ? EffectTagState.Yes : EffectTagState.No;
                text = text.Remove(effectMatch.Index, effectMatch.Length).TrimEnd();
            }

            // Try the canonical order: narration first, then "---", then choices.
            // If we don't find the delimiter, try the reversed order: choices first,
            // then "---", then narration. Some LLMs misorder these blocks.
            var delimiter = DelimiterRegex.Match(text);
            string choiceSection;

            if (delimiter.Success)
            {
                narration = text.Substring(0, delimiter.Index).Trim();
                choiceSection = text.Substring(delimiter.Index + delimiter.Length).Trim();
            }
            else
            {
                // No standalone delimiter line found — try to recover by looking for
                // a "---" / "——" inline (with content on both sides). The substring
                // after the marker is the choice section.
                int inlineDelimIdx = FindLooseDelimiterIndex(text);
                if (inlineDelimIdx < 0)
                {
                    error = "未找到选项分隔线 ---";
                    return false;
                }
                narration = text.Substring(0, inlineDelimIdx).Trim();
                choiceSection = text.Substring(inlineDelimIdx).TrimStart('-', '—', ' ').Trim();
            }

            if (string.IsNullOrWhiteSpace(narration))
            {
                error = "旁白正文为空";
                return false;
            }
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

        // Fallback: when no standalone "---" line is found, locate the first
        // occurrence of 2+ hyphens or 1+ em dashes that has content on both
        // sides — that string is treated as a loose delimiter. Returns -1 when
        // nothing reasonable can be found.
        private static int FindLooseDelimiterIndex(string text)
        {
            int best = -1;
            for (int i = 0; i < text.Length; i++)
            {
                int runLen = 0;
                if (text[i] == '-')
                {
                    while (i + runLen < text.Length && text[i + runLen] == '-') runLen++;
                }
                else if (text[i] == '—')
                {
                    while (i + runLen < text.Length && text[i + runLen] == '—') runLen++;
                }
                else continue;

                if (runLen >= 2 || (runLen == 1 && text[i] == '—'))
                {
                    // Prefer the first run with content on BOTH sides.
                    if (i > 0 && i + runLen < text.Length) return i;
                    if (best < 0) best = i;
                }
                i += runLen - 1;
            }
            return best;
        }
    }
}
