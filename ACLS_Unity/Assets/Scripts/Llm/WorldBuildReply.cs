using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        public string L4Text;  // formatted Chinese text for world.Stage.L4World
        public string L3Text;  // formatted Chinese text for world.Stage.L3Expanse
        public string Summary; // one-line world summary shown to player

        public static bool TryParse(string raw, out WorldBuildReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw)) { error = "LLM 返回为空"; return false; }

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
            if (open < 0 || close <= open) { error = "未找到 JSON 对象"; return false; }

            JObject obj;
            try { obj = JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (JsonException ex) { error = "JSON 解析失败：" + ex.Message; return false; }

            var result = new WorldBuildReply();

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
                    }
                }
                if (l4["history_anchors"] is JArray anchors)
                {
                    sb4.Append("历史锚点：");
                    foreach (var a in anchors) sb4.Append((string)a + "  ");
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
                    }
                }
                string tensions = (string)l3["regional_tensions"] ?? "";
                if (!string.IsNullOrWhiteSpace(tensions)) sb3.AppendLine(tensions);
                string sum3 = (string)l3["summary"] ?? "";
                if (!string.IsNullOrWhiteSpace(sum3)) sb3.AppendLine(sum3);
                result.L3Text = sb3.ToString().Trim();
            }

            if (string.IsNullOrWhiteSpace(result.L4Text))
            { error = "l4_world 字段缺失或为空"; return false; }

            reply = result;
            return true;
        }
    }
}
