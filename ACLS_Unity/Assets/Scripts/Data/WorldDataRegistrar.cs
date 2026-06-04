using ACLS.Llm;
using ACLS.Data;

namespace ACLS.Authoring
{
    /// <summary>
    /// 世界实体注册器。
    /// 将 LLM 返回的 WorldBuildReply / StageCreateReply 中的结构化实体数据
    /// 灌入 GameDataLoader，使 lookup_* 工具能查到 LLM 新生成的角色/势力/地点。
    /// </summary>
    public static class WorldDataRegistrar
    {
        /// <summary>注册 WorldBuildReply 中的所有实体。</summary>
        public static void Register(WorldBuildReply reply)
        {
            if (reply == null) return;

            // ---- L4 宏观势力 ----
            foreach (var f in reply.L4Factions)
            {
                if (string.IsNullOrWhiteSpace(f.name)) continue;
                GameDataLoader.AddFaction(new FactionEntry
                {
                    Name = f.name.Trim(),
                    Status = (f.status ?? "").Trim(),
                    Type = "macro",
                    Source = "world_build",
                });
            }

            // ---- L3 区域势力 ----
            foreach (var p in reply.L3Powers)
            {
                if (string.IsNullOrWhiteSpace(p.name)) continue;
                GameDataLoader.AddFaction(new FactionEntry
                {
                    Name = p.name.Trim(),
                    Status = (p.stance ?? "").Trim(),
                    Type = "regional",
                    Source = "world_build",
                });
            }

            // ---- L1 场景 NPC（active_npcs） ----
            string stageLocation = "";
            if (reply.L1Npcs.Count > 0)
            {
                // 从 L1Text 提取第一行 [所在] 作为默认位置（若有）
                var lines = (reply.L1Text ?? "").Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("[所在]"))
                    {
                        stageLocation = line.Substring(4).Trim();
                        break;
                    }
                }
            }

            foreach (var n in reply.L1Npcs)
            {
                if (string.IsNullOrWhiteSpace(n.name)) continue;
                GameDataLoader.AddNpc(new NpcEntry
                {
                    Name = n.name.Trim(),
                    Role = (n.role ?? "").Trim(),
                    RelationValue = n.relation_value,
                    Stance = (n.stance ?? "").Trim(),
                    Location = stageLocation,
                    Source = "world_build",
                });
            }

            // ---- L2 近域人脉（near_contacts） ----
            foreach (var c in reply.L2Contacts)
            {
                if (string.IsNullOrWhiteSpace(c.name)) continue;
                GameDataLoader.AddNpc(new NpcEntry
                {
                    Name = c.name.Trim(),
                    Role = (c.role ?? "").Trim(),
                    Location = (c.location ?? "").Trim(),
                    DaysAway = c.days_away,
                    Source = "world_build",
                });
            }
        }

        /// <summary>注册 StageCreateReply 中的所有实体。</summary>
        public static void Register(StageCreateReply reply)
        {
            if (reply == null) return;

            foreach (var n in reply.ActiveNpcs)
            {
                if (string.IsNullOrWhiteSpace(n.Name)) continue;
                GameDataLoader.AddNpc(new NpcEntry
                {
                    Name = n.Name.Trim(),
                    Role = (n.Role ?? "").Trim(),
                    RelationValue = n.RelationValue,
                    Stance = (n.Stance ?? "").Trim(),
                    Source = "stage_create",
                });
            }
        }
    }
}
