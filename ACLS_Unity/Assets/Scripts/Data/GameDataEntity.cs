using System;
using System.Text;

namespace ACLS.Data
{
    // ──── L2 Char（人物/人脉） ────

    [Serializable]
    public sealed class CharEntry
    {
        public string name = "";
        public string role = "";
        public string location = "";
        public int relation;
        public int reachable_in_days;

        public string ToLlmText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {name}");
            sb.AppendLine($"角色：{role}");
            sb.AppendLine($"关系：{relation:+0;-0;0}");
            if (!string.IsNullOrEmpty(location)) sb.AppendLine($"位置：{location}");
            sb.AppendLine($"可达：约{reachable_in_days}天");
            return sb.ToString();
        }
    }

    // ──── L2 Faction（势力/组织） ────

    [Serializable]
    public sealed class FactionEntry
    {
        public string name = "";
        public string type = "";      // "macro" / "regional" / "local"
        public string stance = "";

        public string ToLlmText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {name}");
            if (!string.IsNullOrEmpty(type)) sb.AppendLine($"类型：{type}");
            sb.AppendLine($"态势：{stance}");
            return sb.ToString();
        }
    }

    // ──── L2 Place（地点） ────

    [Serializable]
    public sealed class PlaceEntry
    {
        public string name = "";
        public string type = "";         // "settlement" / "exit" / "region"
        public string description = "";

        public string ToLlmText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {name}");
            if (!string.IsNullOrEmpty(type)) sb.AppendLine($"类型：{type}");
            if (!string.IsNullOrEmpty(description)) sb.AppendLine($"描述：{description}");
            return sb.ToString();
        }
    }

    // ──── L2 Event（事件） ────

    [Serializable]
    public sealed class EventEntry
    {
        public string title = "";
        public string urgency = "medium";   // "high" / "medium" / "low"
        public string deadline = "ongoing";
        public string detail = "";

        public string ToLlmText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {title}");
            sb.AppendLine($"优先级：{urgency}");
            sb.AppendLine($"期限：{deadline}");
            if (!string.IsNullOrEmpty(detail)) sb.AppendLine($"详情：{detail}");
            return sb.ToString();
        }
    }
}
