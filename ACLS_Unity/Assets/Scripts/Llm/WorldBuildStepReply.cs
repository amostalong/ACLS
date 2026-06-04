using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Llm
{
    /// <summary>
    /// 世界观设定层（World Build Step 1）的 LLM 回复。
    /// LLM 生成整个世界观基座（时代、叙事风格、认知边界、历史锚点）。
    /// 输出存储到 world.Stage.WorldBuild 中，作为后续 L4/L3/L2/L1 各层的上下文根基。
    /// </summary>
    [Serializable]
    public sealed class WorldBuildStepReply
    {
        public string Thinking = "";
        public string Narration = "";                       // DM 叙事文本
        public string EraName = "";                          // 时代名称，如"东汉末年·中平元年（184年）"
        public string NarrativeStyle = "";                 // 叙事风格，如"近似古典白话小说"
        public string NarrativePerspective = "";           // 叙事视角，如"第一人称"
        public List<string> CognitiveBoundaries = new();   // 认知限制列表
        public string WorldUndertones = "";                // 世界底色/当下特点
        public List<string> HistoricalAnchors = new();     // 历史锚点
        public string WorldSummary = "";                   // 一句话世界摘要

        public static bool TryParse(string raw, out WorldBuildStepReply reply, out string error)
        {
            reply = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "LLM 返回为空";
                Log.Warn(Log.Channels.WorldBuild, "[WorldBuild] ❌ {0}", error);
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
                Log.Warn(Log.Channels.WorldBuild, "[WorldBuild] ❌ {0}", error);
                return false;
            }

            JObject obj;
            try { obj = JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (JsonException ex)
            {
                error = "JSON 解析失败：" + ex.Message;
                Log.Warn(Log.Channels.WorldBuild, "[WorldBuild] ❌ {0}", error);
                return false;
            }

            var result = new WorldBuildStepReply();
            result.Thinking = ((string)obj["thinking"] ?? "").Trim();
            result.Narration = ((string)obj["narration"] ?? "").Trim();
            result.EraName = ((string)obj["era_name"] ?? "").Trim();
            result.NarrativeStyle = ((string)obj["narrative_style"] ?? "").Trim();
            result.NarrativePerspective = ((string)obj["narrative_perspective"] ?? "").Trim();
            result.WorldUndertones = ((string)obj["world_undertones"] ?? "").Trim();
            result.WorldSummary = ((string)obj["world_summary"] ?? "").Trim();

            if (obj["cognitive_boundaries"] is JArray cb)
            {
                foreach (var item in cb)
                {
                    string s = ((string)item ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) result.CognitiveBoundaries.Add(s);
                }
            }

            if (obj["historical_anchors"] is JArray ha)
            {
                foreach (var item in ha)
                {
                    string s = ((string)item ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) result.HistoricalAnchors.Add(s);
                }
            }

            if (string.IsNullOrWhiteSpace(result.EraName))
            {
                error = "era_name 字段缺失或为空";
                Log.Warn(Log.Channels.WorldBuild, "[WorldBuild] ❌ {0}", error);
                return false;
            }

            reply = result;
            Log.Info(Log.Channels.WorldBuild,
                "[WorldBuild] ✅ era={0} style={1} anchors={2} summary={3}",
                result.EraName,
                result.NarrativeStyle.Length > 0 ? result.NarrativeStyle.Substring(0, Math.Min(20, result.NarrativeStyle.Length)) + "…" : "(空)",
                result.HistoricalAnchors.Count,
                result.WorldSummary.Length > 0 ? result.WorldSummary.Substring(0, Math.Min(30, result.WorldSummary.Length)) + "…" : "(空)");
            return true;
        }

        /// <summary>生成供后续 LLM 步骤使用的上下文文本（世界设定概述）。</summary>
        public string ToContextText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"【时代】{EraName}");
            if (!string.IsNullOrWhiteSpace(NarrativeStyle))
                sb.AppendLine($"【叙事风格】{NarrativeStyle}");
            if (!string.IsNullOrWhiteSpace(NarrativePerspective))
                sb.AppendLine($"【叙事视角】{NarrativePerspective}");
            if (CognitiveBoundaries.Count > 0)
                sb.AppendLine("【认知边界】" + string.Join("；", CognitiveBoundaries));
            if (!string.IsNullOrWhiteSpace(WorldUndertones))
                sb.AppendLine($"【世界底色】{WorldUndertones}");
            if (HistoricalAnchors.Count > 0)
                sb.AppendLine("【历史锚点】" + string.Join("、", HistoricalAnchors));
            if (!string.IsNullOrWhiteSpace(WorldSummary))
                sb.AppendLine($"【世界摘要】{WorldSummary}");
            return sb.ToString().Trim();
        }
    }
}
