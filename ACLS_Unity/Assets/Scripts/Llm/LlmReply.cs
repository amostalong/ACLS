using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        // Tolerant parser: strips ```json fences, leading/trailing prose,
        // tries to recover the first complete {...} JSON object.
        public static bool TryParse(string raw, out LlmReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "LLM 返回为空";
                return false;
            }

            string text = raw.Trim();

            // Strip ```json ... ``` fences if present.
            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0) text = text.Substring(firstNewline + 1);
                int closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence >= 0) text = text.Substring(0, closingFence);
                text = text.Trim();
            }

            // Find outermost {...}
            int openIdx = text.IndexOf('{');
            int closeIdx = text.LastIndexOf('}');
            if (openIdx < 0 || closeIdx <= openIdx)
            {
                error = "未找到 JSON 对象（{...}）";
                return false;
            }
            string json = text.Substring(openIdx, closeIdx - openIdx + 1);

            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (JsonException ex) { error = "JSON 解析失败：" + ex.Message; return false; }

            string narration = (string)obj["narration"];
            if (string.IsNullOrWhiteSpace(narration))
            {
                error = "narration 字段缺失或为空";
                return false;
            }

            var result = new LlmReply { Narration = narration.Trim() };
            result.Thinking = ((string)obj["thinking"] ?? "").Trim();

            // scene_participants (optional but expected)
            if (obj["scene_participants"] is JArray pArr)
            {
                foreach (var p in pArr)
                {
                    var name = (string)p["name"];
                    var role = (string)p["role"];
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.SceneParticipants.Add(new Participant
                    {
                        Name = name.Trim(),
                        Role = (role ?? "").Trim(),
                    });
                }
            }

            // choices (required for step-2; allow empty for terminal scenes but warn)
            if (obj["choices"] is JArray cArr)
            {
                foreach (var c in cArr)
                {
                    var label = (string)c["label"];
                    var outcome = (string)c["outcome_narration"];
                    if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(outcome)) continue;

                    int days = 0;
                    var dToken = c["days_passed"];
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

                    if (c["effects"] is JArray eArr)
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
                return false;
            }

            // _system (optional engine directives hidden from the player)
            if (obj["_system"] is JObject sysObj)
            {
                result.System = new SystemBlock
                {
                    SuggestedState = ((string)sysObj["suggested_state"])?.Trim() ?? "",
                };

                if (sysObj["skill_triggers"] is JArray skArr)
                {
                    foreach (var sk in skArr)
                    {
                        var s = (string)sk;
                        if (!string.IsNullOrWhiteSpace(s))
                            result.System.SkillTriggers.Add(s.Trim());
                    }
                }

                if (sysObj["effects"] is JArray seArr)
                {
                    foreach (var e in seArr)
                    {
                        var spec = ParseEffect(e);
                        if (spec != null) result.System.Effects.Add(spec);
                    }
                }
            }

            reply = result;
            return true;
        }

        private static EffectSpec ParseEffect(JToken e)
        {
            var kind = (string)e["kind"];
            if (string.IsNullOrWhiteSpace(kind)) return null;
            return new EffectSpec
            {
                Kind = kind.Trim(),
                Stat = (string)e["stat"],
                Trait = (string)e["trait"],
                Target = (string)e["target"],
                Flag = (string)e["flag"],
                Delta = e["delta"]?.Type == JTokenType.Integer ? (int)e["delta"] : 0,
            };
        }
    }
}
