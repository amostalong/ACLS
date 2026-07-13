using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ACLS.Logging;

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

        // Matches a choice line. Accepts many LLM deviations:
        //   1. xxx  /  1) xxx  /  1）xxx  /  1、xxx
        //   ① ② ...  ⑩  (followed by space, no required punct)
        //   第一项 / 第一项 xxx / 选项一 xxx / A. xxx / a) xxx
        //   * xxx / - xxx / · xxx   (loose bullet)
        // Group 1 is the captured label.
        private static readonly Regex ChoiceLineRegex = new Regex(
            @"^\s*(?:(?:\d{1,2}|[①-⑩])\s*[\.\u3001\)\uFF09]\s*|[①-⑩]\s*|(?:第[一-十百]+项|选项?[一-十百]+|[\*\-•·])\s*)(.{2,}?)\s*$",
            RegexOptions.Compiled);

        // Loose fallback: a non-empty line that is short enough to be a choice label.
        // Used only when the strict ChoiceLineRegex matched zero rows.
        private static readonly Regex LooseChoiceLineRegex = new Regex(
            @"^\s*(.{2,40}[。！？]?\s*)$",
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
                    // Last resort: treat the whole response as narration (no choices).
                    // The 0-choices fallback below will inject the default option.
                    narration = text;
                    choiceSection = "";
                    ACLS.Logging.Log.Warn(ACLS.Logging.Log.Channels.LlmReply,
                        "[NarrationParser] 未找到分隔线 | 原文长度={0} | 全部作为 narration + 注入默认选项",
                        narration.Length);
                }
                else
                {
                    narration = text.Substring(0, inlineDelimIdx).Trim();
                    choiceSection = text.Substring(inlineDelimIdx).TrimStart('-', '—', ' ').Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(narration))
            {
                // Last-resort fallback: use the raw text (truncated) as narration
                // so the UI isn't blank when LLM forgot to emit any separator at all.
                string fallback = text ?? "";
                if (fallback.Length > 200) fallback = fallback.Substring(0, 200) + "…";
                narration = fallback;
                if (string.IsNullOrWhiteSpace(narration))
                {
                    error = "旁白正文为空";
                    return false;
                }
                ACLS.Logging.Log.Warn(ACLS.Logging.Log.Channels.LlmReply,
                    "[NarrationParser] 未找到分隔线 | 使用原文前 {0} 字作为旁白",
                    narration.Length);
            }
            if (string.IsNullOrWhiteSpace(choiceSection))
            {
                // No delimiter at all → no choice section to parse. Synthesize one
                // with the default "继续故事" option; the loop below will fall through.
                choiceSection = "";
            }

            string[] lines = choiceSection.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.Length > 60) continue;   // too long to be a button label

                var m = ChoiceLineRegex.Match(line);
                string label;
                if (m.Success)
                {
                    // Group 1 is the captured label (the leading digit prefix is in a non-capturing group).
                    label = m.Groups[1].Value.Trim();
                }
                else
                {
                    // Loose fallback: accept any short line that doesn't look like
                    // a header / meta / section title. This is the safety net for
                    // LLMs that emit choices without numbering at all.
                    var lm = LooseChoiceLineRegex.Match(line);
                    if (!lm.Success) continue;
                    label = lm.Groups[1].Value.Trim();
                }

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
                // Fallback: LLM 返回了旁白正文但漏写了选项 / 分隔线。
                // 注入一个默认「继续故事」选项，让玩家能继续推进而不是卡死。
                // 严格模式仍保留 error 字段供上游诊断。
                error = "未解析到任何选项（已注入默认「继续故事」）";
                ACLS.Logging.Log.Warn(ACLS.Logging.Log.Channels.LlmReply,
                    "[NarrationParser] {0} | narration长度={1} content前100={2}",
                    error, narration.Length, Truncate(text, 100));
                choices.Add(new LlmReply.Choice
                {
                    Label = "继续故事",
                    OutcomeNarration = "",
                    DaysPassed = 0,
                    Effects = new List<LlmReply.EffectSpec>(),
                });
            }

            if (choices.Count > 4)
                choices.RemoveRange(4, choices.Count - 4);

            return true;
        }

        private static string Truncate(string s, int n) =>
            s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

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

            // 删掉 emoji / 不可打印字符（LLM 常在空白轮返 "🎮" 之类，TMP 字体里缺失会变方块）
            text = StripEmojiAndControl(text).Trim();
            return text;
        }

        // 刷除 LLM 输出中偶发的 emoji 和其他控制字符。保留中日韩、ASCII、常见中文/英文标点。
        private static string StripEmojiAndControl(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int cp = char.ConvertToUtf32(s, i);
                if (cp > 0xFFFF) i++; // surrogate pair
                // 控制字符 (除了 \n \r \t)
                if (cp < 0x20 && cp != '\n' && cp != '\r' && cp != '\t') continue;
                if (cp == 0x7F) continue; // DEL
                // 主要 emoji 区间
                if (cp >= 0x1F300 && cp <= 0x1FAFF) continue;  // emoji blocks (misc symbols, emoticons, transport, etc.)
                if (cp >= 0x2600 && cp <= 0x27BF) continue;    // misc symbols + dingbats
                if (cp >= 0x1F000 && cp <= 0x1F02F) continue;  // mahjong tiles
                if (cp >= 0x1F100 && cp <= 0x1F1FF) continue;  // enclosed alphanumeric supplement (regional indicators)
                if (cp >= 0x1F200 && cp <= 0x1F2FF) continue;  // enclosed ideographic supplement
                sb.Append(char.ConvertFromUtf32(cp));
            }
            return sb.ToString();
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
