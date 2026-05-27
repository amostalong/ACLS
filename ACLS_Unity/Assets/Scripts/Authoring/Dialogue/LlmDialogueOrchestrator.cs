using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Sim;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ACLS.Authoring
{
    // Central orchestrator for all LLM dialogue.
    //
    // Responsibilities:
    //   1. Maintain the current DialogueState
    //   2. Assemble prompts (delegates to PromptAssembler)
    //   3. Call the LLM asynchronously
    //   4. Parse responses (delegates to current DialogueState)
    //   5. Apply system effects (delegates to EffectRouter)
    //   6. Transition states when appropriate
    //   7. Publish user-facing content via events
    //
    // ChatBridge is a thin MonoBehaviour shell that forwards UI calls
    // here and surfaces events to the UI layer.
    public sealed class LlmDialogueOrchestrator
    {
        public World World { get; }
        public ILlmClient LlmClient { get; }
        public PromptAssembler PromptAssembler { get; }
        public EffectRouter EffectRouter { get; }
        public ChatHistory History { get; }

        public DialogueState CurrentState { get; private set; }
        public bool Busy { get; private set; }
        public int CallCount => callCount;
        public int RecentMessages = 20;

        // ---- events (UI-facing) ----
        public event Action<string> OnNarration;               // narration text
        public event Action<IReadOnlyList<LlmReply.Choice>> OnChoices;
        public event Action<IReadOnlyList<LlmReply.Participant>> OnParticipants;
        public event Action<DialogueStateType, DialogueStateType> OnStateChanged;
        public event Action<bool> OnBusyChanged;
        public event Action<string> OnError;                   // human-readable error
        public event Action<LlmUsage, LlmUsage> OnUsage;       // (last, cumulative)
        public event Action<string> OnThinkingChanged;

        // ---- internal ----
        private CancellationTokenSource lifetimeCts;
        private CancellationTokenSource currentCts;
        private LlmUsage cumulativeUsage;
        private int callCount;
        private string currentThinking = "";

        public LlmDialogueOrchestrator(World world, ILlmClient llm,
            LlmPromptConfig promptConfig)
        {
            World = world;
            LlmClient = llm;
            PromptAssembler = new PromptAssembler(promptConfig, world);
            EffectRouter = new EffectRouter(world);
            History = new ChatHistory();
            lifetimeCts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            try { lifetimeCts?.Cancel(); } catch { }
            lifetimeCts?.Dispose();
            lifetimeCts = null;
            try { currentCts?.Cancel(); } catch { }
            currentCts?.Dispose();
            currentCts = null;
        }

        public void CancelCurrent()
        {
            try { currentCts?.Cancel(); } catch { }
        }

        // ---- state transitions ----

        public void TransitionTo(DialogueStateType type)
        {
            var prev = CurrentState?.StateType ?? (DialogueStateType)(-1);
            CurrentState?.Exit();

            CurrentState = type switch
            {
                DialogueStateType.StagePlay => new FreeNarrativeState(this),
                // WorldBuild and StageCreate are one-shot; not entered via TransitionTo.
                _ => CurrentState,
            };

            CurrentState?.Enter();
            if (CurrentState != null && (int)prev != -1)
                OnStateChanged?.Invoke(prev, CurrentState.StateType);
        }

        public void TransitionTo(DialogueState state)
        {
            var prev = CurrentState?.StateType ?? (DialogueStateType)(-1);
            CurrentState?.Exit();
            CurrentState = state;
            CurrentState?.Enter();
            if (CurrentState != null && (int)prev != -1)
                OnStateChanged?.Invoke(prev, CurrentState.StateType);
        }

        // ---- public entry points (called by ChatBridge / UI) ----

        // 1. Character expansion (one-shot, no history).
        public async void ExpandCharacter(CharacterPresets.Preset preset,
            Character player, Action<bool> onComplete)
        {
            if (Busy || LlmClient == null || player == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            var state = new CharacterExpansionState(this, preset, player);
            string prompt = state.AssemblePrompt();

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                var resp = await CompleteStreamWithThinking(prompt,
                    new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") }, thisCts.Token);
                TrackUsage(resp.Usage);

                var result = state.ParseResponse(resp.Content);
                if (result.IsError)
                {
                    OnError?.Invoke(result.ErrorMessage);
                    onComplete?.Invoke(false);
                    return;
                }

                SetThinking(result.Thinking ?? "");
                state.ApplyEffects(result);
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                    OnError?.Invoke("已中断角色拓展。");
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                OnError?.Invoke("角色拓展调用失败：" + ex.Message);
                onComplete?.Invoke(false);
            }
            finally
            {
                if (currentCts == thisCts) currentCts = null;
                thisCts.Dispose();
                SetBusy(false);
            }
        }

        // 2. Opening scene (first narrative turn after expansion).
        public async void StartOpening(CharacterPresets.Preset preset)
        {
            if (Busy || LlmClient == null) return;

            var state = new OpeningSceneState(this, preset);
            string prompt = state.AssemblePrompt();

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                var resp = await CompleteStreamWithThinking(prompt,
                    new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") }, thisCts.Token);
                TrackUsage(resp.Usage);

                var result = state.ParseResponse(resp.Content);
                HandleResult(state, result);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                    OnError?.Invoke("已中断开场请求。");
            }
            catch (Exception ex)
            {
                OnError?.Invoke("开场调用失败：" + ex.Message);
            }
            finally
            {
                if (currentCts == thisCts) currentCts = null;
                thisCts.Dispose();
                SetBusy(false);
            }
        }

        public async void StartWorldBuild(string roleDescription, string worldDescription, Action<bool> onComplete)
        {
            if (Busy || LlmClient == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            var state = new WorldBuildState(this, roleDescription ?? "", worldDescription ?? "");
            string prompt = state.AssemblePrompt();

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                var resp = await CompleteStreamWithThinking(prompt,
                    new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") }, thisCts.Token);
                TrackUsage(resp.Usage);

                var result = state.ParseResponse(resp.Content);
                if (result.IsError)
                {
                    OnError?.Invoke(result.ErrorMessage);
                    onComplete?.Invoke(false);
                    return;
                }

                SetThinking(result.Thinking ?? "");
                state.ApplyEffects(result);
                if (!string.IsNullOrWhiteSpace(result.Narration))
                {
                    History.Add(new ChatMessage(ChatRole.Assistant, result.Narration));
                    OnNarration?.Invoke(result.Narration);
                }
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                    OnError?.Invoke("已中断世界构建。");
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                OnError?.Invoke("世界构建调用失败：" + ex.Message);
                onComplete?.Invoke(false);
            }
            finally
            {
                if (currentCts == thisCts) currentCts = null;
                thisCts.Dispose();
                SetBusy(false);
            }
        }

        // 4. Stage create (one-shot, no history — called after character expansion).
        public async void StartStageCreate(CharacterPresets.Preset preset, Action<bool> onComplete)
        {
            if (Busy || LlmClient == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            var state = new StageCreateState(this, preset);
            string prompt = state.AssemblePrompt();

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                var resp = await CompleteStreamWithThinking(prompt,
                    new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") }, thisCts.Token);
                TrackUsage(resp.Usage);

                var result = state.ParseResponse(resp.Content);
                if (result.IsError)
                {
                    OnError?.Invoke(result.ErrorMessage);
                    onComplete?.Invoke(false);
                    return;
                }

                SetThinking(result.Thinking ?? "");
                state.ApplyEffects(result);
                if (!string.IsNullOrWhiteSpace(result.Narration))
                {
                    History.Add(new ChatMessage(ChatRole.Assistant, result.Narration));
                    OnNarration?.Invoke(result.Narration);
                }
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                    OnError?.Invoke("已中断舞台生成。");
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                OnError?.Invoke("舞台生成调用失败：" + ex.Message);
                onComplete?.Invoke(false);
            }
            finally
            {
                if (currentCts == thisCts) currentCts = null;
                thisCts.Dispose();
                SetBusy(false);
            }
        }

        // 5. Regular narrative turn (choice selected or freeform input).
        public async void SendAction(string userInput, string displayText = null,
            bool addToHistory = true)
        {
            if (Busy || LlmClient == null || CurrentState == null) return;

            if (addToHistory && !string.IsNullOrWhiteSpace(displayText))
                History.Add(new ChatMessage(ChatRole.User, displayText));

            string prompt = CurrentState.AssemblePrompt(userInput);

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                var resp = await CompleteStreamWithThinking(prompt, History.Recent(RecentMessages), thisCts.Token);
                TrackUsage(resp.Usage);

                var result = CurrentState.ParseResponse(resp.Content);
                HandleResult(CurrentState, result);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                    OnError?.Invoke("已中断当前请求。");
            }
            catch (Exception ex)
            {
                OnError?.Invoke("调用 LLM 失败：" + ex.Message);
            }
            finally
            {
                if (currentCts == thisCts) currentCts = null;
                thisCts.Dispose();
                SetBusy(false);
            }
        }

        // ---- internal ----

        private void SetThinking(string thinking)
        {
            currentThinking = thinking ?? "";
            if (PlayerLoopHelper.IsMainThread)
                OnThinkingChanged?.Invoke(currentThinking);
            else
                UniTask.Post(() => OnThinkingChanged?.Invoke(currentThinking));
        }

        private static IReadOnlyList<ChatMessage> EnsureMessages(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") };
            return messages;
        }

        private async Task<LlmResponse> CompleteStreamWithThinking(string prompt,
            IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            SetThinking("");

            var raw = new StringBuilder();
            string last = "";
            int lastEmit = Environment.TickCount;

            messages = EnsureMessages(messages);

            var resp = await LlmClient.CompleteStreamAsync(prompt, messages, delta =>
            {
                if (string.IsNullOrEmpty(delta)) return;
                raw.Append(delta);
                if (TryExtractThinking(raw, out var t) && t != last)
                {
                    last = t;
                    int now = Environment.TickCount;
                    if (now - lastEmit >= 50)
                    {
                        lastEmit = now;
                        SetThinking(t);
                    }
                }
            }, ct);

            return resp;
        }

        private static bool TryExtractThinking(StringBuilder raw, out string thinking)
        {
            thinking = "";
            if (raw == null || raw.Length == 0) return false;

            string s = raw.ToString();
            int key = s.IndexOf("\"thinking\"", StringComparison.Ordinal);
            if (key < 0) return false;

            int i = key + "\"thinking\"".Length;
            while (i < s.Length && s[i] != ':') i++;
            if (i >= s.Length) return false;
            i++;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] != '"') return false;
            i++;

            var sb = new StringBuilder();
            bool esc = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (esc)
                {
                    sb.Append(c switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => c,
                    });
                    esc = false;
                    continue;
                }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { thinking = sb.ToString(); return true; }
                sb.Append(c);
            }

            thinking = sb.ToString();
            return true;
        }

        private void HandleResult(DialogueState state, DialogueResult result)
        {
            if (result.IsError)
            {
                OnError?.Invoke(result.ErrorMessage);
                return;
            }

            // 1. Apply system effects.
            state.ApplyEffects(result);

            // 2. Publish user-facing content.
            SetThinking(result.Thinking ?? "");
            if (!string.IsNullOrWhiteSpace(result.Narration))
            {
                History.Add(new ChatMessage(ChatRole.Assistant, result.Narration));
                OnNarration?.Invoke(result.Narration);
            }

            OnParticipants?.Invoke(result.Participants);
            OnChoices?.Invoke(result.Choices);

            // 3. Check for state transition.
            var nextType = state.GetNextState(result);
            if (nextType.HasValue && nextType.Value != state.StateType)
            {
                TransitionTo(nextType.Value);
            }
        }

        private void TrackUsage(LlmUsage usage)
        {
            cumulativeUsage = cumulativeUsage + usage;
            callCount++;
            OnUsage?.Invoke(usage, cumulativeUsage);
        }

        private void SetBusy(bool v)
        {
            if (Busy == v) return;
            Busy = v;
            OnBusyChanged?.Invoke(v);
        }
    }
}
