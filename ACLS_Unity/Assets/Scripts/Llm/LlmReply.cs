using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Llm
{
    // Step-2 schema. The LLM returns a *scene* (narration + who's present)
    // plus 3-4 *choices*; each choice carries its own outcome and effects so
    // the UI can render the consequence instantly when clicked.
    //
    // {
    //   "narration": "<scene description>",
    //   "scene_participants": [ {"name": "...", "role": "..."}, ... ],
    //   "choices": [ ... ],
    //   "_system": {
    //     "suggested_state": "DeepDialogue|Combat|Travel|Planning|FreeNarrative",
    //     "skill_triggers": ["npc-psychology", "relationship-tracker"],
    //     "effects": [ {"kind": "...", ...} ]
    //   }
    // }
    //
    // _system is optional. It carries directives for the game engine that
    // must NOT be shown to the player.
    public sealed class LlmReply
    {
        public string Thinking;
        public string Date;           // LLM-friendly date: "0184年01月08日"
        public int DaysPassed;
        public string Narration;
        public List<Participant> SceneParticipants = new List<Participant>();
        public List<Choice> Choices = new List<Choice>();
        public SystemBlock System;   // optional, parsed from _system

        public sealed class Participant
        {
            public string Name;
            public string Role;
        }

        public sealed class Choice
        {
            public string Label;
            public string OutcomeNarration;
            public int DaysPassed;
            public List<EffectSpec> Effects = new List<EffectSpec>();
        }

        // System-level directives hidden from the player.
        public sealed class SystemBlock
        {
            public string SuggestedState;      // e.g. "DeepDialogue"
            public List<string> SkillTriggers = new List<string>();
            public List<EffectSpec> Effects = new List<EffectSpec>();
            public List<Participant> SceneParticipants = new List<Participant>();
        }

        // Symbolic effect — ChatBridge / EffectParser maps to runtime EffectOp.
        // Not every field is used by every kind; fields are nullable strings to
        // keep parsing forgiving.
        public sealed class EffectSpec
        {
            public string Kind;       // AdjustStat / AdjustOpinion / AdjustGold / AddTrait / RemoveTrait / SetFlag / ClearFlag
            public string Stat;       // Wu / Tong / Zhi / Zheng / Mei
            public string Trait;      // cautious / decisive / studious
            public string Target;     // character name (Chinese)
            public string Flag;       // world flag key (SetFlag / ClearFlag)
            public int Delta;
        }

        public static bool TryParseEffectsOnly(string raw, out LlmReply reply, out string error)
        {
            reply = null;
            error = null;

            // No-op fast path: reasoning models (e.g. deepseek-v4-flash) often emit
            // blank/whitespace content when they decide "no effects to land" — e.g.
            // the opening turn has no player action yet. Treat empty / JSON-less
            // content as a successful empty-effects reply instead of a parse failure.
            if (string.IsNullOrWhiteSpace(raw)
                || raw.Trim().IndexOf('{') < 0)
            {
                reply = new LlmReply();
                error = null;
                return true;
            }

            if (!TryParseJsonObject(raw, out var obj, out error))
                return false;

            var result = new LlmReply();
            result.Date = ((string)(obj["dt"] ?? obj["date"]) ?? "").Trim();
            var dpToken = obj["dp"] ?? obj["days_passed"];
            if (dpToken != null && dpToken.Type != JTokenType.Null)
            {
                try { result.DaysPassed = (int)dpToken; } catch { result.DaysPassed = 0; }
                if (result.DaysPassed < 0) result.DaysPassed = 0;
                if (result.DaysPassed > 365) result.DaysPassed = 365;
            }
            result.System = new SystemBlock
            {
                SuggestedState = ((string)(obj["ss"] ?? obj["suggested_state"]) ?? "").Trim(),
            };

            if ((obj["sk"] ?? obj["skill_triggers"]) is JArray skArr)
            {
                foreach (var sk in skArr)
                {
                    var s = (string)sk;
                    if (!string.IsNullOrWhiteSpace(s))
                        result.System.SkillTriggers.Add(s.Trim());
                }
            }

            if ((obj["ef"] ?? obj["effects"]) is JArray eArr)
            {
                foreach (var e in eArr)
                {
                    var spec = ParseEffect(e);
                    if (spec != null) result.System.Effects.Add(spec);
                }
            }

            if ((obj["sp"] ?? obj["scene_participants"]) is JArray pArr)
            {
                foreach (var p in pArr)
                {
                    var name = (string)(p["n"] ?? p["name"]);
                    var role = (string)(p["r"] ?? p["role"]);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.System.SceneParticipants.Add(new Participant
                    {
                        Name = name.Trim(),
                        Role = (role ?? "").Trim(),
                    });
                }
            }

            reply = result;
            return true;
        }

        // Tolerant parser: strips ```json fences, leading/trailing prose,
        // tries to recover the first complete {...} JSON object.
        public static bool TryParse(string raw, out LlmReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "LLM 返回为空";
                Log.Warn(Log.Channels.LlmReply, "❌ 解析失败: {0}", error);
                Log.Debug(Log.Channels.LlmReply, "原始响应为空");
                return false;
            }

            if (!TryParseJsonObject(raw, out var obj, out error))
                return false;

            string narration = ((string)(obj["nar"] ?? obj["narration"]) ?? "");
            if (string.IsNullOrWhiteSpace(narration))
            {
                error = "narration 字段缺失或为空";
                Log.Warn(Log.Channels.LlmReply, "❌ {0}", error);
                Log.Debug(Log.Channels.LlmReply, "原始响应:\n{0}", raw);
                return false;
            }

            var result = new LlmReply { Narration = narration.Trim() };
            result.Thinking = ((string)(obj["th"] ?? obj["thinking"]) ?? "").Trim();
            result.Date = ((string)(obj["dt"] ?? obj["date"]) ?? "").Trim();

            // scene_participants (optional but expected)
            if ((obj["sp"] ?? obj["scene_participants"]) is JArray pArr)
            {
                foreach (var p in pArr)
                {
                    var name = (string)(p["n"] ?? p["name"]);
                    var role = (string)(p["r"] ?? p["role"]);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.SceneParticipants.Add(new Participant
                    {
                        Name = name.Trim(),
                        Role = (role ?? "").Trim(),
                    });
                }
            }

            // choices (required for step-2; allow empty for terminal scenes but warn)
            if ((obj["ch"] ?? obj["choices"]) is JArray cArr)
            {
                foreach (var c in cArr)
                {
                    var label = (string)(c["lb"] ?? c["label"]);
                    var outcome = (string)(c["on"] ?? c["outcome_narration"]);
                    if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(outcome)) continue;

                    int days = 0;
                    var dToken = c["dp"] ?? c["days_passed"];
                    if (dToken != null && dToken.Type != JTokenType.Null)
                    {
                        try { days = (int)dToken; } catch { /* leave 0 */ }
                    }
                    if (days < 0) days = 0;
                    if (days > 365) days = 365;

                    var choice = new Choice
                    {
                        Label = label.Trim(),
                        OutcomeNarration = outcome.Trim(),
                        DaysPassed = days,
                    };

                    if ((c["ef"] ?? c["effects"]) is JArray eArr)
                    {
                        foreach (var e in eArr)
                        {
                            var spec = ParseEffect(e);
                            if (spec != null) choice.Effects.Add(spec);
                        }
                    }

                    result.Choices.Add(choice);
                }
            }

            if (result.Choices.Count == 0)
            {
                error = "choices 为空（场景至少要给 1 个可选行动）";
                Log.Warn(Log.Channels.LlmReply, "❌ {0}", error);
                Log.Debug(Log.Channels.LlmReply, "原始响应:\n{0}", raw);
                return false;
            }

            // _system (optional engine directives hidden from the player)
            if ((obj["_s"] ?? obj["_system"]) is JObject sysObj)
            {
                result.System = new SystemBlock
                {
                    SuggestedState = ((string)(sysObj["ss"] ?? sysObj["suggested_state"]) ?? "").Trim(),
                };

                if ((sysObj["sk"] ?? sysObj["skill_triggers"]) is JArray skArr)
                {
                    foreach (var sk in skArr)
                    {
                        var s = (string)sk;
                        if (!string.IsNullOrWhiteSpace(s))
                            result.System.SkillTriggers.Add(s.Trim());
                    }
                }

                if ((sysObj["ef"] ?? sysObj["effects"]) is JArray seArr)
                {
                    foreach (var e in seArr)
                    {
                        var spec = ParseEffect(e);
                        if (spec != null) result.System.Effects.Add(spec);
                    }
                }
            }

            reply = result;

            // 日志输出 LLM 返回的解析结果
            var choiceLabels = string.Join(" | ", result.Choices.Select(c => c.Label));
            Log.Info(Log.Channels.LlmReply,
                "✅ 成功解析标准叙事回复"
                + " | narration长度={0}"
                + " | choices={1} [{2}]"
                + " | participants={3}"
                + " | 有_system={4}"
                + " | raw长度={5}",
                result.Narration.Length,
                result.Choices.Count, choiceLabels,
                result.SceneParticipants.Count,
                result.System != null,
                raw.Length);

            return true;
        }

        private static bool TryParseJsonObject(string raw, out JObject obj, out string error)
        {
            obj = null;
            error = null;

            string text = raw.Trim();
            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0) text = text.Substring(firstNewline + 1);
                int closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence >= 0) text = text.Substring(0, closingFence);
                text = text.Trim();
            }

            int openIdx = text.IndexOf('{');
            int closeIdx = text.LastIndexOf('}');
            if (openIdx < 0 || closeIdx <= openIdx)
            {
                error = "未找到 JSON 对象（{...}）";
                Log.Warn(Log.Channels.LlmReply, "❌ {0}", error);
                Log.Debug(Log.Channels.LlmReply, "原始响应:\n{0}", raw);
                return false;
            }

            string json = text.Substring(openIdx, closeIdx - openIdx + 1);
            try { obj = JObject.Parse(json); }
            catch (JsonException ex)
            {
                error = "JSON 解析失败：" + ex.Message;
                Log.Warn(Log.Channels.LlmReply, "❌ {0}", error);
                Log.Debug(Log.Channels.LlmReply, "原始响应:\n{0}", raw);
                return false;
            }

            return true;
        }

        private static EffectSpec ParseEffect(JToken e)
        {
            var kind = (string)(e["kd"] ?? e["kind"]);
            if (string.IsNullOrWhiteSpace(kind)) return null;
            return new EffectSpec
            {
                Kind = kind.Trim(),
                Stat = (string)(e["st"] ?? e["stat"]),
                Trait = (string)(e["tr"] ?? e["trait"]),
                Target = (string)(e["tg"] ?? e["target"]),
                Flag = (string)(e["fl"] ?? e["flag"]),
                Delta = (e["dl"] ?? e["delta"])?.Type == JTokenType.Integer ? (int)(e["dl"] ?? e["delta"]) : 0,
            };
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
