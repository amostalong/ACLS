using ACLS.Llm;
using ACLS.Data;

namespace ACLS.Authoring
{
    /// <summary>
    /// 世界实体注册器。
    /// 将 LLM 返回的 WorldBuildReply / StageCreateReply 中的结构化实体数据
    /// 灌入 GameMemory，使 lookup_* 工具能查到 LLM 新生成的角色/势力/地点。
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
            GameMemory.Instance.AddFaction(new FactionEntry
            {
                name = f.name.Trim(),
                stance = (f.status ?? "").Trim(),
                type = "macro",
            });
            }

            // ---- L3 区域势力 ----
            foreach (var p in reply.L3Powers)
            {
                if (string.IsNullOrWhiteSpace(p.name)) continue;
            GameMemory.Instance.AddFaction(new FactionEntry
            {
                name = p.name.Trim(),
                stance = (p.stance ?? "").Trim(),
                type = "regional",
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
            GameMemory.Instance.AddChar(new CharEntry
            {
                name = n.name.Trim(),
                role = (n.role ?? "").Trim(),
                relation = n.relation_value,
                location = stageLocation,
                reachable_in_days = 0,
            });
            }

            // ---- L2 chars（人脉/关系人） ----
            foreach (var c in reply.Chars)
            {
                if (string.IsNullOrWhiteSpace(c.name)) continue;
            GameMemory.Instance.AddChar(new CharEntry
            {
                name = c.name.Trim(),
                role = (c.role ?? "").Trim(),
                location = (c.location ?? "").Trim(),
                relation = c.relation,
                reachable_in_days = c.reachable_in_days,
            });
            }

            // ---- L2 factions（可见势力/组织/家族） ----
            foreach (var f in reply.Factions)
            {
                if (string.IsNullOrWhiteSpace(f.name)) continue;
            GameMemory.Instance.AddFaction(new FactionEntry
            {
                name = f.name.Trim(),
                stance = (f.stance ?? "").Trim(),
                type = (f.type ?? "").Trim(),
            });
            }

            // ---- L2 places（关键地点） ----
            foreach (var p in reply.Places)
            {
                if (string.IsNullOrWhiteSpace(p.name)) continue;
            GameMemory.Instance.AddPlace(new PlaceEntry
            {
                name = p.name.Trim(),
                type = (p.type ?? "").Trim(),
                description = "",
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
            GameMemory.Instance.AddChar(new CharEntry
            {
                name = n.Name.Trim(),
                role = (n.Role ?? "").Trim(),
                relation = n.RelationValue,
                location = "",
                reachable_in_days = 0,
            });
            }
        }
    }
}
