using System;

namespace ACLS.Data
{
    // 实体条目基类。
    public abstract class GameDataEntityBase
    {
        public string Id = "";
        public string Name = "";
        public string Source = "";   // "world_build" / "stage_create" / "static"
    }

    // ──── NPC / 人物 ────

    [Serializable]
    public sealed class NpcEntry : GameDataEntityBase
    {
        public string Role = "";            // 身份/职能
        public int RelationValue;           // -100 ~ +100
        public string Stance = "";          // 当前立场
        public string Location = "";        // 所在地点
        public int DaysAway;                // 仅用于 L2 人脉（可达天数）

        public string ToLlmText()
        {
            return $"# {Name} ({Id})\n"
                + $"角色：{Role}\n"
                + $"关系：{RelationValue:+0;-0;0}\n"
                + $"立场：{Stance}\n"
                + (!string.IsNullOrEmpty(Location) ? $"位置：{Location}\n" : "")
                + $"来源：{Source}\n";
        }
    }

    // ──── 势力 ────

    [Serializable]
    public sealed class FactionEntry : GameDataEntityBase
    {
        public string Status = "";          // 一句话态势
        public string Type = "";            // "macro" / "regional"

        public string ToLlmText()
        {
            return $"# {Name} ({Id})\n"
                + $"态势：{Status}\n"
                + $"类型：{Type}\n"
                + $"来源：{Source}\n";
        }
    }

    // ──── 地点 ────

    [Serializable]
    public sealed class LocationEntry : GameDataEntityBase
    {
        public string Region = "";          // 所属郡/州
        public string Type = "";            // "settlement" / "exit" / "region"

        public string ToLlmText()
        {
            return $"# {Name} ({Id})\n"
                + (!string.IsNullOrEmpty(Region) ? $"所属：{Region}\n" : "")
                + $"类型：{Type}\n"
                + $"来源：{Source}\n";
        }
    }
}
