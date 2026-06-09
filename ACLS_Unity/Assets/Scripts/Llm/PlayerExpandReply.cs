using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Llm
{
    /// <summary>
    /// 角色丰富化 + 故事线生成步骤（Step 5）的 LLM 回复。
    /// 在 L2 近域网络之后、L1 当前场景之前执行。
    /// </summary>
    [Serializable]
    public sealed class PlayerExpandReply
    {
        public string Thinking = "";
        public string Narration = "";

        // ---- 角色丰富化 ----
        public string BackgroundStory = "";
        public string Values = "";
        public string CurrentGoal = "";
        public string Secret = "";
        public List<string> Connections = new();
        public List<string> KnownFacts = new();
        public List<string> OwnedItems = new();

        // ---- 故事线 ----
        public List<Storyline> Storylines = new();

        // ---- NPC 丰富化 ----
        public List<NpcExpansion> NpcExpansions = new();

        [Serializable]
        public sealed class Storyline
        {
            public string Title = "";
            public string Summary = "";
            public List<string> InvolvedNpcs = new();
            public List<string> InvolvedItems = new();
            public List<string> InvolvedLocations = new();
            public string KeyTimePoint = "";
            public string Hook = "";
        }

        [Serializable]
        public sealed class NpcExpansion
        {
            public string Name = "";
            public string BackgroundStory = "";
            public string Values = "";
            public string CurrentGoal = "";
            public string Secret = "";
        }

        public static bool TryParse(string raw, out PlayerExpandReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "LLM 返回为空";
                Log.Warn(Log.Channels.LlmReply, "[PlayerExpand] ❌ {0}", error);
                return false;
            }

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
            if (open < 0 || close <= open)
            {
                error = "未找到 JSON 对象";
                Log.Warn(Log.Channels.LlmReply, "[PlayerExpand] ❌ {0}", error);
                return false;
            }

            JObject obj;
            try { obj = JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (JsonException ex)
            {
                error = "JSON 解析失败：" + ex.Message;
                Log.Warn(Log.Channels.LlmReply, "[PlayerExpand] ❌ {0}", error);
                return false;
            }

            var result = new PlayerExpandReply();
            result.Thinking = ((string)(obj["th"] ?? obj["thinking"]) ?? "").Trim();
            result.Narration = ((string)(obj["nar"] ?? obj["narration"]) ?? "").Trim();

            // ---- player_expansion ----
            var pe = (obj["pe"] ?? obj["player_expansion"]) as JObject;
            if (pe != null)
            {
                result.BackgroundStory = ((string)(pe["bg"] ?? pe["background_story"]) ?? "").Trim();
                result.Values = ((string)(pe["val"] ?? pe["values"]) ?? "").Trim();
                result.CurrentGoal = ((string)(pe["cg"] ?? pe["current_goal"]) ?? "").Trim();
                result.Secret = ((string)(pe["sec"] ?? pe["secret"]) ?? "").Trim();

                if ((pe["conn"] ?? pe["connections"]) is JArray conns)
                    foreach (var item in conns) { string s = ((string)item ?? "").Trim(); if (!string.IsNullOrWhiteSpace(s)) result.Connections.Add(s); }
                if ((pe["kf"] ?? pe["known_facts"]) is JArray facts)
                    foreach (var item in facts) { string s = ((string)item ?? "").Trim(); if (!string.IsNullOrWhiteSpace(s)) result.KnownFacts.Add(s); }
                if ((pe["oi"] ?? pe["owned_items"]) is JArray items)
                    foreach (var item in items) { string s = ((string)item ?? "").Trim(); if (!string.IsNullOrWhiteSpace(s)) result.OwnedItems.Add(s); }
            }

            // ---- storylines ----
            if ((obj["sl"] ?? obj["storylines"]) is JArray slArr)
            {
                foreach (var sl in slArr)
                {
                    var title = ((string)(sl["ti"] ?? sl["title"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var storyline = new Storyline
                    {
                        Title = title,
                        Summary = ((string)(sl["su"] ?? sl["summary"]) ?? "").Trim(),
                        Hook = ((string)(sl["hk"] ?? sl["hook"]) ?? "").Trim(),
                        KeyTimePoint = ((string)(sl["ktp"] ?? sl["key_time_point"]) ?? "").Trim(),
                    };

                    if ((sl["inp"] ?? sl["involved_npcs"]) is JArray npcArr)
                        foreach (var n in npcArr) { string s = ((string)n ?? "").Trim(); if (!string.IsNullOrWhiteSpace(s)) storyline.InvolvedNpcs.Add(s); }
                    if ((sl["ini"] ?? sl["involved_items"]) is JArray itemArr)
                        foreach (var it in itemArr) { string s = ((string)it ?? "").Trim(); if (!string.IsNullOrWhiteSpace(s)) storyline.InvolvedItems.Add(s); }
                    if ((sl["inl"] ?? sl["involved_locations"]) is JArray locArr)
                        foreach (var loc in locArr) { string s = ((string)loc ?? "").Trim(); if (!string.IsNullOrWhiteSpace(s)) storyline.InvolvedLocations.Add(s); }

                    result.Storylines.Add(storyline);
                }
            }

            // ---- npc_expansions ----
            if ((obj["ne"] ?? obj["npc_expansions"]) is JArray npcExArr)
            {
                foreach (var npc in npcExArr)
                {
                    var name = ((string)(npc["n"] ?? npc["name"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var expansion = new NpcExpansion
                    {
                        Name = name,
                        BackgroundStory = ((string)(npc["bg"] ?? npc["background_story"]) ?? "").Trim(),
                        Values = ((string)(npc["val"] ?? npc["values"]) ?? "").Trim(),
                        CurrentGoal = ((string)(npc["cg"] ?? npc["current_goal"]) ?? "").Trim(),
                        Secret = ((string)(npc["sec"] ?? npc["secret"]) ?? "").Trim(),
                    };

                    result.NpcExpansions.Add(expansion);
                }
            }

            if (result.Storylines.Count == 0)
            {
                error = "storylines 为空（至少需要 1 条故事线）";
                Log.Warn(Log.Channels.LlmReply, "[PlayerExpand] ❌ {0}", error);
                return false;
            }

            reply = result;
            Log.Info(Log.Channels.LlmReply,
                "[PlayerExpand] ✅ bg={0} goal={1} secret={2} storylines={3} npcs={4}",
                result.BackgroundStory.Length > 0 ? "有" : "无",
                result.CurrentGoal.Length > 0 ? "有" : "无",
                result.Secret.Length > 0 ? "有" : "无",
                result.Storylines.Count,
                result.NpcExpansions.Count);
            return true;
        }

        /// <summary>生成供 L1 步骤使用的上下文文本。</summary>
        public string ToContextText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[角色背景]");
            if (!string.IsNullOrWhiteSpace(BackgroundStory)) sb.AppendLine(BackgroundStory);
            if (!string.IsNullOrWhiteSpace(Values)) sb.AppendLine($"\n[价值观] {Values}");
            if (!string.IsNullOrWhiteSpace(CurrentGoal)) sb.AppendLine($"[近期目标] {CurrentGoal}");
            if (!string.IsNullOrWhiteSpace(Secret)) sb.AppendLine($"[秘密] {Secret}");
            if (Connections.Count > 0) sb.AppendLine("\n[人脉] " + string.Join("、", Connections));
            if (KnownFacts.Count > 0) sb.AppendLine("[已知情报] " + string.Join("、", KnownFacts));
            if (OwnedItems.Count > 0) sb.AppendLine("[随身物品] " + string.Join("、", OwnedItems));

            if (Storylines.Count > 0)
            {
                sb.AppendLine("\n[活跃故事线]");
                foreach (var sl in Storylines)
                {
                    sb.AppendLine($"· {sl.Title}：{sl.Summary}");
                    if (sl.InvolvedNpcs.Count > 0)
                        sb.AppendLine($"  涉及人物：{string.Join("、", sl.InvolvedNpcs)}");
                    if (sl.InvolvedItems.Count > 0)
                        sb.AppendLine($"  涉及事物：{string.Join("、", sl.InvolvedItems)}");
                    if (sl.InvolvedLocations.Count > 0)
                        sb.AppendLine($"  涉及地点：{string.Join("、", sl.InvolvedLocations)}");
                    if (!string.IsNullOrWhiteSpace(sl.KeyTimePoint))
                        sb.AppendLine($"  关键时间：{sl.KeyTimePoint}");
                    if (!string.IsNullOrWhiteSpace(sl.Hook))
                        sb.AppendLine($"  切入点：{sl.Hook}");
                }
            }

            if (NpcExpansions.Count > 0)
            {
                sb.AppendLine("\n[已丰富的主要人物]");
                foreach (var npc in NpcExpansions)
                {
                    sb.AppendLine($"· {npc.Name}");
                    if (!string.IsNullOrWhiteSpace(npc.BackgroundStory)) sb.AppendLine($"  背景：{npc.BackgroundStory}");
                    if (!string.IsNullOrWhiteSpace(npc.Values)) sb.AppendLine($"  价值观：{npc.Values}");
                    if (!string.IsNullOrWhiteSpace(npc.CurrentGoal)) sb.AppendLine($"  当前目标：{npc.CurrentGoal}");
                    if (!string.IsNullOrWhiteSpace(npc.Secret)) sb.AppendLine($"  秘密：{npc.Secret}");
                }
            }

            return sb.ToString().Trim();
        }
    }
}
