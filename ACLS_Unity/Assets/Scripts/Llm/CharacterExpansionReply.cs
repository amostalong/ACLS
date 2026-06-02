using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Llm
{
    // JSON schema returned by the LLM during character expansion.
    // {
    //   "family_background": "...",
    //   "social_circle": [
    //     { "name": "...", "relation": "...", "attitude_toward_player": "..." }
    //   ],
    //   "recent_goal": "...",
    //   "secret": "...",
    //   "values": "...",
    //   "starting_assets": {
    //     "connections": [...],
    //     "knowledge": [...],
    //     "items": [...]
    //   }
    // }
    [Serializable]
    public sealed class CharacterExpansionReply
    {
        public string Thinking;
        public string FamilyBackground;
        public List<SocialContact> SocialCircle = new List<SocialContact>();
        public string RecentGoal;
        public string Secret;
        public string Values;
        public ExpansionAssets StartingAssets = new ExpansionAssets();

        [Serializable]
        public sealed class SocialContact
        {
            public string Name;
            public string Relation;
            public string AttitudeTowardPlayer;
        }

        [Serializable]
        public sealed class ExpansionAssets
        {
            public List<string> Connections = new List<string>();
            public List<string> Knowledge = new List<string>();
            public List<string> Items = new List<string>();
        }

        public static bool TryParse(string raw, out CharacterExpansionReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "LLM 返回为空";
                Log.Warn(Log.Channels.CharExpand, "❌ {0}", error);
                Log.Trace(Log.Channels.CharExpand, "原始响应为空");
                return false;
            }

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
                error = "未找到 JSON 对象";
                Log.Warn(Log.Channels.CharExpand, "❌ {0}", error);
                Log.Trace(Log.Channels.CharExpand, "原始响应:\n{0}", raw);
                return false;
            }
            string json = text.Substring(openIdx, closeIdx - openIdx + 1);

            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (JsonException ex) { error = "JSON 解析失败：" + ex.Message; Log.Warn(Log.Channels.CharExpand, "❌ {0}", error); Log.Trace(Log.Channels.CharExpand, "原始响应:\n{0}", raw); return false; }

            var result = new CharacterExpansionReply();
            result.Thinking = ((string)obj["thinking"] ?? "").Trim();

            result.FamilyBackground = (string)obj["family_background"] ?? "";
            result.RecentGoal = (string)obj["recent_goal"] ?? "";
            result.Secret = (string)obj["secret"] ?? "";
            result.Values = (string)obj["values"] ?? "";

            if (obj["social_circle"] is JArray sArr)
            {
                foreach (var s in sArr)
                {
                    var name = (string)s["name"];
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.SocialCircle.Add(new SocialContact
                    {
                        Name = name.Trim(),
                        Relation = ((string)s["relation"] ?? "").Trim(),
                        AttitudeTowardPlayer = ((string)s["attitude_toward_player"] ?? "").Trim(),
                    });
                }
            }

            if (obj["starting_assets"] is JObject aObj)
            {
                result.StartingAssets.Connections = ParseStringArray(aObj["connections"]);
                result.StartingAssets.Knowledge = ParseStringArray(aObj["knowledge"]);
                result.StartingAssets.Items = ParseStringArray(aObj["items"]);
            }

            reply = result;

            // 日志输出
            var socialSummary = string.Join(", ", result.SocialCircle.Select(s => $"{s.Name}({s.Relation})"));
            Log.Info(Log.Channels.CharExpand,
                "✅ 成功解析角色拓展"
                + " | 家族背景长度={0}"
                + " | 社交圈={1}人 [{2}]"
                + " | 近期目标={3} 秘密={4} 价值观={5}"
                + " | 资产:关系={6} 知识={7} 物品={8}"
                + " | raw长度={9}",
                result.FamilyBackground.Length,
                result.SocialCircle.Count, socialSummary,
                !string.IsNullOrWhiteSpace(result.RecentGoal),
                !string.IsNullOrWhiteSpace(result.Secret),
                !string.IsNullOrWhiteSpace(result.Values),
                result.StartingAssets.Connections.Count,
                result.StartingAssets.Knowledge.Count,
                result.StartingAssets.Items.Count,
                raw.Length);

            return true;
        }

        private static List<string> ParseStringArray(JToken token)
        {
            var list = new List<string>();
            if (token is JArray arr)
            {
                foreach (var t in arr)
                {
                    var s = (string)t;
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
            }
            return list;
        }
    }
}
