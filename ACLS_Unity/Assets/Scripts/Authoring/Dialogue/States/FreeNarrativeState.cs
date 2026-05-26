using ACLS.Sim;

namespace ACLS.Authoring
{
    // State: the default narrative loop.
    // Player clicks a choice or types freeform → LLM generates next scene.
    public sealed class FreeNarrativeState : DialogueState
    {
        public FreeNarrativeState(LlmDialogueOrchestrator orchestrator)
            : base(orchestrator, DialogueStateType.StagePlay)
        {
        }

        public override string AssemblePrompt(string userInput = null)
        {
            return Orchestrator?.PromptAssembler?.Assemble(StateType, userInput) ?? "";
        }

        public override DialogueResult ParseResponse(string rawResponse)
        {
            return OpeningSceneState.ParseStandardReply(rawResponse);
        }

        // State transitions are driven by the LLM's _system.suggested_state field.
        public override DialogueStateType? GetNextState(DialogueResult result)
        {
            if (string.IsNullOrWhiteSpace(result?.SuggestedNextState)) return null;
            return EffectRouter.ParseSuggestedState(result.SuggestedNextState);
        }
    }
}
