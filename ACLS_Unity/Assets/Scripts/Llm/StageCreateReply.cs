using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Llm
{
    // JSON schema returned by the LLM during stage-create.
    // {
    //   "l1_stage": {
    //     "location": "颍川·阳翟",
    //     "scene_description": "...",
    //     "active_npcs": [{"name":"...","role":"...","relation_value":0,"stance":"..."}],
    //     "immediate_situation": "...",
    //     "exits": ["北往洛阳 约3天", "东往许县 约2天"]
    //   },
    //   "l2_arena": {
    //     "chars": [{"name":"...","role":"...","location":"...","relation":0,"reachable_in_days":1}],
    //     "factions": [{"name":"...","type":"...","stance":"..."}],
    //     "places": [{"name":"...","type":"...","description":"..."}],
    //     "active_events": [{"title":"...","urgency":"high|medium|low","deadline":"...","detail":"..."}],
    //     "opportunities": ["..."]
    //   }
    // }
    [Serializable]
    public sealed class StageCreateReply
    {
        public string Thinking;
        public string L1Text;              // formatted text for world.Stage.L1Stage
        public string L2Text;              // formatted text for world.Stage.L2Arena
        public string SceneDescription;    // displayed to the player as narration
        public List<StageNpc> ActiveNpcs = new List<StageNpc>();

        [Serializable]
        public sealed class StageNpc
        {
            public string Name;
            public string Role;
            public int RelationValue;
            public string Stance;
        }

        public static bool TryParse(string raw, out StageCreateReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw)) { error = "LLM 返回为空"; Log.Warn(Log.Channels.Stage, "❌ {0}", error); Log.Debug(Log.Channels.Stage, "原始响应为空"); return false; }

            string text = raw.Trim();
            if (text.StartsWith("```"))
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(nl + 1);
                int fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) text = text.Substring(0, fence);
                text = text.Trim();
            }

            int open = text.IndexOf('{');
            int close = text.LastIndexOf('}');
            if (open < 0 || close <= open) { error = "未找到 JSON 对象"; Log.Warn(Log.Channels.Stage, "❌ {0}", error); Log.Debug(Log.Channels.Stage, "原始响应:\n{0}", raw); return false; }

            JObject obj;
            try { obj = JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (JsonException ex) { error = "JSON 解析失败：" + ex.Message; Log.Warn(Log.Channels.Stage, "❌ {0}", error); Log.Debug(Log.Channels.Stage, "原始响应:\n{0}", raw); return false; }

            var result = new StageCreateReply();
            result.Thinking = ((string)obj["thinking"] ?? "").Trim();

            // ---- L1 ----
            var l1 = obj["l1_stage"] as JObject;
            if (l1 != null)
            {
                var sb1 = new StringBuilder();
                string loc = ((string)l1["location"] ?? "").Trim();
                string scene = ((string)l1["scene_description"] ?? "").Trim();
                string situation = ((string)l1["immediate_situation"] ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(loc)) sb1.AppendLine($"[所在] {loc}");
                if (!string.IsNullOrWhiteSpace(scene)) sb1.AppendLine(scene);
                result.SceneDescription = scene;

                if (l1["active_npcs"] is JArray npcs)
                {
                    foreach (var n in npcs)
                    {
                        string name = ((string)n["name"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string role  = ((string)n["role"] ?? "").Trim();
                        string stance = ((string)n["stance"] ?? "").Trim();
                        int rel = n["relation_value"]?.Value<int>() ?? 0;
                        sb1.AppendLine($"· {name}（{role}，关系{rel:+#;-#;0}）：{stance}");
                        result.ActiveNpcs.Add(new StageNpc { Name = name, Role = role, RelationValue = rel, Stance = stance });
                    }
                }
                if (!string.IsNullOrWhiteSpace(situation)) sb1.AppendLine(situation);

                if (l1["exits"] is JArray exits)
                {
                    sb1.Append("[出口] ");
                    foreach (var e in exits) sb1.Append((string)e + "  ");
                    sb1.AppendLine();
                }
                result.L1Text = sb1.ToString().Trim();
            }

            // ---- L2 ----
            var l2 = obj["l2_arena"] as JObject;
            if (l2 != null)
            {
                var sb2 = new StringBuilder();

                // Chars
                if (l2["chars"] is JArray chars)
                {
                    sb2.AppendLine("【人物】");
                    foreach (var c in chars)
                    {
                        string name = ((string)c["name"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string role  = ((string)c["role"] ?? "").Trim();
                        string cloc  = ((string)c["location"] ?? "").Trim();
                        int rel = c["relation"]?.Value<int>() ?? 0;
                        int days = c["reachable_in_days"]?.Value<int>() ?? 0;
                        sb2.AppendLine($"· {name}（{role}，{cloc}，关系{rel:+#;-#;0}，约{days}天）");
                    }
                }

                // Factions
                if (l2["factions"] is JArray factions)
                {
                    sb2.AppendLine("【势力】");
                    foreach (var f in factions)
                    {
                        string fn = ((string)f["name"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(fn)) continue;
                        sb2.AppendLine($"▸ {fn}（{((string)f["type"] ?? "")}）：{((string)f["stance"] ?? "")}");
                    }
                }

                // Places
                if (l2["places"] is JArray places)
                {
                    sb2.AppendLine("【地点】");
                    foreach (var p in places)
                    {
                        string pn = ((string)p["name"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(pn)) continue;
                        sb2.AppendLine($"· {pn}（{((string)p["type"] ?? "")}）：{((string)p["description"] ?? "")}");
                    }
                }

                // Active events
                if (l2["active_events"] is JArray events)
                {
                    sb2.AppendLine("【事件】");
                    foreach (var e in events)
                    {
                        string title = ((string)e["title"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        string urgency = ((string)e["urgency"] ?? "medium").Trim();
                        string deadline = ((string)e["deadline"] ?? "ongoing").Trim();
                        string detail = ((string)e["detail"] ?? "").Trim();
                        string prefix = urgency == "high" ? "🔴" : urgency == "medium" ? "🟠" : "🟢";
                        sb2.AppendLine($"{prefix} {title}（{deadline}）：{detail}");
                    }
                }

                // Opportunities
                if (l2["opportunities"] is JArray opps)
                {
                    sb2.AppendLine("【机遇】");
                    foreach (var o in opps)
                    {
                        string ot = ((string)o ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(ot)) sb2.AppendLine($"◇ {ot}");
                    }
                }
                result.L2Text = sb2.ToString().Trim();
            }

            if (string.IsNullOrWhiteSpace(result.L1Text))
            { error = "l1_stage 字段缺失或为空"; Log.Warn(Log.Channels.Stage, "❌ {0}", error); Log.Debug(Log.Channels.Stage, "原始响应:\n{0}", raw); return false; }

            reply = result;

            // 日志输出
            var npcNames = string.Join(", ", result.ActiveNpcs.Select(n => $"{n.Name}({n.Role})"));
            Log.Info(Log.Channels.Stage,
                "✅ 成功解析舞台创建"
                + " | L1长度={0} L2长度={1}"
                + " | NPC数={2} [{3}]"
                + " | 有场景描述={4}"
                + " | raw长度={5}",
                result.L1Text.Length, result.L2Text.Length,
                result.ActiveNpcs.Count, npcNames,
                !string.IsNullOrWhiteSpace(result.SceneDescription),
                raw.Length);

            return true;
        }
    }
}
