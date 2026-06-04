using ACLS.Llm;

namespace ACLS.Authoring
{
    // One-shot state: builds L1_Stage and L2_Arena for the player's starting position.
    // Fires once after character expansion, before the opening scene.
    public sealed class StageCreateState : DialogueState
    {
        private readonly CharacterPresets.Preset preset;

        public StageCreateState(LlmDialogueOrchestrator orchestrator, CharacterPresets.Preset preset)
            : base(orchestrator, DialogueStateType.StageCreate)
        {
            this.preset = preset;
        }

        public override string AssemblePrompt(string userInput = null)
        {
            return Orchestrator?.PromptAssembler?.AssembleStageCreate(preset) ?? "";
        }

        public override DialogueResult ParseResponse(string rawResponse)
        {
            var result = new DialogueResult { RawResponse = rawResponse };

            if (!StageCreateReply.TryParse(rawResponse, out var stage, out var err))
            {
                result.IsError = true;
                result.ErrorMessage = "舞台生成解析失败：" + err;
                return result;
            }

            var world = Orchestrator?.World;
            if (world != null)
            {
                world.Stage.L1Stage = stage.L1Text ?? "";
                world.Stage.L2Arena = stage.L2Text ?? "";
            }

            // Stage 中的 NPC 灌入 GameDataLoader
            WorldDataRegistrar.Register(stage);

            result.Thinking = stage.Thinking ?? "";
            result.Narration = stage.SceneDescription ?? stage.L1Text ?? "";
            return result;
        }

        public override DialogueStateType? GetNextState(DialogueResult result) => DialogueStateType.StagePlay;
    }
}
