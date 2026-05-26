using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Thin MonoBehaviour shell around LlmDialogueOrchestrator.
    //
    // Responsibilities:
    //   - Receive UI calls (Choose, SubmitFreeform, ExpandCharacter, StartOpening)
    //   - Manage world-side side effects (ApplyEffects, AdvanceTime) on choice
    //   - Surface events to the UI layer (OnMessage, OnChoicesChanged, etc.)
    //   - Delegate all LLM orchestration (prompt assembly, calling, parsing,
    //     state transitions) to LlmDialogueOrchestrator.
    public sealed class ChatBridge : MonoBehaviour
    {
        public LlmPromptConfig PromptConfig;
        public GameStateMachine StateMachine;

        public int RecentMessages = 20;

        // History is delegated to the orchestrator so prompt assembly and
        // the chat log stay in sync.
        public ChatHistory History => orchestrator?.History;

        public bool Ready => world != null && llm != null && orchestrator != null;
        public bool Busy => orchestrator?.Busy ?? false;
        public string ConfigError { get; private set; }

        public IReadOnlyList<LlmReply.Choice> CurrentChoices => currentChoices;
        public IReadOnlyList<LlmReply.Participant> CurrentParticipants => currentParticipants;

        public LlmUsage LastUsage { get; private set; }
        public LlmUsage CumulativeUsage { get; private set; }
        public int CallCount => orchestrator?.CallCount ?? 0;

        public event Action<ChatMessage> OnMessage;
        public event Action<bool> OnBusyChanged;
        public event Action<IReadOnlyList<LlmReply.Choice>> OnChoicesChanged;
        public event Action<IReadOnlyList<LlmReply.Participant>> OnParticipantsChanged;
        public event Action<LlmUsage, LlmUsage> OnUsageReported;   // (lastCall, cumulative)

        private World world;
        private ILlmClient llm;
        private LlmDialogueOrchestrator orchestrator;
        private CancellationTokenSource cts;

        private List<LlmReply.Choice> currentChoices = new List<LlmReply.Choice>();
        private List<LlmReply.Participant> currentParticipants = new List<LlmReply.Participant>();

        public void Bind(World world, ILlmClient llm, LlmPromptConfig promptConfig, string configErrorIfAny)
        {
            this.world = world;
            this.llm = llm;
            this.PromptConfig = promptConfig;
            this.ConfigError = configErrorIfAny;
            this.cts = new CancellationTokenSource();

            if (world != null && llm != null && promptConfig != null)
            {
                orchestrator = new LlmDialogueOrchestrator(world, llm, promptConfig);
                orchestrator.RecentMessages = RecentMessages;
                WireOrchestratorEvents();
            }
        }

        private void WireOrchestratorEvents()
        {
            if (orchestrator == null) return;

            orchestrator.OnNarration += narration =>
            {
                var msg = new ChatMessage(ChatRole.Assistant, narration);
                OnMessage?.Invoke(msg);
            };

            orchestrator.OnChoices += choices =>
            {
                currentChoices = new List<LlmReply.Choice>(choices ?? new List<LlmReply.Choice>());
                OnChoicesChanged?.Invoke(currentChoices);
            };

            orchestrator.OnParticipants += participants =>
            {
                currentParticipants = new List<LlmReply.Participant>(participants ?? new List<LlmReply.Participant>());
                OnParticipantsChanged?.Invoke(currentParticipants);
            };

            orchestrator.OnUsage += (last, cumulative) =>
            {
                LastUsage = last;
                CumulativeUsage = cumulative;
                OnUsageReported?.Invoke(last, cumulative);
            };

            orchestrator.OnBusyChanged += busy => OnBusyChanged?.Invoke(busy);

            orchestrator.OnError += error =>
            {
                var msg = new ChatMessage(ChatRole.System, "[错误] " + error);
                OnMessage?.Invoke(msg);
            };

            // Keep GameStateMachine sub-state in sync with the dialogue state machine.
            orchestrator.OnStateChanged += (prev, next) =>
            {
                var sub = MapToDialogueSubState(next);
                if (sub.HasValue)
                    StateMachine?.TransitionSubState(sub.Value);
            };
        }

        public void CancelCurrent()
        {
            orchestrator?.CancelCurrent();
        }

        private void OnDestroy()
        {
            try { cts?.Cancel(); } catch { }
            cts?.Dispose();
            cts = null;
            orchestrator?.Dispose();
            orchestrator = null;
        }

        // -------- public entry points --------

        // Player picked one of the LLM-supplied choices.
        public void Choose(int index)
        {
            if (!Ready || Busy) return;
            if (currentChoices == null || index < 0 || index >= currentChoices.Count) return;

            var choice = currentChoices[index];

            // 1. Show outcome narration instantly (player-visible, not in History).
            OnMessage?.Invoke(new ChatMessage(ChatRole.Assistant, choice.OutcomeNarration));

            // 2. Apply effects and advance time (world-side mutation).
            ApplyEffects(choice);
            AdvanceTime(choice.DaysPassed);

            // 3. Next LLM turn. The outcome is embedded so the LLM can
            //    continue the narrative while preserving user/assistant alternation.
            string action = $"[玩家选择：{choice.Label}]\n[上一幕后果]：{choice.OutcomeNarration}\n请承接上文，描写下一幕，并给出 3-4 个新选项。";
            orchestrator?.SendAction(action, displayText: action);
        }

        // Called once before character creation to let the LLM build L4+L3 world context.
        public void StartWorldBuild(string worldDescription, Action<bool> onComplete)
        {
            if (!Ready) { onComplete?.Invoke(false); return; }
            orchestrator?.StartWorldBuild(worldDescription ?? "", onComplete);
        }

        // Called once after character expansion to generate L1_Stage + L2_Arena.
        public void StartStageCreate(CharacterPresets.Preset preset, Action<bool> onComplete)
        {
            if (!Ready || preset == null) { onComplete?.Invoke(false); return; }
            orchestrator?.StartStageCreate(preset, success =>
            {
                if (success) orchestrator?.TransitionTo(DialogueStateType.StagePlay);
                onComplete?.Invoke(success);
            });
        }

        // Called once after character creation to let the LLM flesh out
        // family, social circle, secrets and assets.
        public void ExpandCharacter(CharacterPresets.Preset preset, Action<bool> onComplete)
        {
            if (!Ready || preset == null || world?.Player == null)
            {
                onComplete?.Invoke(false);
                return;
            }
            if (PromptConfig == null || string.IsNullOrWhiteSpace(PromptConfig.CharacterExpansionPrompt))
            {
                onComplete?.Invoke(false);
                return;
            }

            // TransitionTo(StagePlay) is now owned by StartStageCreate (called after this).
            orchestrator?.ExpandCharacter(preset, world.Player, onComplete);
        }

        // Kicks off the first LLM call right after stage creation.
        public void StartOpening(CharacterPresets.Preset preset)
        {
            if (!Ready || Busy || preset == null) return;
            orchestrator?.StartOpening(preset);
        }

        // Player typed into the input field.
        public void SubmitFreeform(string text)
        {
            if (!Ready || Busy) return;
            if (world?.Player == null) return;
            text = (text ?? "").Trim();
            if (text.Length == 0) return;

            // Show human-readable text in the chat panel.
            OnMessage?.Invoke(new ChatMessage(ChatRole.User, text));

            string action = $"[玩家自由行动：{text}] 请承接上文，描写下一幕，并给出 3-4 个新选项。";
            orchestrator?.SendAction(action, displayText: action);
        }

        // -------- helpers --------

        private void ApplyEffects(LlmReply.Choice choice)
        {
            if (choice?.Effects == null || choice.Effects.Count == 0) return;
            var ops = EffectParser.ParseAll(choice.Effects, world);
            for (int i = 0; i < ops.Count; i++)
            {
                Effects.ApplyOne(ops[i], world, world.Player);
            }
        }

        private void AdvanceTime(int days)
        {
            if (days <= 0) return;
            for (int i = 0; i < days; i++)
            {
                if (world.EventQueue.Count > 0) break;
                world.Tick();
            }
        }

        private static DialogueSubState? MapToDialogueSubState(DialogueStateType type) =>
            type == DialogueStateType.StagePlay ? DialogueSubState.FreeNarrative : (DialogueSubState?)null;
    }
}
