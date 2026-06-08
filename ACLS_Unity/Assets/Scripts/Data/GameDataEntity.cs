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

        // ──── 社会关系 ────
        public string father = "";
        public string mother = "";
        public string[] siblings = Array.Empty<string>();
        public string[] other_relatives = Array.Empty<string>();
        public string[] core_friends = Array.Empty<string>();

        // ──── 重要人物标记（需要每次更新时重新生成） ────
        public bool is_important;

        // ──── 角色丰富化（由 Step 5 LLM 完成后填充） ────
        public string background_story = "";
        public string values = "";
        public string current_goal = "";
        public string secret = "";

        public string ToLlmText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {name}");
            sb.AppendLine($"角色：{role}");
            sb.AppendLine($"关系：{relation:+0;-0;0}");
            if (!string.IsNullOrEmpty(location)) sb.AppendLine($"位置：{location}");
            sb.AppendLine($"可达：约{reachable_in_days}天");

            if (!string.IsNullOrEmpty(father) || !string.IsNullOrEmpty(mother))
                sb.AppendLine($"父母：{father}{(string.IsNullOrEmpty(father) ? "" : " ")}{mother}");
            if (siblings.Length > 0)
                sb.AppendLine($"兄弟姐妹：{string.Join("、", siblings)}");
            if (other_relatives.Length > 0)
                sb.AppendLine($"其他亲戚：{string.Join("、", other_relatives)}");
            if (core_friends.Length > 0)
                sb.AppendLine($"核心朋友：{string.Join("、", core_friends)}");
            if (is_important)
                sb.AppendLine("【重要人物】");

            if (!string.IsNullOrEmpty(background_story)) sb.AppendLine($"背景：{background_story}");
            if (!string.IsNullOrEmpty(values)) sb.AppendLine($"价值观：{values}");
            if (!string.IsNullOrEmpty(current_goal)) sb.AppendLine($"当前目标：{current_goal}");
            if (!string.IsNullOrEmpty(secret)) sb.AppendLine($"秘密：{secret}");
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
