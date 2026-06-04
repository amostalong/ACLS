using System;
using System.Collections.Generic;
using System.Text;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Sim;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ACLS.Logging;

namespace ACLS.Authoring
{
    /// <summary>
    /// L1 场景构建状态。使用工具驱动流程：
    /// LLM 通过 read_world_layer / read_player_state / read_memory / lookup_* 等工具
    /// 自行读取 L4/L3/玩家状态/记忆等数据，然后返回 L1 场景 JSON。
    ///
    /// 输出 JSON 格式：
    /// {
    ///   "thinking": "...",
    ///   "l1_stage": { ... 同 WorldBuild L1 结构 ... },
    ///   "l2_arena": { ... 同 WorldBuild L2 结构 ... },
    ///   "memory_entries": [ {"date":"...", "event":"..."} ],
    ///   "dramatis_personae": [ {"name":"...", "role":"...", "location":"...", "relation_value": 0} ]
    /// }
    /// </summary>
    public sealed class L1BuilderState : DialogueState
    {
        public L1BuilderState(LlmDialogueOrchestrator orchestrator)
            : base(orchestrator, DialogueStateType.L1Builder)
        {
        }

        public override string AssemblePrompt(string userInput = null)
        {
            var sb = new StringBuilder();

            // 基础系统提示（不含世界数据——LLM 用工具自己读）
            sb.Append("你是一位 L1 场景构建器（l1-builder）。当前游戏的第一步已生成世界背景（L4/L3）和玩家角色。");
            sb.Append("\n\n你的任务：使用下方列出的工具，自主读取所需数据，然后构建当前场景。");

            // 工具列表说明
            sb.Append("\n\n可用工具：");
            sb.Append("\n- read_world_layer(layer): 读取 L4(宏观) / L3(区域) / L2(近域) 背景文本");
            sb.Append("\n- read_player_state(field): 读取玩家状态，field=name/location/date/goal/stats/all");
            sb.Append("\n- read_memory(count): 读取最近 N 条叙事记忆");
            sb.Append("\n- lookup_character(name): 查询某个人物的详细档案");
            sb.Append("\n- lookup_faction(name): 查询某个势力的详细档案");
            sb.Append("\n- lookup_location(name): 查询某个地点的详细档案");
            sb.Append("\n- calculate_travel(from, to, mode): 计算两地距离和行军时间");
            sb.Append("\n- write_memory(date, event): 记录一条记忆（最后调用，保存关键事件）");

            sb.Append("\n\n推荐工作流：");
            sb.Append("\n1. read_world_layer(\"L4\") — 先了解宏观背景");
            sb.Append("\n2. read_world_layer(\"L3\") — 再了解区域背景");
            sb.Append("\n3. read_player_state(\"all\") — 了解玩家状态");
            sb.Append("\n4. read_memory(10) — 了解近期事件（如有）");
            sb.Append("\n5. 需要时 lookup_character/faction/location — 补查具体实体");
            sb.Append("\n6. 如果已有 L2 数据，read_world_layer(\"L2\") 了解近域网络");
            sb.Append("\n7. 构建并输出 L1 场景 JSON");

            sb.Append("\n\n输出要求：请返回一个 JSON 对象，包含以下顶层字段（尽量先输出 thinking 字段）：");
            sb.Append("\n- thinking: 你的构建推理过程");
            sb.Append("\n- l1_stage: 当前场景，结构如下：");
            sb.Append("\n    location: 当前地点全称");
            sb.Append("\n    scene_description: 3-4 句场景描写，感官先行");
            sb.Append("\n    active_npcs: [{name, role, relation_value(-50~50), stance}] 在场NPC");
            sb.Append("\n    immediate_situation: 主角当前即时处境");
            sb.Append("\n    exits: [地点+时间描述]");
            sb.Append("\n- l2_arena: 近域层，结构如下：");
            sb.Append("\n    near_contacts: [{name, role, location, days_away}]");
            sb.Append("\n    active_pressures: [压力事件]");
            sb.Append("\n    opportunities: [机遇]");
            sb.Append("\n- memory_entries: [要记录的事件，每条含 date 和 event]（可选）");
            sb.Append("\n- dramatis_personae: [全剧重要人物清单，每条含 name, role, location, relation_value]（可选）");

            sb.Append("\n\n重要规则：");
            sb.Append("\n- 必须使用工具读取数据，不可凭空编造。");
            sb.Append("\n- l1_stage 的 location 必须与玩家当前位置一致（用 read_player_state 获取）。");
            sb.Append("\n- active_npcs 中的 NPC 必须在工具查询中确认存在。");
            sb.Append("\n- 所有内容符合东汉末年时代背景。");
            sb.Append("\n- 不要 JSON 之外的文字（含 ``` 围栏）。");

            return sb.ToString();
        }

        public override DialogueResult ParseResponse(string rawResponse)
        {
            var result = new DialogueResult { RawResponse = rawResponse };

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                result.IsError = true;
                result.ErrorMessage = "LLM 返回为空";
                return result;
            }

            // 清理围栏
            string text = rawResponse.Trim();
            if (text.StartsWith("```"))
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(nl + 1);
                int fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) text = text.Substring(0, fence);
                text = text.Trim();
            }

            // 提取 JSON
            int open = text.IndexOf('{');
            int close = text.LastIndexOf('}');
            if (open < 0 || close <= open)
            {
                result.IsError = true;
                result.ErrorMessage = "未找到 JSON 对象";
                return result;
            }

            JObject obj;
            try { obj = JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (JsonException ex)
            {
                result.IsError = true;
                result.ErrorMessage = "JSON 解析失败：" + ex.Message;
                return result;
            }

            var world = Orchestrator?.World;

            // ---- 提取 thinking ----
            result.Thinking = ((string)obj["thinking"] ?? "").Trim();

            // ---- 解析 L1 Stage ----
            var l1 = obj["l1_stage"] as JObject;
            if (l1 != null && world != null)
            {
                var sb1 = new StringBuilder();
                string loc = ((string)l1["location"] ?? "").Trim();
                string scene = ((string)l1["scene_description"] ?? "").Trim();
                string situation = ((string)l1["immediate_situation"] ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(loc)) sb1.AppendLine($"[所在] {loc}");
                if (!string.IsNullOrWhiteSpace(scene)) sb1.AppendLine(scene);

                // NPCs
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

                        // 注册到 GameDataLoader
                        GameDataLoader.AddNpc(new NpcEntry
                        {
                            Name = name,
                            Role = role,
                            RelationValue = rel,
                            Stance = stance,
                            Location = loc,
                            Source = "l1_builder",
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(situation)) sb1.AppendLine(situation);

                // Exits
                if (l1["exits"] is JArray exits)
                {
                    sb1.Append("[出口] ");
                    foreach (var e in exits)
                    {
                        string ex = ((string)e ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(ex))
                        {
                            sb1.Append(ex + "  ");
                            // 注册为地点
                            GameDataLoader.AddLocation(new LocationEntry
                            {
                                Name = ex,
                                Type = "exit",
                                Source = "l1_builder",
                            });
                        }
                    }
                    sb1.AppendLine();
                }

                world.Stage.L1Stage = sb1.ToString().Trim();
                result.Narration = scene;
            }

            // ---- 解析 L2 Arena ----
            var l2 = obj["l2_arena"] as JObject;
            if (l2 != null && world != null)
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
                            GameDataLoader.AddNpc(new NpcEntry
                            {
                                Name = name,
                                Role = cRole,
                                Location = cloc,
                                DaysAway = days,
                                Source = "l1_builder",
                            });
                        }
                    }
                }

                if (l2["active_pressures"] is JArray pressures)
                {
                    foreach (var p in pressures)
                    {
                        string pt = ((string)p ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(pt)) sb2.AppendLine($"⚠ {pt}");
                    }
                }

                if (l2["opportunities"] is JArray opps)
                {
                    foreach (var o in opps)
                    {
                        string ot = ((string)o ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(ot)) sb2.AppendLine($"◇ {ot}");
                    }
                }

                world.Stage.L2Arena = sb2.ToString().Trim();
            }

            // ---- 写入记忆 ----
            if (obj["memory_entries"] is JArray memEntries && world != null)
            {
                foreach (var m in memEntries)
                {
                    string date = ((string)m["date"] ?? "").Trim();
                    string evt = ((string)m["event"] ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(date) && !string.IsNullOrWhiteSpace(evt))
                        GameMemory.Append(world, date, evt);
                }
            }

            // ---- dramatis_personae 注册为 NPC 实体 ----
            if (obj["dramatis_personae"] is JArray dpEntries)
            {
                foreach (var d in dpEntries)
                {
                    string name = ((string)d["name"] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    GameDataLoader.AddNpc(new NpcEntry
                    {
                        Name = name,
                        Role = ((string)d["role"] ?? "").Trim(),
                        Location = ((string)d["location"] ?? "").Trim(),
                        RelationValue = d["relation_value"]?.Value<int>() ?? 0,
                        Source = "l1_builder",
                    });
                }
            }

            Log.Info(Log.Channels.Stage, "✅ L1Builder 完成 | L1长度={0} L2长度={1}",
                world?.Stage.L1Stage?.Length ?? 0, world?.Stage.L2Arena?.Length ?? 0);

            return result;
        }
    }
}
