using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Llm
{
    // JSON schema returned by the LLM during world-build.
    // {
    //   "l4_world": {
    //     "era_name": "...",
    //     "macro_factions": [{"name":"...","status":"..."}],
    //     "history_anchors": ["..."],
    //     "summary": "..."
    //   },
    //   "l3_expanse": {
    //     "region": "...",
    //     "regional_powers": [{"name":"...","stance":"..."}],
    //     "regional_tensions": "...",
    //     "summary": "..."
    //   }
    // }
    [Serializable]
    public sealed class WorldBuildReply
    {
        public string Thinking;
        public string L4Text;  // formatted Chinese text for world.Stage.L4World
        public string L3Text;  // formatted Chinese text for world.Stage.L3Expanse
        public string L2Text;  // formatted Chinese text for world.Stage.L2Arena
        public string L1Text;  // formatted Chinese text for world.Stage.L1Stage
        public string Summary; // one-line world summary shown to player
        public PlayerSpec Player;

        // ---- 结构化实体数据（补充文本内容，供 WorldDataRegistrar 灌入 GameDataLoader） ----
        public List<FactionInfo> L4Factions = new List<FactionInfo>();
        public List<HistoryAnchor> HistoryAnchors = new List<HistoryAnchor>();
        public List<PowerInfo> L3Powers = new List<PowerInfo>();
        public List<NpcInfo> L1Npcs = new List<NpcInfo>();
        public List<string> L1Exits = new List<string>();
        public List<ContactInfo> L2Contacts = new List<ContactInfo>();
        public List<string> L2Pressures = new List<string>();
        public List<string> L2Opportunities = new List<string>();

        // ---- 内层类型（JSON 解析用） ----
        [Serializable]
        public sealed class FactionInfo
        {
            public string name = "";
            public string status = "";
        }

        [Serializable]
        public sealed class HistoryAnchor
        {
            public string text = "";
        }

        [Serializable]
        public sealed class PowerInfo
        {
            public string name = "";
            public string stance = "";
        }

        [Serializable]
        public sealed class NpcInfo
        {
            public string name = "";
            public string role = "";
            public int relation_value;
            public string stance = "";
        }

        [Serializable]
        public sealed class ContactInfo
        {
            public string name = "";
            public string role = "";
            public string location = "";
            public int days_away;
        }

        [Serializable]
        public sealed class PlayerSpec
        {
            public string Name = "";
            public string Courtesy = "";
            public string Sex = "";
            public int Age;
            public string LocationName = "";
            public string Trait = "";
            public string Blurb = "";
        }

        public static bool TryParse(string raw, out WorldBuildReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw)) { error = "LLM 返回为空"; Log.Warn(Log.Channels.WorldBuild, "❌ {0}", error); Log.Trace(Log.Channels.WorldBuild, "原始响应为空"); return false; }

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
            if (open < 0 || close <= open) { error = "未找到 JSON 对象"; Log.Warn(Log.Channels.WorldBuild, "❌ {0}", error); Log.Trace(Log.Channels.WorldBuild, "原始响应:\n{0}", raw); return false; }

            JObject obj;
            try { obj = JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (JsonException ex)
            {
                error = "JSON 解析失败：" + ex.Message;
                Log.Warn(Log.Channels.WorldBuild, "❌ {0}", error);
                Log.Trace(Log.Channels.WorldBuild, "原始响应:\n{0}", raw);
                return false;
            }

            var result = new WorldBuildReply();
            result.Thinking = ((string)obj["thinking"] ?? "").Trim();

            // ---- L4 ----
            var l4 = obj["l4_world"] as JObject;
            if (l4 != null)
            {
                var sb4 = new StringBuilder();
                string eraName = (string)l4["era_name"] ?? "";
                if (!string.IsNullOrWhiteSpace(eraName)) sb4.AppendLine(eraName);

                if (l4["macro_factions"] is JArray factions)
                {
                    foreach (var f in factions)
                    {
                        string fn = ((string)f["name"] ?? "").Trim();
                        string fs = ((string)f["status"] ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(fn)) sb4.AppendLine($"· {fn}：{fs}");
                        if (!string.IsNullOrWhiteSpace(fn))
                            result.L4Factions.Add(new FactionInfo { name = fn, status = fs });
                    }
                }
                if (l4["history_anchors"] is JArray anchors)
                {
                    sb4.Append("历史锚点：");
                    foreach (var a in anchors)
                    {
                        string at = ((string)a ?? "").Trim();
                        sb4.Append(at + "  ");
                        if (!string.IsNullOrWhiteSpace(at))
                            result.HistoryAnchors.Add(new HistoryAnchor { text = at });
                    }
                    sb4.AppendLine();
                }
                string sum4 = (string)l4["summary"] ?? "";
                if (!string.IsNullOrWhiteSpace(sum4)) { sb4.AppendLine(sum4); result.Summary = sum4; }
                result.L4Text = sb4.ToString().Trim();
            }

            // ---- L3 ----
            var l3 = obj["l3_expanse"] as JObject;
            if (l3 != null)
            {
                var sb3 = new StringBuilder();
                string region = (string)l3["region"] ?? "";
                if (!string.IsNullOrWhiteSpace(region)) sb3.AppendLine(region);

                if (l3["regional_powers"] is JArray powers)
                {
                    foreach (var p in powers)
                    {
                        string pn = ((string)p["name"] ?? "").Trim();
                        string ps = ((string)p["stance"] ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(pn)) sb3.AppendLine($"· {pn}：{ps}");
                        if (!string.IsNullOrWhiteSpace(pn))
                            result.L3Powers.Add(new PowerInfo { name = pn, stance = ps });
                    }
                }
                string tensions = (string)l3["regional_tensions"] ?? "";
                if (!string.IsNullOrWhiteSpace(tensions)) sb3.AppendLine(tensions);
                string sum3 = (string)l3["summary"] ?? "";
                if (!string.IsNullOrWhiteSpace(sum3)) sb3.AppendLine(sum3);
                result.L3Text = sb3.ToString().Trim();
            }

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

                if (l1["active_npcs"] is JArray npcs)
                {
                    foreach (var n in npcs)
                    {
                        string name = ((string)n["name"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        string role = ((string)n["role"] ?? "").Trim();
                        string stance = ((string)n["stance"] ?? "").Trim();
                        int rel = n["relation_value"]?.Value<int>() ?? 0;
                        sb1.AppendLine($"· {name}（{role}，关系{rel:+#;-#;0}）：{stance}");
                        result.L1Npcs.Add(new NpcInfo { name = name, role = role, relation_value = rel, stance = stance });
                    }
                }

                if (!string.IsNullOrWhiteSpace(situation)) sb1.AppendLine(situation);

                if (l1["exits"] is JArray exits)
                {
                    sb1.Append("[出口] ");
                    foreach (var e in exits)
                    {
                        string ex = ((string)e ?? "").Trim();
                        sb1.Append(ex + "  ");
                        if (!string.IsNullOrWhiteSpace(ex))
                            result.L1Exits.Add(ex);
                    }
                    sb1.AppendLine();
                }

                result.L1Text = sb1.ToString().Trim();
            }

            // ---- L2 ----
            var l2 = obj["l2_arena"] as JObject;
            if (l2 != null)
            {
                var sb2 = new StringBuilder();
                if (l2["near_contacts"] is JArray contacts)
                {
                    foreach (var c in contacts)
                    {
                        string name = ((string)c["name"] ?? "").Trim();
                        string cRole = ((string)c["role"] ?? "").Trim();
                        string cloc = ((string)c["location"] ?? "").Trim();
                        int days = c["days_away"]?.Value<int>() ?? 0;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            sb2.AppendLine($"· {name}（{cRole}，{cloc}，约{days}天）");
                            result.L2Contacts.Add(new ContactInfo { name = name, role = cRole, location = cloc, days_away = days });
                        }
                    }
                }
                if (l2["active_pressures"] is JArray pressures)
                {
                    foreach (var p in pressures)
                    {
                        string pt = ((string)p ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(pt)) sb2.AppendLine($"⚠ {pt}");
                        if (!string.IsNullOrWhiteSpace(pt)) result.L2Pressures.Add(pt);
                    }
                }
                if (l2["opportunities"] is JArray opps)
                {
                    foreach (var o in opps)
                    {
                        string ot = ((string)o ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(ot)) sb2.AppendLine($"◇ {ot}");
                        if (!string.IsNullOrWhiteSpace(ot)) result.L2Opportunities.Add(ot);
                    }
                }
                result.L2Text = sb2.ToString().Trim();
            }

            // ---- P0 ----
            var p0 = obj["p0_player"] as JObject;
            if (p0 != null)
            {
                result.Player = new PlayerSpec
                {
                    Name = ((string)p0["name"] ?? "").Trim(),
                    Courtesy = ((string)p0["courtesy"] ?? "").Trim(),
                    Sex = ((string)p0["sex"] ?? "").Trim(),
                    Age = p0["age"]?.Value<int>() ?? 0,
                    LocationName = ((string)p0["location_name"] ?? "").Trim(),
                    Trait = ((string)p0["trait"] ?? "").Trim(),
                    Blurb = ((string)p0["blurb"] ?? "").Trim(),
                };
            }

            if (string.IsNullOrWhiteSpace(result.L4Text))
            {
                error = "l4_world 字段缺失或为空";
                Log.Warn(Log.Channels.WorldBuild, "❌ {0}", error);
                Log.Trace(Log.Channels.WorldBuild, "原始响应:\n{0}", raw);
                return false;
            }
            if (string.IsNullOrWhiteSpace(result.L1Text))
            {
                error = "l1_stage 字段缺失或为空";
                Log.Warn(Log.Channels.WorldBuild, "❌ {0}", error);
                Log.Trace(Log.Channels.WorldBuild, "原始响应:\n{0}", raw);
                return false;
            }
            if (result.Player == null || string.IsNullOrWhiteSpace(result.Player.Name))
            {
                error = "p0_player 字段缺失或为空";
                Log.Warn(Log.Channels.WorldBuild, "❌ {0}", error);
                Log.Trace(Log.Channels.WorldBuild, "原始响应:\n{0}", raw);
                return false;
            }

            reply = result;

            // 日志输出
            Log.Info(Log.Channels.WorldBuild,
                "✅ 成功解析世界构建"
                + " | L4长度={0} L3长度={1} L2长度={2} L1长度={3}"
                + " | 玩家={4}({5}), {6}, {7}岁, 位于{8}"
                + " | raw长度={9}",
                result.L4Text.Length, result.L3Text.Length,
                result.L2Text.Length, result.L1Text.Length,
                result.Player.Name, result.Player.Courtesy,
                result.Player.Sex, result.Player.Age,
                result.Player.LocationName,
                raw.Length);

            return true;
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
