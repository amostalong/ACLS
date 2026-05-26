using System;
using UnityEngine;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Top-level game states.
    public enum GameState
    {
        Boot,              // Initialising — not yet interactive
        WorldSelection,    // Player is choosing their world setting
        CharacterCreation, // Player is on the creation modal
        Dialogue,          // Main narrative loop (has sub-states)
        EventModal,        // A modal event is blocking the screen
        GameOver,          // Player line ended
    }

    // Sub-states inside the Dialogue top-level state.
    // The LLM prompt and active skills change depending on which sub-state
    // we are in.
    public enum DialogueSubState
    {
        FreeNarrative,     // Default: options + freeform input
        DeepDialogue,      // Talking to a specific NPC (negotiation, interrogation)
        Combat,            // Battle scene
        Travel,            // On the road / journey
        Planning,          // Long-term strategic planning
    }

    // Lightweight state machine.  No coroutines — state transitions are
    // synchronous; async work (LLM calls) is kicked off and reports back
    // via events.
    public sealed class GameStateMachine
    {
        public GameState CurrentState { get; private set; } = GameState.Boot;
        public DialogueSubState CurrentSubState { get; private set; } = DialogueSubState.FreeNarrative;

        public World World { get; }
        public ChatBridge Chat { get; }
        public PromptSelector Prompts { get; }

        public event Action<GameState, GameState> OnStateChanged;         // (old, new)
        public event Action<DialogueSubState, DialogueSubState> OnSubStateChanged; // (old, new)

        public GameStateMachine(World world, ChatBridge chat, PromptSelector prompts)
        {
            World = world;
            Chat = chat;
            Prompts = prompts;
        }

        // -------- top-level state transitions --------

        public void TransitionTo(GameState next)
        {
            if (CurrentState == next) return;
            var prev = CurrentState;
            CurrentState = next;
            OnStateChanged?.Invoke(prev, next);
        }

        // -------- sub-state transitions (only valid while in Dialogue) --------

        public void TransitionSubState(DialogueSubState next)
        {
            if (CurrentSubState == next) return;
            var prev = CurrentSubState;
            CurrentSubState = next;
            OnSubStateChanged?.Invoke(prev, next);
        }

        // -------- helpers --------

        public bool IsInDialogue => CurrentState == GameState.Dialogue;

        public string ResolveSystemPrompt()
        {
            if (Prompts == null) return "";

            // Base prompt is always the narrative system prompt.
            var builder = Prompts.GetBasePrompt();

            // Append state-specific prompt fragments.
            switch (CurrentState)
            {
                case GameState.CharacterCreation:
                    builder.Append(Prompts.GetFragment(PromptFragment.CharacterCreation));
                    break;
                case GameState.Dialogue:
                    builder.Append(Prompts.GetFragment(FragmentFor(CurrentSubState)));
                    break;
            }

            return builder.ToString();
        }

        private static PromptFragment FragmentFor(DialogueSubState sub) => sub switch
        {
            DialogueSubState.FreeNarrative => PromptFragment.FreeNarrative,
            DialogueSubState.DeepDialogue => PromptFragment.DeepDialogue,
            DialogueSubState.Combat => PromptFragment.Combat,
            DialogueSubState.Travel => PromptFragment.Travel,
            DialogueSubState.Planning => PromptFragment.Planning,
            _ => PromptFragment.FreeNarrative,
        };
    }
}
