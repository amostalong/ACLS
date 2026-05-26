using System.Collections.Generic;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Abstract base for every dialogue mode.
    // Each state knows:
    //   - How to assemble its prompt
    //   - How to parse the LLM's JSON response
    //   - How to apply system-level effects
    //   - Whether a state transition is warranted
    public abstract class DialogueState
    {
        protected readonly LlmDialogueOrchestrator Orchestrator;

        public DialogueStateType StateType { get; }

        protected DialogueState(LlmDialogueOrchestrator orchestrator, DialogueStateType stateType)
        {
            Orchestrator = orchestrator;
            StateType = stateType;
        }

        // Called when entering this state.
        public virtual void Enter() { }

        // Called when leaving this state.
        public virtual void Exit() { }

        // Assemble the full prompt to send to the LLM.
        public abstract string AssemblePrompt(string userInput = null);

        // Parse raw LLM text into a structured DialogueResult.
        public abstract DialogueResult ParseResponse(string rawResponse);

        // Apply system effects extracted from the result.
        public virtual void ApplyEffects(DialogueResult result)
        {
            if (Orchestrator?.EffectRouter != null)
                Orchestrator.EffectRouter.Apply(result);
        }

        // Decide the next dialogue state based on the result.
        // Return null to stay in the current state.
        public virtual DialogueStateType? GetNextState(DialogueResult result)
        {
            if (string.IsNullOrWhiteSpace(result?.SuggestedNextState)) return null;
            return EffectRouter.ParseSuggestedState(result.SuggestedNextState);
        }
    }
}
