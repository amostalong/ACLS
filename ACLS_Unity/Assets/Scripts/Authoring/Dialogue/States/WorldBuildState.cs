using ACLS.Llm;
using ACLS.Sim;

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
                world.Stage.L2Arena   = wb.L2Text   ?? "";
                world.Stage.L1Stage   = wb.L1Text   ?? "";

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

            result.Thinking = wb.Thinking ?? "";
            result.Narration = wb.Summary ?? wb.L4Text ?? "";
            return result;
        }

        public override DialogueStateType? GetNextState(DialogueResult result) => null;
    }
}
