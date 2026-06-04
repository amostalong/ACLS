using ACLS.Llm;
using ACLS.Sim;
using ACLS.Data;

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
                // WorldBuild 只写入 L4（宏观）和 L3（区域），L1/L2 由 L1Builder 后续生成
                world.Stage.L4World   = wb.L4Text   ?? "";
                world.Stage.L3Expanse = wb.L3Text   ?? "";
                // L1Stage 和 L2Arena 暂不写入，由 L1Builder 通过工具读取 L4/L3/玩家状态后生成

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

            // 仅注册 L4/L3 宏观实体，L1/L2 实体由 L1Builder 处理
            RegisterMacroEntities(wb);

            result.Thinking = wb.Thinking ?? "";
            result.Narration = wb.Summary ?? wb.L4Text ?? "";
            return result;
        }

        public override DialogueStateType? GetNextState(DialogueResult result) => null;

        /// <summary>仅注册 L4（宏观势力）和 L3（区域势力）实体，供 lookup_* 工具查询。</summary>
        private static void RegisterMacroEntities(WorldBuildReply wb)
        {
            if (wb == null) return;
            foreach (var f in wb.L4Factions)
            {
                if (string.IsNullOrWhiteSpace(f.name)) continue;
                GameDataLoader.AddFaction(new ACLS.Data.FactionEntry
                {
                    Name = f.name.Trim(),
                    Status = (f.status ?? "").Trim(),
                    Type = "macro",
                    Source = "world_build",
                });
            }
            foreach (var p in wb.L3Powers)
            {
                if (string.IsNullOrWhiteSpace(p.name)) continue;
                GameDataLoader.AddFaction(new ACLS.Data.FactionEntry
                {
                    Name = p.name.Trim(),
                    Status = (p.stance ?? "").Trim(),
                    Type = "regional",
                    Source = "world_build",
                });
            }
        }
    }
}
