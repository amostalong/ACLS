using ACLS.Llm;
using ACLS.Sim;
using ACLS.Data;
using ACLS.Logging;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ACLS.Authoring
{
    public sealed class WorldBuildState : DialogueState
    {
        private readonly string roleDescription;
        private readonly string worldDescription;

        public WorldBuildState(LlmDialogueOrchestrator orchestrator, string roleDescription, string worldDescription)
            : base(orchestrator, DialogueStateType.WorldBuild)
        {
            this.roleDescription = roleDescription;
            this.worldDescription = worldDescription;
        }

        public override string AssemblePrompt(string userInput = null)
        {
            return Orchestrator?.PromptAssembler?.AssembleWorldBuild(roleDescription, worldDescription) ?? "";
        }

        public override DialogueResult ParseResponse(string rawResponse)
        {
            var result = new DialogueResult { RawResponse = rawResponse };

            if (!WorldBuildReply.TryParse(rawResponse, out var wb, out var err))
            {
                result.IsError = true;
                result.ErrorMessage = "世界构建解析失败：" + err;
                return result;
            }

            var world = Orchestrator?.World;
            if (world != null)
            {
                world.Stage.L4World   = wb.L4Text   ?? "";
                world.Stage.L3Expanse = wb.L3Text   ?? "";
                // L2 由世界构建阶段写入，后续流水线 Step 6 可增量更新
                world.Stage.L2Arena   = wb.L2Text   ?? "";

                if (world.Player == null && wb.Player != null)
                {
                    var sex = wb.Player.Sex == "女" ? Sex.Female : Sex.Male;
                    int traitId = wb.Player.Trait switch
                    {
                        "谨慎" => WorldFactory.TRAIT_CAUTIOUS,
                        "果决" => WorldFactory.TRAIT_DECISIVE,
                        "好学" => WorldFactory.TRAIT_STUDIOUS,
                        _ => 0,
                    };
                    int age = wb.Player.Age > 0 ? wb.Player.Age : 22;
                    string locationName = string.IsNullOrWhiteSpace(wb.Player.LocationName) ? "颍川" : wb.Player.LocationName;
                    WorldFactory.ConfigurePlayer(
                        world,
                        name: string.IsNullOrWhiteSpace(wb.Player.Name) ? "无名" : wb.Player.Name,
                        courtesy: wb.Player.Courtesy ?? "",
                        sex: sex,
                        age: age,
                        locationName: locationName,
                        traitId: traitId);
                }
                else if (world.Player != null && wb.Player != null &&
                         string.IsNullOrWhiteSpace(world.Player.BackgroundStory) &&
                         !string.IsNullOrWhiteSpace(wb.Player.Blurb))
                {
                    world.Player.BackgroundStory = wb.Player.Blurb;
                }
            }

            // 注册 L4/L3/L2 实体到 GameMemory
            RegisterAllEntities(wb);

            // Debug：将 L2 完整结构写入项目根目录，便于查看
            DumpL2Debug(wb, rawResponse);

            result.Thinking = wb.Thinking ?? "";
            result.Narration = wb.Summary ?? wb.L4Text ?? "";
            return result;
        }

        public override DialogueStateType? GetNextState(DialogueResult result) => null;

        /// <summary>注册 L4/L3/L2 实体，供 lookup_* 工具查询。</summary>
        private static void RegisterAllEntities(WorldBuildReply wb)
        {
            if (wb == null) return;

            // ---- L4 宏观势力 ----
            foreach (var f in wb.L4Factions)
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
            foreach (var p in wb.L3Powers)
            {
                if (string.IsNullOrWhiteSpace(p.name)) continue;
                GameMemory.Instance.AddFaction(new FactionEntry
                {
                    name = p.name.Trim(),
                    stance = (p.stance ?? "").Trim(),
                    type = "regional",
                });
            }

            // ---- L2  chars（人脉/关系人） ----
            foreach (var c in wb.Chars)
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

            // ---- L2  factions（可见势力/组织/家族） ----
            foreach (var f in wb.Factions)
            {
                if (string.IsNullOrWhiteSpace(f.name)) continue;
                GameMemory.Instance.AddFaction(new FactionEntry
                {
                    name = f.name.Trim(),
                    stance = (f.stance ?? "").Trim(),
                    type = (f.type ?? "").Trim(),
                });
            }

            // ---- L2  places（关键地点） ----
            foreach (var p in wb.Places)
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

        /// <summary>将 L2 完整结构写入项目根目录 Logs/，供开发时直接查看。</summary>
        private static void DumpL2Debug(WorldBuildReply wb, string rawResponse)
        {
            try
            {
                string projectRoot = Application.dataPath + "/../";
                string dir = Path.Combine(projectRoot, "Logs");
                Directory.CreateDirectory(dir);

                var dump = new JObject
                {
                    ["_raw_llm_response_length"] = rawResponse?.Length ?? 0,
                    ["chars"] = JArray.FromObject(wb.Chars.Select(c => new JObject
                    {
                        ["name"] = c.name,
                        ["role"] = c.role,
                        ["location"] = c.location,
                        ["relation"] = c.relation,
                        ["reachable_in_days"] = c.reachable_in_days,
                    })),
                    ["factions"] = JArray.FromObject(wb.Factions.Select(f => new JObject
                    {
                        ["name"] = f.name,
                        ["type"] = f.type,
                        ["stance"] = f.stance,
                    })),
                    ["places"] = JArray.FromObject(wb.Places.Select(p => new JObject
                    {
                        ["name"] = p.name,
                        ["type"] = p.type,
                        ["description"] = p.description,
                    })),
                    ["active_events"] = JArray.FromObject(wb.ActiveEvents.Select(e => new JObject
                    {
                        ["title"] = e.title,
                        ["urgency"] = e.urgency,
                        ["deadline"] = e.deadline,
                        ["detail"] = e.detail,
                    })),
                    ["opportunities"] = JArray.FromObject(wb.Opportunities),
                    ["_formatted_l2_text"] = wb.L2Text ?? "",
                };

                string path = Path.Combine(dir, "worldbuild_l2.json");
                File.WriteAllText(path, dump.ToString(Newtonsoft.Json.Formatting.Indented));
                Log.Info(Log.Channels.WorldBuild, "L2 dump → {0}", path);
            }
            catch (System.Exception ex)
            {
                Log.Warn(Log.Channels.WorldBuild, "L2 dump 写入失败: {0}", ex.Message);
            }
        }
    }
}
