using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Sim;
using ACLS.Logging;
using Newtonsoft.Json;

namespace ACLS.Authoring
{
    // Thin MonoBehaviour shell around LlmDialogueOrchestrator.
    //
    // Responsibilities:
    //   - Receive UI calls (Choose, SubmitFreeform, ExpandCharacter, StartOpening)
    //   - Manage world-side side effects (ApplyEffects) on choice
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
        public string CurrentThinking { get; private set; } = "";

        public LlmUsage LastUsage { get; private set; }
        public LlmUsage CumulativeUsage { get; private set; }
        public float LastResponseTime { get; private set; }  // 最近一次 LLM 响应耗时（秒）
        public int CallCount => orchestrator?.CallCount ?? 0;

        public event Action<ChatMessage> OnMessage;
        public event Action<bool> OnBusyChanged;
        public event Action<IReadOnlyList<LlmReply.Choice>> OnChoicesChanged;
        public event Action<IReadOnlyList<LlmReply.Participant>> OnParticipantsChanged;
        public event Action<LlmUsage, LlmUsage> OnUsageReported;   // (lastCall, cumulative)
        public event Action<int, string, string, string> OnToolActivity;   // (round, toolName, args, result) — UI renders this in the Thinking panel
        public event Action<string> OnThinkingChanged;
        public event Action<string> OnMessageDelta;
        public event Action<string> OnSystemMessage;
        public event Action OnStreamingBegin;
        public event Action OnStreamingEnd;
        public event Action<UI.ChatBlock> OnBlock;                 // unified displayable block (Static or Streaming)

        private World world;
        private ILlmClient llm;
        private LlmDialogueOrchestrator orchestrator;
        private CancellationTokenSource cts;

        private List<LlmReply.Choice> currentChoices = new List<LlmReply.Choice>();
        private List<LlmReply.Participant> currentParticipants = new List<LlmReply.Participant>();
        private UI.ChatBlock _currentStreamBlock;

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
                // Assistant narration also fires via the streaming flow above;
                // OnStreamingEnd is what finalizes the block, so we don't push
                // a duplicate block here.
            };

            orchestrator.OnNarrationDelta += narration =>
            {
                // Skip empty deltas so the typewriter doesn't render a fresh
                // empty chat block when the LLM streams pure-tool-call rounds
                // (no `nar` field is emitted then — TryExtractNarration returns
                // false upstream, but defensively double-filter here).
                if (string.IsNullOrWhiteSpace(narration)) return;

                OnMessageDelta?.Invoke(narration);
                if (_currentStreamBlock != null)
                {
                    _currentStreamBlock.CurrentStreamText = narration ?? "";
                    OnBlock?.Invoke(_currentStreamBlock);   // re-fire as a data tick
                }
            };

            orchestrator.OnSystemDelta += message =>
            {
                OnSystemMessage?.Invoke(message);
                // System status: streaming block with immediate flush so the
                // typewriter types it out then is done.
                var b = UI.ChatBlock.Streaming(ChatRole.System, "系统");
                b.CurrentStreamText = message ?? "";
                b.StreamFlushed = true;
                OnBlock?.Invoke(b);
            };

            orchestrator.OnStreamingBegin += () =>
            {
                _currentStreamBlock = UI.ChatBlock.Streaming(ChatRole.Assistant, "旁白");
                // Defer OnBlock until the first narration delta arrives. This
                // prevents an empty chat block from appearing when the LLM's
                // streaming round emits only tool calls / thinking (no `nar`
                // field) — the block is created up front but only pushed to
                // the UI once real content is in it.
                OnStreamingBegin?.Invoke();
            };

            orchestrator.OnStreamingEnd += () =>
            {
                if (_currentStreamBlock != null)
                {
                    _currentStreamBlock.StreamFlushed = true;
                    // Only push to UI if the block actually accumulated any text.
                    // This prevents pure-tool-call rounds (which create a block
                    // via OnStreamingBegin but never fill it) from leaving a
                    // blank message in the chat panel.
                    if (!string.IsNullOrWhiteSpace(_currentStreamBlock.CurrentStreamText))
                        OnBlock?.Invoke(_currentStreamBlock);   // final tick
                    _currentStreamBlock = null;
                }
                OnStreamingEnd?.Invoke();
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

            orchestrator.OnEffects += effects =>
            {
                // 把记账员落地的数据用黄色文字显示在对话流里（便于核对双层 LLM 的记账）。
                string line = FormatEffectsYellow(effects);
                if (!string.IsNullOrEmpty(line))
                    OnBlock?.Invoke(UI.ChatBlock.Static(ChatRole.System, "", line));
            };

            orchestrator.OnUsage += (last, cumulative) =>
            {
                LastUsage = last;
                CumulativeUsage = cumulative;
                // Token usage no longer goes into the main ChatPanel — the panel
                // would otherwise fill with empty-looking rows whenever a tool
                // round produces no narration. The Thinking panel subscribes via
                // OnUsageReported to render these stats out of the way.
                OnUsageReported?.Invoke(last, cumulative);
            };

            // Tool activity forwarder: each round the LLM requests tools and
            // each tool returns, we re-fire so the UI can show "what tool
            // was called, with what args, and what came back" alongside the
            // thinking stream.
            orchestrator.OnToolActivity += (round, name, args, result) =>
            {
                OnToolActivity?.Invoke(round, name, args, result);
            };

            orchestrator.OnResponseTime += seconds =>
            {
                LastResponseTime = seconds;
            };

            orchestrator.OnBusyChanged += busy => OnBusyChanged?.Invoke(busy);

            orchestrator.OnThinkingChanged += thinking =>
            {
                CurrentThinking = thinking ?? "";
                OnThinkingChanged?.Invoke(CurrentThinking);
            };

            orchestrator.OnError += error =>
            {
                var msg = new ChatMessage(ChatRole.System, "[错误] " + error);
                OnMessage?.Invoke(msg);
                OnBlock?.Invoke(UI.ChatBlock.Static(ChatRole.System, "系统", "[错误] " + error));
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
            // Unstick any in-flight typewriter block — the orchestrator's
            // OnStreamingEnd won't fire on cancellation.
            if (_currentStreamBlock != null)
            {
                _currentStreamBlock.StreamFlushed = true;
                OnBlock?.Invoke(_currentStreamBlock);
                _currentStreamBlock = null;
            }
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

            Log.Debug(Log.Channels.Llm, "[Timing] Choose click t={0:F3}s index={1}", Time.realtimeSinceStartup, index);
            var choice = currentChoices[index];

            // 1. Show player's choice as "你" message.
            OnMessage?.Invoke(new ChatMessage(ChatRole.User, choice.Label));
            OnBlock?.Invoke(UI.ChatBlock.Static(ChatRole.User, "你", choice.Label ?? ""));

            // 2. Outcome narration / effects no longer come from the choice itself.
            //    The next narrative turn will describe consequences, and a
            //    second effects-only LLM call will update local data.

            // 3. Next LLM turn.
            string action = $"[玩家选择：{choice.Label}]\n请承接上文，描写下一幕，并给出 1-4 个新选项。";
            DispatchAction(action, choice.Label);
        }

        // Staleness-aware dispatcher. Before sending the player's action to
        // StagePlay, checks StageStaleness — if L1 is stale, runs StageRefresh
        // first and re-feeds the original action on completion.
        private void DispatchAction(string action, string display)
        {
            if (world?.Stage == null || !world.Stage.IsStageBuilt)
            {
                Log.Debug(Log.Channels.Llm,
                    "[Staleness] skip check (stage not built) | SendAction 直接提交 | display={0}",
                    Truncate(display ?? "", 60));
                orchestrator?.SendAction(action, displayText: display);
                return;
            }

            var report = ACLS.Authoring.Stage.StageStaleness.Evaluate(world);
            if (!report.L1NeedsFullRefresh)
            {
                // Fresh — log only at Debug to avoid spamming the main channel on every
                // player action. Stale path always logs at Info so the refresh sequence
                // is easy to grep in acls-runtime.log.
                Log.Debug(Log.Channels.Llm,
                    "[Staleness] L1 fresh | L1light={0} L2={1} L3={2} L4={3} | SendAction 直发 | display={4}",
                    report.L1NeedsLightRefresh, report.L2NeedsRefresh, report.L3NeedsRefresh, report.L4NeedsRefresh,
                    Truncate(display ?? "", 60));
                orchestrator?.SendAction(action, displayText: display);
                return;
            }

            Log.Info(Log.Channels.Llm,
                "[Staleness] L1 stale | reason={0} | L1light={1} L2={2} L3={3} L4={4} | 计划 StageRefresh→SendAction | display={5}",
                report.Reason, report.L1NeedsLightRefresh, report.L2NeedsRefresh, report.L3NeedsRefresh, report.L4NeedsRefresh,
                Truncate(display ?? "", 60));
            orchestrator?.StartStageRefresh(report.Reason, success =>
            {
                if (success)
                {
                    Log.Info(Log.Channels.Llm,
                        "[Staleness] StageRefresh 完成 | 续接原 SendAction | display={0}",
                        Truncate(display ?? "", 60));
                }
                else
                {
                    Log.Warn(Log.Channels.Llm,
                        "[Staleness] StageRefresh 失败 | 原 SendAction 仍提交 | display={0}",
                        Truncate(display ?? "", 60));
                }
                orchestrator?.SendAction(action, displayText: display);
            });
        }

        private static string Truncate(string s, int n) =>
            s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

        public void StartWorldBuild(string roleDescription, string worldDescription, Action<bool> onComplete)
        {
            if (!Ready) { onComplete?.Invoke(false); return; }
            orchestrator?.StartWorldBuild(roleDescription ?? "", worldDescription ?? "", success =>
            {
                if (success) orchestrator?.TransitionTo(DialogueStateType.StagePlay);
                onComplete?.Invoke(success);
            });
        }

        // 5-step pipeline: WorldBuild → L4Build → L3Build → L2Build → L1Build.
        public void StartWorldPipeline(string roleDescription, string worldDescription, Action<bool> onComplete)
        {
            if (!Ready) { onComplete?.Invoke(false); return; }
            orchestrator?.StartWorldPipeline(roleDescription ?? "", worldDescription ?? "", success =>
            {
                if (success) orchestrator?.TransitionTo(DialogueStateType.StagePlay);
                onComplete?.Invoke(success);
            });
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

        // Kicks off the first LLM call right after stage creation.
        public void StartOpening(CharacterPresets.Preset preset)
        {
            if (preset == null)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartOpening 跳过: preset is null");
                return;
            }
            if (!Ready)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartOpening 跳过: Ready=false (world={0} llm={1} orch={2})",
                    world != null, llm != null, orchestrator != null);
                return;
            }
            if (Busy)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartOpening 跳过: Busy=true");
                return;
            }
            orchestrator?.StartOpening(preset);
        }

        // 快速根据角色背景调 LLM 取一个贴背景的中文姓名+字。失败回调 name=null。
        // 独立于 WorldPipeline，不影响主流程 busy 状态。
        public void GenerateNameFromBlurb(string charBlurb, string era, string locationName,
                                          Action<string> onName, Action<string> onCourtesy,
                                          Action<string> onError)
        {
            if (llm == null) { onError?.Invoke("llm not ready"); return; }
            string blurb = (charBlurb ?? "").Trim();
            if (blurb.Length == 0) { onError?.Invoke("empty blurb"); return; }

            string sys = "你是一个中文起名助手。根据用户提供的角色背景，输出一个符合时代与身份的中文姓名（姓+名，1-2 字名）和字（1 字）。严格只返回 JSON：{\"name\":\"\",\"courtesy\":\"\"}。不要任何解释。";
            string usr =
                $"时代：{(string.IsNullOrEmpty(era) ? "未知" : era)}\n" +
                $"出身地：{(string.IsNullOrEmpty(locationName) ? "未知" : locationName)}\n" +
                $"角色背景：{blurb}\n\n" +
                "请给该角色起一个贴背景的中文姓名（含姓）和字。";

            // 独立 CancellationToken，避免主流程 cts 被影响
            var localCts = new CancellationTokenSource();
            _ = GenerateNameAsync(sys, usr, localCts.Token, onName, onCourtesy, onError);
        }

        private async System.Threading.Tasks.Task GenerateNameAsync(
            string sys, string usr, CancellationToken ct,
            Action<string> onName, Action<string> onCourtesy, Action<string> onError)
        {
            try
            {
                var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, usr) };
                var resp = await llm.CompleteAsync(sys, messages, ct);
                string text = resp?.Content ?? "";

                // 提取 JSON（容忍 ```json 围栏、前后杂文）
                string json = ExtractFirstJsonObject(text);
                if (string.IsNullOrEmpty(json)) { onError?.Invoke("no json in response"); return; }

                var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (obj == null) { onError?.Invoke("json parse failed"); return; }

                string name = obj.TryGetValue("name", out var n) ? (n ?? "").Trim() : "";
                string court = obj.TryGetValue("courtesy", out var c) ? (c ?? "").Trim() : "";
                if (name.Length == 0) { onError?.Invoke("empty name"); return; }

                onName?.Invoke(name);
                onCourtesy?.Invoke(court);
            }
            catch (System.Exception ex)
            {
                onError?.Invoke(ex.Message);
            }
        }

        private static string ExtractFirstJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int open = text.IndexOf('{');
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(open, i - open + 1);
                }
            }
            return null;
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
            OnBlock?.Invoke(UI.ChatBlock.Static(ChatRole.User, "你", text));

            string action = $"[玩家自由行动：{text}] 请承接上文，描写下一幕，并给出 1-4 个新选项。";
            DispatchAction(action, text);
        }

        // -------- helpers --------

        // Formats the bookkeeper-recorded effects into one yellow rich-text line.
        // Returns "" when there is nothing to show (so callers can skip the block).
        private static string FormatEffectsYellow(IReadOnlyList<LlmReply.EffectSpec> effects)
        {
            if (effects == null || effects.Count == 0) return "";

            var parts = new List<string>(effects.Count);
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || string.IsNullOrEmpty(e.Kind)) continue;
                string d = (e.Delta >= 0 ? "+" : "") + e.Delta;
                switch (e.Kind)
                {
                    case "AdjustStat":    parts.Add($"{e.Stat}{d}"); break;
                    case "AdjustGold":    parts.Add($"金{d}"); break;
                    case "AdjustOpinion": parts.Add($"{e.Target}好感{d}"); break;
                    case "AddTrait":      parts.Add($"+特质[{e.Trait}]"); break;
                    case "RemoveTrait":   parts.Add($"-特质[{e.Trait}]"); break;
                    case "SetFlag":       parts.Add($"标记+{e.Flag}"); break;
                    case "ClearFlag":     parts.Add($"标记-{e.Flag}"); break;
                    default:              parts.Add(e.Kind); break;
                }
            }

            if (parts.Count == 0) return "";
            return $"<size=15><color=#ffd24a>「数据」{string.Join(" · ", parts)}</color></size>";
        }

        private void ApplyEffects(LlmReply.Choice choice)
        {
            if (choice?.Effects == null || choice.Effects.Count == 0) return;
            var ops = EffectParser.ParseAll(choice.Effects, world);
            for (int i = 0; i < ops.Count; i++)
            {
                Effects.ApplyOne(ops[i], world, world.Player);
            }
        }

        

        private static DialogueSubState? MapToDialogueSubState(DialogueStateType type) =>
            type == DialogueStateType.StagePlay ? DialogueSubState.FreeNarrative : (DialogueSubState?)null;
    }
}
