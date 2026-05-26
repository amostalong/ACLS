using ACLS.Llm;

namespace ACLS.Authoring
{
    // One-shot state: builds L4_World and L3_Expanse from the player's world description.
    // Fires once at new-game startup before character creation.
    public sealed class WorldBuildState : DialogueState
    {
        private readonly string worldDescription;

        public WorldBuildState(LlmDialogueOrchestrator orchestrator, string worldDescription)
            : base(orchestrator, DialogueStateType.WorldBuild)
        {
            this.worldDescription = worldDescription;
        }

        public override string AssemblePrompt(string userInput = null)
        {
            return Orchestrator?.PromptAssembler?.AssembleWorldBuild(worldDescription) ?? "";
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
            }

            result.Narration = wb.Summary ?? wb.L4Text ?? "";
            return result;
        }

        public override DialogueStateType? GetNextState(DialogueResult result) => null;
    }
}
