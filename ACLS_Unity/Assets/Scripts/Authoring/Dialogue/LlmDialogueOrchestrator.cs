using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Llm.Tools;
using ACLS.Sim;
using ACLS.Sim.Narrative;
using ACLS.Logging;
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
        public event Action<IReadOnlyList<LlmReply.EffectSpec>> OnEffects;   // bookkeeper-recorded data (shown in yellow)
        public event Action<IReadOnlyList<LlmReply.Participant>> OnParticipants;
        public event Action<DialogueStateType, DialogueStateType> OnStateChanged;
        public event Action<bool> OnBusyChanged;
        public event Action<string> OnError;                   // human-readable error
        public event Action<LlmUsage, LlmUsage> OnUsage;       // (last, cumulative)
        public event Action<float> OnResponseTime;             // 响应耗时（秒）
        public event Action<string> OnThinkingChanged;
        public event Action<string> OnNarrationDelta;          // streaming narration delta
        public event Action<string> OnSystemDelta;             // system status delta (pipeline progress, etc.)
        public event Action OnStreamingBegin;                  // fired before each new streaming call
        public event Action OnStreamingEnd;                    // fired after stream ends, before ParseResponse

        // Tool activity surfaces. Fired in CompleteStreamWithTools so the UI can
        // show "what tools is the LLM calling and what came back" without having
        // to scrape the log. Args: (round, toolName, args, resultSummary).
        public event Action<int, string, string, string> OnToolActivity;

        // ---- tools ----
        public ToolRegistry ToolRegistry { get; } = ToolRegistry.Instance;

        // ---- internal ----
        private CancellationTokenSource lifetimeCts;
        private CancellationTokenSource currentCts;
        private CancellationTokenSource currentEffectsCts;   // tracks the in-flight effects-only LLM call so the next SendAction can cancel it (fire-and-forget).
        private readonly object effectsCtsLock = new object();
        private LlmUsage cumulativeUsage;
        private int callCount;
        private string currentThinking = "";
        private float lastResponseTime;  // 最近一次 LLM 响应耗时（秒），0 = 无数据
        private WorldBuildStepReply pipelineWorldBuildReply;
        private PlayerExpandReply pipelinePlayerExpandReply;

        public LlmDialogueOrchestrator(World world, ILlmClient llm,
            LlmPromptConfig promptConfig)
        {
            World = world;
            LlmClient = llm;
            PromptAssembler = new PromptAssembler(promptConfig, world);
            EffectRouter = new EffectRouter(world);
            History = new ChatHistory();
            lifetimeCts = new CancellationTokenSource();

            // 注册默认工具
            RegisterDefaultTools();
        }

        /// <summary>注册所有默认 LLM 工具。可被子类或额外初始化覆盖。</summary>
        public void RegisterDefaultTools()
        {
            ToolRegistry.Register(new CalculateTravelTool());
            ToolRegistry.Register(new LookupCharacterTool());
            ToolRegistry.Register(new LookupLocationTool());
            ToolRegistry.Register(new LookupFactionTool());
            // L1-builder 工具（需要 World 引用）
            ToolRegistry.Register(new ReadWorldLayerTool(World));
            ToolRegistry.Register(new ReadPlayerStateTool(World));
            ToolRegistry.Register(new ReadMemoryTool(World));
            ToolRegistry.Register(new WriteMemoryTool(World));
            // 时代大势(主线)工具
            ToolRegistry.Register(new ReadEraTrendTool(World));
        }

        public void Dispose()
        {
            try { lifetimeCts?.Cancel(); } catch { }
            lifetimeCts?.Dispose();
            lifetimeCts = null;
            try { currentCts?.Cancel(); } catch { }
            currentCts?.Dispose();
            currentCts = null;
            CancelEffectsInFlight();
        }

        public void CancelCurrent()
        {
            try { currentCts?.Cancel(); } catch { }
            CancelEffectsInFlight();
        }

        // Cancels and disposes any in-flight effects-only LLM call.
        // The background task observes the token, drops the parsed result
        // (no ApplyEffects, no events), and exits. Safe to call from any thread.
        private void CancelEffectsInFlight()
        {
            CancellationTokenSource toCancel = null;
            lock (effectsCtsLock)
            {
                toCancel = currentEffectsCts;
                currentEffectsCts = null;
            }
            if (toCancel == null) return;
            try { toCancel.Cancel(); } catch { }
            try { toCancel.Dispose(); } catch { }
        }

        // ---- state transitions ----

        public void TransitionTo(DialogueStateType type)
        {
            var prev = CurrentState?.StateType ?? (DialogueStateType)(-1);
            Log.Info(Log.Channels.Llm, "[Trans] TransitionTo(type): {0} → {1}", prev, type);
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
            Log.Info(Log.Channels.Llm, "[Trans] TransitionTo(state): {0} → {1}", prev, state.StateType);
            CurrentState?.Exit();
            CurrentState = state;
            CurrentState?.Enter();
            if (CurrentState != null && (int)prev != -1)
                OnStateChanged?.Invoke(prev, CurrentState.StateType);
        }

        // ---- public entry points (called by ChatBridge / UI) ----

        // 1. Opening scene (first narrative turn after expansion).
        public async void StartOpening(CharacterPresets.Preset preset)
        {
            if (Busy || LlmClient == null)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartOpening 跳过: Busy={0} LlmClient={1}", Busy, LlmClient != null);
                return;
            }

            Log.Info(Log.Channels.Llm, "▶ StartOpening: preset={0}", preset?.Title ?? preset?.Blurb);
            var state = new OpeningSceneState(this, preset);
            string toolHint = BuildToolHint();
            string behaviorHint = BuildBehaviorHint(state.BuildOpeningAction());
            string narrationPrompt = PromptAssembler?.AssembleNarrationAndChoices(state.StateType, state.BuildOpeningAction(), toolHint, behaviorHint) ?? "";

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") };
                var narrationResp = await CompleteStreamWithTools(narrationPrompt, messages, thisCts.Token);
                TrackUsage(narrationResp.Usage);

                if (!NarrationChoicesTextParser.TryParse(narrationResp.Content, out var narration, out var choices, out var effectTag, out var parseError))
                {
                    OnError?.Invoke("开场输出解析失败：" + parseError);
                    return;
                }

                DialogueResult result;
                if (ShouldRequestEffectsForOpening(effectTag))
                {
                    string effectsPrompt = PromptAssembler?.AssembleEffectsOnly(state.StateType, state.BuildOpeningAction(), narration) ?? "";
                    var effectsResp = await CompleteEffectsOnly(effectsPrompt, messages, thisCts.Token);
                    TrackUsageSilent(effectsResp.Usage);

                    LlmReply effectsReply = null;
                    if (!LlmReply.TryParseEffectsOnly(effectsResp.Content, out effectsReply, out var effectsError))
                    {
                        // Degrade gracefully: narration + choices are still valid.
                        Log.Warn(Log.Channels.Llm,
                            "开场 effects 解析失败，降级使用空 effects：{0} | rawLen={1} | content前200={2}",
                            effectsError,
                            effectsResp.Content?.Length ?? 0,
                            Truncate(effectsResp.Content ?? "", 200));
                        result = state.BuildNarrationTextResult(narrationResp.Content, narration, choices, null);
                    }
                    else
                    {
                        result = state.BuildNarrationTextResult(narrationResp.Content, narration, choices, effectsReply);
                    }
                }
                else
                {
                    result = state.BuildNarrationTextResult(narrationResp.Content, narration, choices, null);
                }

                HandleResult(state, result);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                {
                    Log.Error(Log.Channels.Llm, "已中断开场请求");
                    OnError?.Invoke("已中断开场请求。");
                }
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "开场调用失败: {0}", ex.Message);
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
                Log.Warn(Log.Channels.Llm, "[ERR] StartWorldBuild 跳过: Busy={0} LlmClient={1}", Busy, LlmClient != null);
                onComplete?.Invoke(false);
                return;
            }

            Log.Info(Log.Channels.Llm, "▶ StartWorldBuild: role={0} world={1}", Truncate(roleDescription ?? "", 60), Truncate(worldDescription ?? "", 60));
            EmitSystemStatus("正在构建世界观…");
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
                    Log.Error(Log.Channels.Llm, "世界构建解析失败: {0}", result.ErrorMessage);
                    OnError?.Invoke(result.ErrorMessage);
                    onComplete?.Invoke(false);
                    return;
                }

                SetThinking(result.Thinking ?? "");
                state.ApplyEffects(result);
                PublishNarration(result.Narration);
                SimLayerSync.Sync(World);
                SaveManager.Save(World, GameMemory.Instance);
                EmitSystemStatus("世界观构建完成 ✓");
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                {
                    Log.Error(Log.Channels.Llm, "已中断世界构建");
                    OnError?.Invoke("已中断世界构建。");
                }
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "世界构建调用失败: {0}", ex.Message);
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

        // ──── World 流水线：World → L4 → L3 → L2 → L1 ────

        /// <summary>
        /// 六阶段流水线构建世界：World(世界观) → L4(宏观势力) → L3(区域势力) → L2(近域网络) → Player(角色丰富+故事线) → L1(当前场景)。
        /// 每步调用一次 LLM，上一步的输出作为下一步的上下文。
        /// </summary>
        public async void StartWorldPipeline(string roleDescription, string worldDescription, Action<bool> onComplete)
        {
            if (Busy || LlmClient == null)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartWorldPipeline 跳过: Busy={0} LlmClient={1}", Busy, LlmClient != null);
                onComplete?.Invoke(false);
                return;
            }

            Log.Info(Log.Channels.Llm, "▶ StartWorldPipeline: role={0} world={1}",
                Truncate(roleDescription ?? "", 60), Truncate(worldDescription ?? "", 60));

            SetBusy(true);
            var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = pipelineCts;
            var pipelineSw = System.Diagnostics.Stopwatch.StartNew();

            string worldBuildContext = "";
            string l4Context = "";
            string l3Context = "";
            string playerContext = "";

            try
            {
                // ══════ Step 1: World（世界观设定） ══════
                Log.Info(Log.Channels.Llm, "▸ 流水线 Step 1/5: WorldBuild");
                EmitSystemStatus("正在构建世界观设定 (1/5)…");
                {
                    string fragment = LoadFragment("Fragment_WorldBuild");
                    string prompt = fragment
                        .Replace("{role_description}", roleDescription ?? "")
                        .Replace("{world_description}", worldDescription ?? "");

                    var resp = await CompleteStreamWithThinking(prompt,
                        new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始构建世界观") }, pipelineCts.Token);
                    TrackUsage(resp.Usage);

                    if (!WorldBuildStepReply.TryParse(resp.Content, out var wsReply, out var wsError))
                    {
                        Log.Error(Log.Channels.Llm, "WorldBuild 步骤解析失败: {0}", wsError);
                        OnError?.Invoke("世界构建失败：" + wsError);
                        onComplete?.Invoke(false);
                        return;
                    }

                    SetThinking(wsReply.Thinking ?? "");
                    pipelineWorldBuildReply = wsReply;
                    worldBuildContext = wsReply.ToContextText();
                    World.Stage.WorldBuild = worldBuildContext;

                    // LLM 指定的剧本起始日期写入 World.Date。
                    // 若 LLM 未输出 start_date 或解析失败，World.Date 保持 0,0,0，后续环节应作“未初始化”处理。
                    if (wsReply.StartDate.Year > 0)
                    {
                        World.Date = wsReply.StartDate;
                        BackfillCharacterBirths();
                        Log.Info(Log.Channels.Llm, "[WorldBuild] 起始日期写入: {0}", World.Date);
                    }
                    else
                    {
                        Log.Warn(Log.Channels.Llm, "[WorldBuild] LLM 未提供有效 start_date，World.Date 保持未初始化 (0,0,0)");
                    }

                    PublishNarration(wsReply.Narration);

                    // Build player context string for subsequent steps (basic info only; expansion happens later in Step 5)
                    {
                        var p = World.Player;
                        var sb = new System.Text.StringBuilder();
                        if (p != null)
                        {
                            sb.Append($"姓名：{p.Name}，{p.AgeAt(World.Date)}岁，{(p.Sex == Sim.Sex.Male ? "男" : "女")}");
                            if (!string.IsNullOrWhiteSpace(p.Courtesy)) sb.Append($"，字{p.Courtesy}");
                            if (!string.IsNullOrWhiteSpace(p.BackgroundStory)) sb.Append($"\n[背景] {p.BackgroundStory}");
                            if (!string.IsNullOrWhiteSpace(p.Values)) sb.Append($"\n[价值观] {p.Values}");
                            if (!string.IsNullOrWhiteSpace(p.CurrentGoal)) sb.Append($"\n[近期目标] {p.CurrentGoal}");
                            if (!string.IsNullOrWhiteSpace(p.Secret)) sb.Append($"\n[秘密] {p.Secret}");
                            if (p.Connections.Count > 0) sb.Append($"\n[人脉] {string.Join("、", p.Connections)}");
                            if (p.KnownFacts.Count > 0) sb.Append($"\n[已知情报] {string.Join("、", p.KnownFacts)}");
                            if (p.OwnedItems.Count > 0) sb.Append($"\n[随身物品] {string.Join("、", p.OwnedItems)}");
                        }
                        playerContext = sb.ToString().Trim();
                    }
                    SaveManager.Save(World, GameMemory.Instance);
                    Log.Info(Log.Channels.Llm, "[OK] Step 1/5 WorldBuild 完成 (+{0:F1}s)", pipelineSw.Elapsed.TotalSeconds);
                    EmitSystemStatus("世界观设定完成 ✓");
                }

                // ══════ Step 2: L4（宏观势力） ══════
                Log.Info(Log.Channels.Llm, "▸ 流水线 Step 2/5: L4Build");
                EmitSystemStatus("正在构建宏观势力格局 (2/5)…");
                {
                    string fragment = LoadFragment("Fragment_L4Build");
                    string prompt = fragment
                        .Replace("{world_setting_context}", worldBuildContext)
                        .Replace("{player_context}", playerContext)
                        .Replace("{role_description}", roleDescription ?? "")
                        .Replace("{world_description}", worldDescription ?? "");

                    var resp = await CompleteStreamWithThinking(prompt,
                        new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始构建宏观势力") }, pipelineCts.Token);
                    TrackUsage(resp.Usage);

                    var (l4Ok, l4Text, l4Factions, l4Narration) = ParseL4Reply(resp.Content);
                    if (!l4Ok)
                    {
                        Log.Error(Log.Channels.Llm, "L4 步骤解析失败");
                        OnError?.Invoke("宏观势力构建失败。");
                        onComplete?.Invoke(false);
                        return;
                    }

                    SetThinking("");
                    l4Context = l4Text;
                    World.Stage.L4World = l4Text;
                    foreach (var f in l4Factions.Factions)
                        GameMemory.Instance.AddFaction(new FactionEntry { name = f.name, stance = f.stance, type = "macro" });
                    PublishNarration(l4Narration);
                    SimLayerSync.Sync(World);
                    SaveManager.Save(World, GameMemory.Instance);
                    Log.Info(Log.Channels.Llm, "[OK] Step 2/5 L4Build 完成，{0} 个势力 (+{1:F1}s)", l4Factions.Factions.Count, pipelineSw.Elapsed.TotalSeconds);
                    EmitSystemStatus("宏观势力格局构建完成 ✓");
                }

                // ══════ Step 3: L3（区域势力） ══════
                Log.Info(Log.Channels.Llm, "▸ 流水线 Step 3/5: L3Build");
                EmitSystemStatus("正在构建区域势力网络 (3/5)…");
                {
                    string fragment = LoadFragment("Fragment_L3Build");
                    string prompt = fragment
                        .Replace("{world_setting_context}", worldBuildContext)
                        .Replace("{l4_context}", l4Context)
                        .Replace("{player_context}", playerContext)
                        .Replace("{role_description}", roleDescription ?? "")
                        .Replace("{world_description}", worldDescription ?? "");

                    var resp = await CompleteStreamWithThinking(prompt,
                        new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始构建区域势力") }, pipelineCts.Token);
                    TrackUsage(resp.Usage);

                    var (l3Ok, l3Text, l3Factions, l3Narration) = ParseL3Reply(resp.Content);
                    if (!l3Ok)
                    {
                        Log.Error(Log.Channels.Llm, "L3 步骤解析失败 | resp.Content 长度={0} | 前400字:\n{1}\n后200字:\n{2}",
                            (resp.Content ?? "").Length,
                            (resp.Content ?? "").Length > 400 ? resp.Content.Substring(0, 400) : (resp.Content ?? ""),
                            (resp.Content ?? "").Length > 200 ? resp.Content.Substring((resp.Content ?? "").Length - 200) : "");
                        OnError?.Invoke("区域势力构建失败。");
                        onComplete?.Invoke(false);
                        return;
                    }

                    SetThinking("");
                    l3Context = l3Text;
                    World.Stage.L3Expanse = l3Text;
                    foreach (var f in l3Factions.Factions)
                        GameMemory.Instance.AddFaction(new FactionEntry { name = f.name, stance = f.stance, type = "regional" });
                    PublishNarration(l3Narration);
                    SimLayerSync.Sync(World);
                    SaveManager.Save(World, GameMemory.Instance);
                    Log.Info(Log.Channels.Llm, "[OK] Step 3/5 L3Build 完成，{0} 个区域势力 (+{1:F1}s)", l3Factions.Factions.Count, pipelineSw.Elapsed.TotalSeconds);
                    EmitSystemStatus("区域势力网络构建完成 ✓");
                }

                // ══════ Step 4: L2（近域网络） ══════
                Log.Info(Log.Channels.Llm, "▸ 流水线 Step 4/5: L2Build");
                EmitSystemStatus("正在构建近域人际网络 (4/5)…");
                {
                    string fragment = LoadFragment("Fragment_L2Build");
                    string prompt = fragment
                        .Replace("{world_setting_context}", worldBuildContext)
                        .Replace("{l4_context}", l4Context)
                        .Replace("{l3_context}", l3Context)
                        .Replace("{player_context}", playerContext)
                        .Replace("{role_description}", roleDescription ?? "")
                        .Replace("{world_description}", worldDescription ?? "");

                    bool l2Ok = false;
                    string l2Text = null;
                    Entities l2Entities = null;
                    string l2Narration = null;

                    for (int attempt = 0; attempt <= 2; attempt++)
                    {
                        string userMsg = attempt == 0
                            ? "开始构建近域网络"
                            : "格式不对，请严格按照要求的 JSON 格式重新生成。不要任何额外文字。";

                        var resp = await CompleteStreamWithThinking(prompt,
                            new List<ChatMessage> { new ChatMessage(ChatRole.User, userMsg) }, pipelineCts.Token);
                        TrackUsage(resp.Usage);

                        (l2Ok, l2Text, l2Entities, l2Narration) = ParseL2Reply(resp.Content);
                        if (l2Ok) break;

                        Log.Warn(Log.Channels.Llm, "L2 解析失败，第{0}次重试...", attempt + 1);
                    }

                    if (!l2Ok)
                    {
                        Log.Error(Log.Channels.Llm, "L2 步骤解析失败（已重试）");
                        OnError?.Invoke("近域网络构建失败。");
                        onComplete?.Invoke(false);
                        return;
                    }

                    SetThinking("");
                    World.Stage.L2Arena = l2Text;
                    foreach (var c in l2Entities.Chars)
                        GameMemory.Instance.AddChar(new CharEntry { name = c.name, role = c.role, location = c.location, relation = c.relation, reachable_in_days = c.reachable_in_days });
                    foreach (var f in l2Entities.Factions)
                        GameMemory.Instance.AddFaction(new FactionEntry { name = f.name, type = f.type, stance = f.stance });
                    foreach (var p in l2Entities.Places)
                        GameMemory.Instance.AddPlace(new PlaceEntry { name = p.name, type = p.type, description = p.description });
                    PublishNarration(l2Narration);
                    SimLayerSync.Sync(World);
                    SaveManager.Save(World, GameMemory.Instance);
                    Log.Info(Log.Channels.Llm, "[OK] Step 4/5 L2Build 完成: chars={0} factions={1} places={2} (+{3:F1}s)",
                        l2Entities.Chars.Count, l2Entities.Factions.Count, l2Entities.Places.Count, pipelineSw.Elapsed.TotalSeconds);
                    EmitSystemStatus("近域人际网络构建完成 ✓");
                }

                // ══════ Step 5: 角色设定 + 故事线 + 初始场景（合并为一次调用） ══════
                Log.Info(Log.Channels.Llm, "▸ 流水线 Step 5/5: 角色设定 + 初始场景");
                EmitSystemStatus("正在扩展角色设定、故事线与初始场景 (5/5)…");
                {
                    string fragment = LoadFragment("Fragment_PlayerExpandStoryline");
                    string prompt = fragment
                        .Replace("{world_setting_context}", worldBuildContext)
                        .Replace("{l4_context}", l4Context)
                        .Replace("{l3_context}", l3Context)
                        .Replace("{l2_context}", World.Stage.L2Arena)
                        .Replace("{player_context}", playerContext);

                    var resp = await CompleteStreamWithThinking(prompt,
                        new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始角色丰富化、故事线与初始场景构建") }, pipelineCts.Token);
                    TrackUsage(resp.Usage);

                    // 同一份回复跑两个解析器：PlayerExpand 读 pe/sl/ne，L1 读 l1s（字段互不重叠）。
                    if (!PlayerExpandReply.TryParse(resp.Content, out var peReply, out var peError))
                    {
                        Log.Error(Log.Channels.Llm, "角色设定步骤解析失败: {0}", peError);
                        OnError?.Invoke("角色丰富化失败：" + peError);
                        onComplete?.Invoke(false);
                        return;
                    }

                    SetThinking(peReply.Thinking ?? "");
                    pipelinePlayerExpandReply = peReply;

                    // Apply player expansion data
                    var player = World.Player;
                    if (player != null)
                    {
                        if (!string.IsNullOrWhiteSpace(peReply.BackgroundStory)) player.BackgroundStory = peReply.BackgroundStory;
                        if (!string.IsNullOrWhiteSpace(peReply.Values)) player.Values = peReply.Values;
                        if (!string.IsNullOrWhiteSpace(peReply.CurrentGoal)) player.CurrentGoal = peReply.CurrentGoal;
                        if (!string.IsNullOrWhiteSpace(peReply.Secret)) player.Secret = peReply.Secret;
                        if (peReply.Connections.Count > 0) player.Connections = peReply.Connections;
                        if (peReply.KnownFacts.Count > 0) player.KnownFacts = peReply.KnownFacts;
                        if (peReply.OwnedItems.Count > 0) player.OwnedItems = peReply.OwnedItems;
                    }

                    // Apply NPC expansions (enrich nearby core characters)
                    foreach (var npcExp in peReply.NpcExpansions)
                    {
                        GameMemory.Instance.ApplyNpcExpansion(
                            npcExp.Name,
                            npcExp.BackgroundStory,
                            npcExp.Values,
                            npcExp.CurrentGoal,
                            npcExp.Secret);
                    }
                    Log.Info(Log.Channels.Llm, "[PlayerExpand] NPC 丰富化完成: {0} 人", peReply.NpcExpansions.Count);

                    // Store storyline text
                    World.Stage.L2Expansion = peReply.ToContextText();

                    // Parse the L1 scene from the SAME response.
                    var (l1Ok, l1Text, l1Narration) = ParseL1Reply(resp.Content);
                    if (!l1Ok)
                    {
                        Log.Error(Log.Channels.Llm, "初始场景解析失败，原始内容={0}", Truncate(resp.Content ?? "(空)", 500));
                        OnError?.Invoke("初始场景构建失败。");
                        onComplete?.Invoke(false);
                        return;
                    }
                    World.Stage.L1Stage = l1Text;

                    // Touch L1 staleness metadata so StageStaleness.Evaluate knows
                    // when / where L1 was last built.
                    int locId = World.Player?.Identity?.LocationId ?? 0;
                    int npcCount = peReply?.NpcExpansions?.Count ?? 0;
                    World.Stage.TouchL1(World, locId, npcCount);

                    // nar 是同一个字段，两个解析器读到的一致——只发布一次。
                    string narration = !string.IsNullOrWhiteSpace(peReply.Narration) ? peReply.Narration : l1Narration;
                    PublishNarration(narration);

                    SaveManager.Save(World, GameMemory.Instance);
                    Log.Info(Log.Channels.Llm, "[OK] Step 5/5 角色设定 + 初始场景 完成: storylines={0} (+{1:F1}s)", peReply.Storylines.Count, pipelineSw.Elapsed.TotalSeconds);
                    EmitSystemStatus("角色设定与初始场景构建完成 ✓");
                }

                // ══════ 全部完成 ══════
                Log.Info(Log.Channels.Llm, "[OK] 世界流水线全部完成 (+{0:F1}s total)", pipelineSw.Elapsed.TotalSeconds);
                EmitSystemStatus("世界构建全部完成，准备开始游戏");
                // Release the busy flag BEFORE the onComplete callback runs so that
                // callbacks (e.g. NewGameView → chat.StartOpening) see Busy=false
                // and can kick off the next LLM call without bouncing off the guard.
                SetBusy(false);
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                {
                    Log.Error(Log.Channels.Llm, "已中断世界流水线");
                    OnError?.Invoke("已中断世界构建。");
                }
                SetBusy(false);
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "世界流水线异常: {0}", ex.Message);
                OnError?.Invoke("世界构建异常：" + ex.Message);
                SetBusy(false);
                onComplete?.Invoke(false);
            }
            finally
            {
                if (currentCts == pipelineCts) currentCts = null;
                pipelineCts.Dispose();
                SetBusy(false);
            }
        }


        // 5. Stage create (one-shot, no history — called after character expansion).
        public async void StartStageCreate(CharacterPresets.Preset preset, Action<bool> onComplete)
        {
            if (Busy || LlmClient == null)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartStageCreate 跳过: Busy={0} LlmClient={1}", Busy, LlmClient != null);
                onComplete?.Invoke(false);
                return;
            }

            Log.Info(Log.Channels.Llm, "▶ StartStageCreate: preset={0}", preset?.Title ?? preset?.Blurb);
            EmitSystemStatus("正在生成舞台与初始场景…");
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
                    Log.Error(Log.Channels.Llm, "舞台生成解析失败: {0}", result.ErrorMessage);
                    OnError?.Invoke(result.ErrorMessage);
                    onComplete?.Invoke(false);
                    return;
                }

                SetThinking(result.Thinking ?? "");
                state.ApplyEffects(result);
                PublishNarration(result.Narration);
                SimLayerSync.Sync(World);
                SaveManager.Save(World, GameMemory.Instance);
                EmitSystemStatus("舞台与初始场景生成完成 ✓");
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                {
                    Log.Error(Log.Channels.Llm, "已中断舞台生成");
                    OnError?.Invoke("已中断舞台生成。");
                }
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "舞台生成调用失败: {0}", ex.Message);
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

        // 2b. Incremental L1 rebuild — triggered when StageStaleness reports
        // L1NeedsFullRefresh before a StagePlay turn. Rebuilds only L1_Stage,
        // reuses L2/L3/L4 and the player / NPC expansions. On success the
        // caller (ChatBridge) re-feeds the original player action into SendAction.
        public async void StartStageRefresh(string triggerReason, Action<bool> onComplete)
        {
            if (Busy || LlmClient == null || World == null)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] StartStageRefresh 跳过: Busy={0} LlmClient={1} World={2}",
                    Busy, LlmClient != null, World != null);
                onComplete?.Invoke(false);
                return;
            }

            Log.Info(Log.Channels.Llm, "▶ StartStageRefresh: reason={0}", triggerReason);
            EmitSystemStatus("场景已过期，正在重建…");

            string previousL1 = World.Stage?.L1Stage ?? "";
            string fragment = LoadFragment("Fragment_L1Refresh");
            string playerContext = BuildPlayerContextText();
            string l4Context = World.Stage?.L4World ?? "";
            string l3Context = World.Stage?.L3Expanse ?? "";
            string l2Context = World.Stage?.L2Arena ?? "";

            string prompt = fragment
                .Replace("{previous_l1}", previousL1)
                .Replace("{l4_context}", l4Context)
                .Replace("{l3_context}", l3Context)
                .Replace("{l2_context}", l2Context)
                .Replace("{player_context}", playerContext)
                .Replace("{trigger_reason}", triggerReason ?? "未指定");

            // Augment with tool descriptors and the same "use tools" hint that
            // WorldBuildState gets, so the LLM knows it can call lookup_character
            // / read_player_state / read_world_layer to verify data.
            string toolHint = "\n\n## 可用工具\n" +
                "- read_player_state：返回玩家当前位置/状态\n" +
                "- read_world_layer(\"L2\"|\"L3\"|\"L4\")：返回该层背景文本\n" +
                "- lookup_character(name)：查询人物详情\n" +
                "- lookup_location(name)：查询地点详情\n" +
                "- lookup_faction(name)：查询势力详情\n" +
                "构建 L1 时优先调以上工具核实数据。\n";
            prompt += toolHint;

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            try
            {
                // Tool-driven stream: LLM may call read_player_state / lookup_character etc.
                var resp = await CompleteStreamWithTools(prompt,
                    new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始场景重建") }, thisCts.Token);
                TrackUsage(resp.Usage);

                var (ok, l1Text, narration) = ParseL1Reply(resp.Content);
                if (!ok)
                {
                    Log.Error(Log.Channels.Llm, "StageRefresh L1 解析失败 | content 前 300: {0}", Truncate(resp.Content ?? "", 300));
                    Log.Warn(Log.Channels.Llm,
                        "[StageRefresh] 解析失败 | reason={0} | onComplete(false)", triggerReason);
                    OnError?.Invoke("场景重建解析失败。");
                    onComplete?.Invoke(false);
                    return;
                }

                World.Stage.L1Stage = l1Text;

                // Touch staleness so the next Evaluate won't immediately re-trigger.
                int locId = World.Player?.Identity?.LocationId ?? 0;
                int npcCount = CountNpcsAtLocation(World, locId);
                World.Stage.TouchL1(World, locId, npcCount);

                // Publish narration as a normal message so the player sees the transition.
                if (!string.IsNullOrWhiteSpace(narration))
                    PublishNarration(narration);

                Log.Info(Log.Channels.Llm,
                    "[StageRefresh] 完成 | reason={0} L1长度={1} loc={2} npcs={3} narration长度={4} | onComplete(true)",
                    triggerReason, l1Text.Length, locId, npcCount, narration?.Length ?? 0);

                // Release Busy BEFORE invoking onComplete so the callback
                // (which typically re-feeds the original action into SendAction)
                // isn't rejected by the Busy guard.
                SetBusy(false);
                onComplete?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                {
                    Log.Error(Log.Channels.Llm, "已中断场景重建");
                    OnError?.Invoke("已中断场景重建。");
                }
                Log.Warn(Log.Channels.Llm,
                    "[StageRefresh] 已取消 | reason={0} | onComplete(false)", triggerReason);
                SetBusy(false);
                onComplete?.Invoke(false);
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "StageRefresh 异常: {0}", ex.Message);
                OnError?.Invoke("场景重建异常：" + ex.Message);
                Log.Warn(Log.Channels.Llm,
                    "[StageRefresh] 失败 | reason={0} | onComplete(false) | ex={1}",
                    triggerReason, ex.GetType().Name);
                SetBusy(false);
                onComplete?.Invoke(false);
            }
            finally
            {
                if (currentCts == thisCts) currentCts = null;
                thisCts.Dispose();
                // SetBusy(false) is called inside try/catch BEFORE onComplete so the
                // callback can submit a follow-up action without bouncing off Busy.
                // Guard against the case where control flow exits via raw throw (not
                // caught) so Busy is still released.
                SetBusy(false);
            }
        }

        private static int CountNpcsAtLocation(World world, int locationId)
        {
            if (world == null || locationId == 0) return 0;
            int playerId = world.PlayerCharacterId;
            int count = 0;
            foreach (var c in world.AliveCharacters())
            {
                if (c.Id == playerId) continue;
                if (c.Identity != null && c.Identity.LocationId == locationId) count++;
            }
            return count;
        }

        private string BuildPlayerContextText()
        {
            var p = World?.Player;
            if (p == null) return "";
            var sb = new StringBuilder();
            sb.Append($"姓名：{p.Name}，{p.AgeAt(World.Date)}岁，{(p.Sex == Sex.Male ? "男" : "女")}");
            if (!string.IsNullOrWhiteSpace(p.Courtesy)) sb.Append($"，字{p.Courtesy}");
            if (!string.IsNullOrWhiteSpace(p.BackgroundStory)) sb.Append($"\n[背景] {p.BackgroundStory}");
            if (!string.IsNullOrWhiteSpace(p.Values)) sb.Append($"\n[价值观] {p.Values}");
            if (!string.IsNullOrWhiteSpace(p.CurrentGoal)) sb.Append($"\n[近期目标] {p.CurrentGoal}");
            if (!string.IsNullOrWhiteSpace(p.Secret)) sb.Append($"\n[秘密] {p.Secret}");
            if (p.Connections != null && p.Connections.Count > 0) sb.Append($"\n[人脉] {string.Join("、", p.Connections)}");
            if (p.KnownFacts != null && p.KnownFacts.Count > 0) sb.Append($"\n[已知情报] {string.Join("、", p.KnownFacts)}");
            if (p.OwnedItems != null && p.OwnedItems.Count > 0) sb.Append($"\n[随身物品] {string.Join("、", p.OwnedItems)}");
            return sb.ToString();
        }

        // 2. Regular narrative turn (choice selected or freeform input).
        public async void SendAction(string userInput, string displayText = null,
            bool addToHistory = true)
        {
            if (Busy || LlmClient == null || CurrentState == null)
            {
                Log.Warn(Log.Channels.Llm, "[ERR] SendAction 跳过: Busy={0} LlmClient={1} CurrentState={2}", Busy, LlmClient != null, CurrentState?.StateType);
                return;
            }

            // Cancel any in-flight effects-only call from the previous turn so
            // its stale result doesn't land in the world after the player has
            // already moved on. The background task observes the token and exits.
            CancelEffectsInFlight();

            Log.Info(Log.Channels.Llm, "▶ SendAction: userInput={0} displayText={1} addToHistory={2}",
                Truncate(userInput ?? "", 80), Truncate(displayText ?? "", 80), addToHistory);

            if (addToHistory && !string.IsNullOrWhiteSpace(displayText))
                History.Add(new ChatMessage(ChatRole.User, displayText));

            var swAssemble = System.Diagnostics.Stopwatch.StartNew();
            string toolHint = BuildToolHint();
            string behaviorHint = BuildBehaviorHint(userInput);
            string narrationPrompt = PromptAssembler?.AssembleNarrationAndChoices(CurrentState.StateType, userInput, toolHint, behaviorHint) ?? "";
            swAssemble.Stop();
            Log.Debug(Log.Channels.Llm, "[Timing] Prompt组装: {0:F2}s, prompt长度={1}", swAssemble.Elapsed.TotalSeconds, narrationPrompt?.Length ?? 0);

            SetBusy(true);
            var thisCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            currentCts = thisCts;
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var messages = History.Recent(RecentMessages);

                var swStream = System.Diagnostics.Stopwatch.StartNew();
                var narrationResp = await CompleteStreamWithTools(narrationPrompt, messages, thisCts.Token);
                swStream.Stop();
                TrackUsage(narrationResp.Usage);
                Log.Debug(Log.Channels.Llm, "[Timing] 文本流式调用: {0:F2}s", swStream.Elapsed.TotalSeconds);

                var swParse = System.Diagnostics.Stopwatch.StartNew();
                bool parsed = NarrationChoicesTextParser.TryParse(narrationResp.Content, out var narration, out var choices, out var effectTag, out var parseError);
                swParse.Stop();
                Log.Debug(Log.Channels.Llm, "[Timing] 文本响应解析: {0:F2}s, content长度={1}", swParse.Elapsed.TotalSeconds, narrationResp.Content?.Length ?? 0);

                if (!parsed)
                {
                    OnError?.Invoke("叙事输出解析失败：" + parseError);
                    return;
                }

                // Publish narration + choices NOW with empty effects so the
                // UI can show the options immediately. The effects-only LLM
                // call runs in the background; its result (when it lands)
                // mutates the world and emits OnEffects for the yellow meta
                // line. Players no longer wait 8-17s for choices to appear.
                var result = BuildNarrationTextResult(narrationResp.Content, narration, choices, null);

                var swHandle = System.Diagnostics.Stopwatch.StartNew();
                HandleResult(CurrentState, result);
                swHandle.Stop();
                Log.Debug(Log.Channels.Llm, "[Timing] HandleResult: {0:F2}s, 总耗时: {1:F2}s",
                    swHandle.Elapsed.TotalSeconds, swTotal.Elapsed.TotalSeconds);

                // Kick off effects in the background. We pass a fresh List
                // copy of `messages` so the fire-and-forget task keeps its
                // own reference (the local `messages` would be GC'd once
                // SendAction returns).
                if (ShouldRequestEffectsForStagePlay(userInput, effectTag))
                {
                    var messagesCopy = new List<ChatMessage>(messages);
                    _ = RunEffectsInBackgroundAsync(narrationPrompt, messagesCopy,
                        narrationResp.Content, narration, choices, userInput);
                }
            }
            catch (OperationCanceledException)
            {
                if (!lifetimeCts.IsCancellationRequested)
                {
                    Log.Error(Log.Channels.Llm, "已中断当前请求");
                    OnError?.Invoke("已中断当前请求。");
                }
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "调用 LLM 失败: {0}", ex.Message);
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

        // Fire-and-forget effects-only call. Runs in the background after the
        // narration + choices have already been published to the UI, so the
        // player can act on the new options without waiting 8-17s for the
        // bookkeeping LLM call to finish.
        //
        // Cancellation contract:
        //   * CancelEffectsInFlight() (called from SendAction's entry and
        //     Dispose / CancelCurrent) signals via the registered token.
        //   * When cancelled mid-flight, the parsed result is discarded —
        //     ApplyEffects is NOT called, OnEffects is NOT emitted, and the
        //     world state remains as it was before this turn's effects.
        //   * On normal completion, the parsed effects are applied via the
        //     EffectRouter and OnEffects fires (so the chat panel can paint
        //     the yellow "数据" line for this turn).
        private async Task RunEffectsInBackgroundAsync(
            string narrationPrompt, List<ChatMessage> messages,
            string rawNarrationResponse, string narration,
            List<LlmReply.Choice> choices, string userInput)
        {
            CancellationTokenSource effectsCts;
            lock (effectsCtsLock)
            {
                effectsCts = new CancellationTokenSource();
                currentEffectsCts = effectsCts;
            }

            try
            {
                string effectsPrompt = PromptAssembler?.AssembleEffectsOnly(CurrentState.StateType, userInput, narration) ?? "";
                var effectsResp = await CompleteEffectsOnly(effectsPrompt, messages, effectsCts.Token);
                TrackUsageSilent(effectsResp.Usage);

                if (effectsCts.IsCancellationRequested) return;  // lost the race — drop result

                LlmReply effectsReply = null;
                if (!LlmReply.TryParseEffectsOnly(effectsResp.Content, out effectsReply, out var effectsError))
                {
                    Log.Warn(Log.Channels.Llm,
                        "background effects 解析失败，跳过本轮数据落地：{0} | rawLen={1} | content前200={2}",
                        effectsError,
                        effectsResp.Content?.Length ?? 0,
                        Truncate(effectsResp.Content ?? "", 200));
                    return;
                }

                // Apply effects via the same path HandleResult uses for narration turn effects.
                var effectsResult = BuildNarrationTextResult(rawNarrationResponse, narration, choices, effectsReply);
                // Re-check cancellation right before mutating world state so a
                // mid-flight SendAction's CancelEffectsInFlight() doesn't apply
                // stale effects after the player has already moved on.
                if (effectsCts.IsCancellationRequested) return;
                if (CurrentState != null)
                    CurrentState.ApplyEffects(effectsResult);
                OnEffects?.Invoke(effectsResult.Effects);
            }
            catch (OperationCanceledException)
            {
                // Cancelled by SendAction's CancelEffectsInFlight() — silent exit.
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Llm, "background effects 调用失败: {0}", ex.Message);
            }
            finally
            {
                lock (effectsCtsLock)
                {
                    if (currentEffectsCts == effectsCts)
                        currentEffectsCts = null;
                }
                effectsCts.Dispose();
            }
        }

        private void SetThinking(string thinking)
        {
            currentThinking = thinking ?? "";
            if (PlayerLoopHelper.IsMainThread)
                OnThinkingChanged?.Invoke(currentThinking);
            else
                UniTask.Post(() => OnThinkingChanged?.Invoke(currentThinking));
        }

        private void EmitSystemStatus(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (PlayerLoopHelper.IsMainThread)
                OnSystemDelta?.Invoke(message);
            else
                UniTask.Post(() => OnSystemDelta?.Invoke(message));
        }

        /// <summary>发布 narration。</summary>
        private void PublishNarration(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            History.Add(new ChatMessage(ChatRole.Assistant, text));
            OnNarration?.Invoke(text);
        }

        /// <summary>
        /// 生成工具提示文本，注入到 system prompt。告诉 LLM：本地可以读哪些数据 / 何时该调用。
        /// 描述从 ToolRegistry 动态生成，避免与硬编码列表脱节。
        /// </summary>
        private string BuildToolHint()
        {
            var tools = ToolRegistry.GetAllDefinitions();
            if (tools == null || tools.Count == 0) return null;

            var sb = new StringBuilder();
            sb.Append("## 可用本地工具\n\n");
            sb.Append("以下工具可调用，调用后本地会返回实时数据。**需要核实人物 / 地点 / 势力 / 玩家状态 / 世界背景时优先调用，不要凭记忆捏造姓名、位置或关系。**\n\n");
            sb.Append("| 工具 | 必填参数 | 用途 |\n|---|---|---|\n");
            foreach (var t in tools)
            {
                string desc = (t.Description ?? "").Replace("\n", " ").Replace("|", "\\|");
                if (desc.Length > 60) desc = desc.Substring(0, 60) + "…";
                string paramsHint = ExtractRequiredParams(t.InputSchema);
                sb.Append("| `").Append(t.Name).Append("` | ").Append(paramsHint).Append(" | ").Append(desc).Append(" |\n");
            }
            sb.Append("\n**调用格式严格要求**：以 JSON 对象形式传参。例如：\n");
            sb.Append("- `read_world_layer({\"layer\": \"L4\"})` — layer 必须是 `\"L2\"` / `\"L3\"` / `\"L4\"` 之一\n");
            sb.Append("- `lookup_character({\"name\": \"何茂\"})` — name 为人物中文名\n");
            sb.Append("- `lookup_faction({\"name\": \"泛银河联邦\"})` — name 为势力中文名\n");
            sb.Append("- `lookup_location({\"name\": \"�的修理铺\"})` — name 为地点中文名\n");
            sb.Append("- `calculate_travel({\"from\": \"新希望站\", \"to\": \"炽天使酒吧\"})`\n");
            sb.Append("- `read_player_state()` / `read_memory({\"count\": 5})` / `read_era_trend()` / `write_memory({\"date\": \"184年01月08日\", \"event\": \"...\"})`\n");
            sb.Append("\n**参数名、参数格式、参数值都要严格正确**。调用报错时请仔细检查参数格式（如忘了引号、拼错参数名），换另一工具调用或者不调，不要重复试同一工具。\n");
            return sb.ToString();
        }

        /// <summary>
        /// 从 world.Stage.L1Stage 文本提取 [聚落内] 段 NPC 名字，
        /// 结合 playerText 计算 behavior hint 文本。非 StagePlay 状态返回 null。
        /// </summary>
        private string BuildBehaviorHint(string playerText)
        {
            if (CurrentState?.StateType != DialogueStateType.StagePlay) return null;
            var l1 = World?.Stage?.L1Stage;
            if (string.IsNullOrEmpty(l1)) return null;

            var names = ExtractSceneNpcNames(l1);
            if (names == null || names.Count == 0) return null;

            var hints = BehaviorHintCalculator.ComputeAll(names, playerText ?? "");
            return BehaviorHintCalculator.FormatHints(hints);
        }

        /// <summary>
        /// 从 L1Stage 文本里粗略提取 [聚落内] 段的 NPC 名字。
        /// 期望格式: "· 张穆（郡司马）· 李四（主簿）" 或 "张穆、李四、王五"
        /// 提取 2-4 字中文名（紧跟 · 或 行首）。
        /// </summary>
        private static List<string> ExtractSceneNpcNames(string l1Stage)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(l1Stage)) return result;

            int start = l1Stage.IndexOf("[聚落内]");
            if (start < 0) start = l1Stage.IndexOf("聚落内");
            if (start < 0) return result;
            int end = l1Stage.IndexOf('[', start + 1);
            if (end < 0) end = l1Stage.Length;
            var section = l1Stage.Substring(start, end - start);

            var matches = System.Text.RegularExpressions.Regex.Matches(
                section, @"[·\s,，、]([\u4e00-\u9fa5]{2,4})(?=[（(、，,\s]|$)");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var name = m.Groups[1].Value;
                if (!result.Contains(name)) result.Add(name);
            }
            return result;
        }

        // Pull the required parameter list out of a tool's JSON Schema so the
        // model sees the exact field names it must provide.
        private static string ExtractRequiredParams(object schema)
        {
            if (schema == null) return "—";
            try
            {
                var jo = schema as Newtonsoft.Json.Linq.JObject
                       ?? Newtonsoft.Json.Linq.JObject.FromObject(schema);
                var req = jo["required"] as Newtonsoft.Json.Linq.JArray;
                if (req == null || req.Count == 0) return "（无）";
                return string.Join(", ", req.Select(x => "`" + (string)x + "`"));
            }
            catch { return "—"; }
        }

        private static IReadOnlyList<ChatMessage> EnsureMessages(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return new List<ChatMessage> { new ChatMessage(ChatRole.User, "开始") };
            return messages;
        }

        private async Task<LlmResponse> CompleteNarrationAndChoicesStream(string prompt,
            IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            SetThinking("");
            OnStreamingBegin?.Invoke();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log.Debug(Log.Channels.Llm, "[Timing] TextStream START t={0:F3}s", Time.realtimeSinceStartup);

            var raw = new StringBuilder();
            string lastShownNarration = "";
            messages = EnsureMessages(messages);

            var resp = await LlmClient.CompleteStreamAsync(prompt, messages, delta =>
            {
                if (string.IsNullOrEmpty(delta)) return;
                raw.Append(delta);
                string narration = NarrationChoicesTextParser.ExtractNarrationForStreaming(raw.ToString());
                if (narration != lastShownNarration)
                {
                    lastShownNarration = narration;
                    if (PlayerLoopHelper.IsMainThread)
                        OnNarrationDelta?.Invoke(narration);
                    else
                        UniTask.Post(() => OnNarrationDelta?.Invoke(narration));
                }
            }, ct, jsonObject: false);   // narrator emits plain text (--- + numbered choices), NOT json_object

            sw.Stop();
            lastResponseTime = (float)sw.Elapsed.TotalSeconds;
            OnResponseTime?.Invoke(lastResponseTime);
            string finalNarration = NarrationChoicesTextParser.ExtractNarrationForStreaming(raw.ToString());
            if (finalNarration != lastShownNarration)
            {
                if (PlayerLoopHelper.IsMainThread)
                    OnNarrationDelta?.Invoke(finalNarration);
                else
                    UniTask.Post(() => OnNarrationDelta?.Invoke(finalNarration));
            }
            OnStreamingEnd?.Invoke();
            Log.Debug(Log.Channels.Llm, "[Timing] TextStream END t={0:F3}s elapsed={1:F2}s rawLen={2}",
                Time.realtimeSinceStartup, sw.Elapsed.TotalSeconds, raw.Length);
            return resp;
        }

        private async Task<LlmResponse> CompleteEffectsOnly(string prompt,
            IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            messages = EnsureMessages(messages);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await LlmClient.CompleteAsync(prompt, messages, ct);
            sw.Stop();
            Log.Debug(Log.Channels.Llm, "[Timing] EffectsOnly END elapsed={0:F2}s rawLen={1}", sw.Elapsed.TotalSeconds, resp?.Content?.Length ?? 0);
            return resp;
        }

        private DialogueResult BuildNarrationTextResult(string rawResponse, string narration,
            List<LlmReply.Choice> choices, LlmReply effectsReply)
        {
            var result = new DialogueResult
            {
                RawResponse = rawResponse,
                Narration = narration ?? "",
                Choices = choices ?? new List<LlmReply.Choice>(),
                Thinking = "",
                Participants = effectsReply?.System?.SceneParticipants ?? new List<LlmReply.Participant>(),
                Effects = effectsReply?.System?.Effects ?? new List<LlmReply.EffectSpec>(),
                SuggestedNextState = effectsReply?.System?.SuggestedState ?? "",
                SkillTriggers = effectsReply?.System?.SkillTriggers ?? new List<string>(),
                DaysPassed = effectsReply?.DaysPassed ?? 0,
            };

            if (!string.IsNullOrWhiteSpace(effectsReply?.Date))
                result.Date = TryParseDateText(effectsReply.Date);

            return result;
        }

        private static bool ShouldRequestEffectsForOpening(NarrationChoicesTextParser.EffectTagState effectTag)
        {
            return effectTag != NarrationChoicesTextParser.EffectTagState.No;
        }

        private static bool ShouldRequestEffectsForStagePlay(string userInput,
            NarrationChoicesTextParser.EffectTagState effectTag)
        {
            bool localStrong = LocalEffectsHeuristic(userInput);

            if (effectTag == NarrationChoicesTextParser.EffectTagState.Yes)
                return true;
            if (effectTag == NarrationChoicesTextParser.EffectTagState.No && !localStrong)
                return false;
            return localStrong;
        }

        private static bool LocalEffectsHeuristic(string userInput)
        {
            string text = (userInput ?? "").Trim();
            if (text.Length == 0) return true;

            string[] strongKeywords =
            {
                "去","前往","出发","赶往","进入","离开","回到","潜入","跟上","追过去",
                "买","卖","给","拿","取","收下","交出","支付","赏","借","还",
                "打","杀","砍","刺","推","抓","绑","逃","躲","冲","拦","救","医","疗伤",
                "搜","查","翻","审","盘问","打听","偷听","观察","查看","检查","试探",
                "说服","劝","威胁","挑衅","道歉","求助","结交","拉拢","拒绝","表态",
                "等","休息","睡","过夜","明天","数日","赶路","停留",
                "记下","记住","告诉","隐瞒","承认","答应","立誓","约定"
            };

            for (int i = 0; i < strongKeywords.Length; i++)
            {
                if (text.Contains(strongKeywords[i]))
                    return true;
            }

            return text.Length >= 10;
        }

        private static Sim.GameDate? TryParseDateText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"(\d{4})年(\d{1,2})月(\d{1,2})日");
                if (m.Success)
                {
                    int y = int.Parse(m.Groups[1].Value);
                    int mo = int.Parse(m.Groups[2].Value);
                    int d = int.Parse(m.Groups[3].Value);
                    if (y < 100 || y > 2200 || mo < 1 || mo > 12 || d < 1 || d > 31)
                        return null;
                    return new Sim.GameDate(y, mo, d);
                }
            }
            catch { }
            return null;
        }

        private async Task<LlmResponse> CompleteStreamWithThinking(string prompt,
            IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            SetThinking("");
            OnStreamingBegin?.Invoke();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log.Debug(Log.Channels.Llm, "[Timing] Stream START t={0:F3}s", Time.realtimeSinceStartup);

            var raw = new StringBuilder();
            string last = "";
            int lastEmit = Environment.TickCount;
            string lastNarration = "";
            int lastNarrationEmit = Environment.TickCount;

            messages = EnsureMessages(messages);

            var resp = await LlmClient.CompleteStreamAsync(prompt, messages, delta =>
            {
                if (string.IsNullOrEmpty(delta)) return;
                raw.Append(delta);
                if (TryExtractThinking(raw, out var t) && t != last)
                {
                    last = t;
                    int now = Environment.TickCount;
                    lastEmit = now;
                    SetThinking(t);
                }
                if (TryExtractNarration(raw, out var n) && n != lastNarration)
                {
                    lastNarration = n;
                    int now = Environment.TickCount;
                    // 不节流：每个 delta 立即推。LLM 一次调用 delta 总数有限（200-500），
                    // 推一次只赋值一个字符串，性能可接受。
                    lastNarrationEmit = now;
                    //Log.Info(Log.Channels.Llm, "[NarDelta] len={0} preview={1}", n.Length, Truncate(n, 80));
                    if (PlayerLoopHelper.IsMainThread)
                        OnNarrationDelta?.Invoke(n);
                    else
                        UniTask.Post(() => OnNarrationDelta?.Invoke(n));
                }
            }, ct);

            sw.Stop();
            lastResponseTime = (float)sw.Elapsed.TotalSeconds;
            OnResponseTime?.Invoke(lastResponseTime);

            // ── 流结束后，发出最后一次完整 narration delta（确保打字机看到完整文本） ──
            EmitFinalNarrationDelta(raw);
            OnStreamingEnd?.Invoke();
            Log.Debug(Log.Channels.Llm, "[Timing] Stream END t={0:F3}s elapsed={1:F2}s rawLen={2}",
                Time.realtimeSinceStartup, sw.Elapsed.TotalSeconds, raw.Length);

            // 日志输出 LLM 流式响应汇总
            Log.Info(Log.Channels.Llm, "[OK] 收到完整流式响应 | 长度={0} ↑{1}↓{2}", raw.Length, resp.Usage.InputTokens, resp.Usage.OutputTokens);

            // 日志输出 thinking + 回复内容（太长时只输出摘要，避免刷屏）
            if (TryExtractThinking(raw, out var finalThinking) && !string.IsNullOrWhiteSpace(finalThinking))
                Log.InfoColored(Log.Channels.Llm, Log.Colors.LlmIO, "[Think] len={0} preview={1}", finalThinking.Length, Truncate(finalThinking, 200));
            Log.InfoColored(Log.Channels.Llm, Log.Colors.LlmIO, "[Resp] len={0} preview={1}", raw.Length, Truncate(raw.ToString(), 200));

            // Record in debug panel (always on main thread).
            string reqJson = $"{{\"model\":\"...\",\"system\":{EscapeJson(prompt)},\"messages\":[{string.Join(",", messages.Select(m => $"{{\"role\":\"{m.Role}\",\"content\":{EscapeJson(m.Content)}}}"))}]}}";
            LlmDebugLog.Add(GetProviderLabel(), reqJson, resp.Content);

            return resp;
        }

        /// <summary>
        /// 带工具调用支持的流式请求。自动处理 tool_calling 循环：
        /// 如果 LLM 返回 tool_use，执行工具后将结果发回 LLM，直到获得纯文本回复。
        /// 中间的工具交互对调用方透明。
        /// </summary>
        private async Task<LlmResponse> CompleteStreamWithTools(string prompt,
            IReadOnlyList<ChatMessage> historyMessages, CancellationToken ct)
        {
            SetThinking("");

            OnStreamingBegin?.Invoke();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log.Debug(Log.Channels.Llm, "[Timing] ToolStream START t={0:F3}s promptLen={1}", Time.realtimeSinceStartup, prompt?.Length ?? 0);
            var tools = ToolRegistry.GetAllDefinitions();
            Log.Info(Log.Channels.Llm, "[Tool] 可用工具({0}个): {1}",
                tools.Count, string.Join(", ", tools.Select(t => t.Name)));

            var workingMessages = new List<ChatMessage>(historyMessages);
            var raw = new StringBuilder();
            string last = "";
            int lastEmit = Environment.TickCount;
            string lastNarration = "";

            int loopCount = 0;
            const int maxToolLoops = 10;

            while (loopCount < maxToolLoops)
            {
                loopCount++;
                raw.Clear();
                last = "";
                lastNarration = "";

                // 日志：本轮发送的消息列表（含已累积的工具调用/结果）
                if (workingMessages.Count > 1)
                {
                    Log.Info(Log.Channels.Llm, "[Tool Msgs] 发送 {0} 条消息 (loop#{1})", workingMessages.Count, loopCount);
                    for (int mi = 0; mi < workingMessages.Count; mi++)
                    {
                        var m = workingMessages[mi];
                        string preview = m.Content;
                        if (preview != null && preview.Length > 120) preview = preview.Substring(0, 120) + "…";
                        Log.Info(Log.Channels.Llm, "  [{0}] {1}: {2}", mi, m.Role, preview);
                    }
                }

                var resp = await LlmClient.CompleteStreamWithToolsAsync(
                    prompt, workingMessages, tools, delta =>
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
                        if (TryExtractNarration(raw, out var n) && n != lastNarration)
                        {
                            lastNarration = n;
                        }
                    }, ct);

                TrackUsage(resp.Usage);

                if (!resp.HasToolCalls)
                {
                    // 纯文本回复——完成
                    Log.Info(Log.Channels.Llm, "[OK] 工具循环完成 (共{0}轮) | 长度={1} ↑{2}↓{3}",
                        loopCount, raw.Length, resp.Usage.InputTokens, resp.Usage.OutputTokens);

                    sw.Stop();
                    lastResponseTime = (float)sw.Elapsed.TotalSeconds;
                    OnResponseTime?.Invoke(lastResponseTime);
                    Log.Debug(Log.Channels.Llm, "[Timing] ToolStream END t={0:F3}s elapsed={1:F2}s loops={2} rawLen={3}",
                        Time.realtimeSinceStartup, sw.Elapsed.TotalSeconds, loopCount, raw.Length);

                    // ── 发出最后一次完整 narration delta（确保打字机看到完整文本） ──
                    EmitFinalNarrationDelta(raw);
                    OnStreamingEnd?.Invoke();

                    resp.Content = raw.ToString();

                    // 日志输出 thinking + 回复内容（太长时只输出摘要）
                    if (TryExtractThinking(raw, out var finalThinking) && !string.IsNullOrWhiteSpace(finalThinking))
                        Log.InfoColored(Log.Channels.Llm, Log.Colors.LlmIO, "[Think] len={0} preview={1}", finalThinking.Length, Truncate(finalThinking, 200));
                    Log.InfoColored(Log.Channels.Llm, Log.Colors.LlmIO, "[Resp] len={0} preview={1}", raw.Length, Truncate(raw.ToString(), 200));

                    return resp;
                }

                // 有工具调用：执行每个工具，将结果加入工作消息列表
                Log.Info(Log.Channels.Llm, "[Tool] 工具循环第{0}轮: {1}个工具调用",
                    loopCount, resp.ToolCalls.Count);
                foreach (var tc in resp.ToolCalls)
                {
                    Log.Info(Log.Channels.Llm, "   LLM 请求工具: {0}", tc.Name);
                }

                // 1. 添加 assistant 文本 + tool_use 到工作列表
                if (raw.Length > 0)
                {
                    workingMessages.Add(new ChatMessage(ChatRole.Assistant, raw.ToString()));
                }

                foreach (var tc in resp.ToolCalls)
                {
                    workingMessages.Add(ChatMessage.ForToolCall(tc.Id, tc.Name, tc.Args));
                    // Surface each tool request to the UI before execution. Result
                    // is empty here; OnToolActivity will fire again after the
                    // tool runs to deliver the result.
                    OnToolActivity?.Invoke(loopCount, tc.Name, tc.Args ?? "{}", "");
                }

                // 2. 执行每个工具
                foreach (var tc in resp.ToolCalls)
                {
                    var tool = ToolRegistry.Get(tc.Name);
                    if (tool == null)
                    {
                        Log.Error(Log.Channels.Llm, "[ERR] 未知工具: {0} | 可用工具: {1}",
                            tc.Name, string.Join(", ", tools.Select(t => t.Name)));
                        var errResult = $"错误：未知工具「{tc.Name}」，可用工具: {string.Join(", ", tools.Select(t => t.Name))}";
                        workingMessages.Add(ChatMessage.ForToolResult(tc.Id, errResult));
                        OnToolActivity?.Invoke(loopCount, tc.Name, tc.Args ?? "{}", errResult);
                        continue;
                    }

                    Log.Info(Log.Channels.Llm, "▶ 执行工具: {0} | args={1}", tc.Name, tc.Args ?? "{}");
                    string result;
                    try
                    {
                        result = await tool.ExecuteAsync(tc.Args, ct);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(Log.Channels.Llm, "[ERR] 工具执行失败 {0}: {1}", tc.Name, ex.Message);
                        result = $"工具执行出错：{ex.Message}";
                    }

                    Log.Info(Log.Channels.Llm, "◀ 工具结果: {0} | result={1}", tc.Name, result ?? "(空)");
                    workingMessages.Add(ChatMessage.ForToolResult(tc.Id, result));
                    OnToolActivity?.Invoke(loopCount, tc.Name, tc.Args ?? "{}", result ?? "");
                }

                // 重置累积文本，继续循环
                raw.Clear();
            }

            // 超过最大循环次数，返回最后累积的文本
            Log.Warn(Log.Channels.Llm, "[WARN] 工具循环达到上限({0}轮)，强制返回 | 内容长度={1}", maxToolLoops, raw.Length);
            if (raw.Length > 0)
            {
                if (TryExtractThinking(raw, out var finalThinking) && !string.IsNullOrWhiteSpace(finalThinking))
                    Log.InfoColored(Log.Channels.Llm, Log.Colors.LlmIO, "[Think] len={0} preview={1}", finalThinking.Length, Truncate(finalThinking, 200));
                Log.InfoColored(Log.Channels.Llm, Log.Colors.LlmIO, "[Resp] len={0} preview={1}", raw.Length, Truncate(raw.ToString(), 200));
            }
            sw.Stop();
            lastResponseTime = (float)sw.Elapsed.TotalSeconds;
            OnResponseTime?.Invoke(lastResponseTime);
            return new LlmResponse { Content = raw.ToString() };
        }

        private string GetProviderLabel()
        {
            string name = LlmClient?.GetType().Name ?? "?";
            return name switch
            {
                "AnthropicClient" => "Anthropic",
                "OpenAiCompatibleClient" => "OpenAI",
                _ => name,
            };
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }

        private static bool TryExtractJsonStringField(StringBuilder raw, string fieldName, out string value)
        {
            value = "";
            if (raw == null || raw.Length == 0) return false;

            string s = raw.ToString();
            string key = "\"" + fieldName + "\"";
            int idx = s.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return false;

            int i = idx + key.Length;
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
                if (c == '"') { value = sb.ToString(); return true; }
                sb.Append(c);
            }

            value = sb.ToString();
            return true;
        }

        private static bool TryExtractThinking(StringBuilder raw, out string thinking)
        {
            // 尝试新版字段名 th，回退到旧版 thinking
            if (TryExtractJsonStringField(raw, "th", out thinking)) return true;
            return TryExtractJsonStringField(raw, "thinking", out thinking);
        }

        private static bool TryExtractNarration(StringBuilder raw, out string narration)
        {
            // 尝试新版字段名 nar，回退到旧版 narration
            if (TryExtractJsonStringField(raw, "nar", out narration)) return true;
            return TryExtractJsonStringField(raw, "narration", out narration);
        }

        /// <summary>
        /// 流结束后发出最后一次完整的 narration delta，确保打字机效果始终看到完整文本。
        /// </summary>
        private void EmitFinalNarrationDelta(StringBuilder raw)
        {
            if (raw == null || raw.Length == 0) return;
            if (!TryExtractNarration(raw, out var finalNarration)) return;
            if (string.IsNullOrWhiteSpace(finalNarration)) return;

            if (PlayerLoopHelper.IsMainThread)
                OnNarrationDelta?.Invoke(finalNarration);
            else
                UniTask.Post(() => OnNarrationDelta?.Invoke(finalNarration));
        }

        private void HandleResult(DialogueState state, DialogueResult result)
        {
            if (result.IsError)
            {
                Log.Error(Log.Channels.Llm, "HandleResult 收到错误: {0}", result.ErrorMessage);
                OnError?.Invoke(result.ErrorMessage);
                return;
            }

            // 1. Apply system effects.
            state.ApplyEffects(result);

            // 2. Publish user-facing content.
            SetThinking(result.Thinking ?? "");

            var effectSummary = string.Join(", ", result.Effects.Select(e => $"{e.Kind}({e.Stat ?? e.Trait ?? e.Flag ?? e.Target ?? "?"})"));
            Log.Info(Log.Channels.Llm,
                "▶ HandleResult: state={0}"
                + " | narration长度={1}"
                + " | choices={2}"
                + " | participants={3}"
                + " | effects=[{4}]"
                + " | suggestedNext={5}"
                + " | daysPassed={6}",
                state.StateType,
                result.Narration?.Length ?? 0,
                result.Choices.Count,
                result.Participants.Count,
                effectSummary,
                result.SuggestedNextState ?? "(无)",
                result.DaysPassed);

            PublishNarration(result.Narration);

            OnParticipants?.Invoke(result.Participants);
            OnChoices?.Invoke(result.Choices);
            OnEffects?.Invoke(result.Effects);

            // 3. Check for state transition.
            var nextType = state.GetNextState(result);
            if (nextType.HasValue && nextType.Value != state.StateType)
            {
                Log.Info(Log.Channels.Llm, "[Trans] 状态转换: {0} → {1}", state.StateType, nextType.Value);
                TransitionTo(nextType.Value);
            }
        }

        private void TrackUsage(LlmUsage usage)
        {
            cumulativeUsage = cumulativeUsage + usage;
            callCount++;
            OnUsage?.Invoke(usage, cumulativeUsage);
        }

        private void TrackUsageSilent(LlmUsage usage)
        {
            cumulativeUsage = cumulativeUsage + usage;
            callCount++;
        }

        private void SetBusy(bool v)
        {
            if (Busy == v) return;
            Busy = v;
            OnBusyChanged?.Invoke(v);
        }

        // World.Date 从 LLM 拿到后调用：用 Character.InitialAge 反推出生日。
        // 玩家角色被创建时 Birth=default(GameDate)，此处回填。
        private void BackfillCharacterBirths()
        {
            if (World == null || World.Date.Year <= 0) return;
            int y = World.Date.Year;
            int mo = World.Date.Month;
            int d = World.Date.Day;
            for (int i = 0; i < World.CharacterList.Count; i++)
            {
                var c = World.CharacterList[i];
                if (c.InitialAge <= 0) continue;
                c.Birth = new Sim.GameDate(y - c.InitialAge, mo, d);
            }
        }

        private static string Truncate(string s, int max) =>
            s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";

        // ════════════════════════════════════════════════════
        //  WorldPipeline 辅助方法
        // ════════════════════════════════════════════════════

        private static string LoadFragment(string name)
        {
            var asset = ContentLoader.LoadSync<TextAsset>(
                $"Assets/Content/Prompts/{name}.md", $"Prompts/{name}");
            return asset != null ? asset.text.Trim() : "";
        }

        /// <summary>为 Step 6 L1 构建紧凑实体摘要（~500-800 token），替代全量前序上下文。</summary>
        private static string BuildStep6EntitySummary(
            WorldBuildStepReply worldBuild, PlayerExpandReply playerExpand, string playerInfo)
        {
            var sb = new StringBuilder();

            // ── 世界观概要（2-3 行） ──
            if (worldBuild != null)
            {
                sb.AppendLine("【世界观概要】");
                if (!string.IsNullOrWhiteSpace(worldBuild.EraName))
                    sb.AppendLine($"时代：{worldBuild.EraName}");
                if (!string.IsNullOrWhiteSpace(worldBuild.NarrativeStyle))
                    sb.AppendLine($"叙事风格：{worldBuild.NarrativeStyle}");
                if (!string.IsNullOrWhiteSpace(worldBuild.WorldUndertones))
                    sb.AppendLine($"世界底色：{worldBuild.WorldUndertones}");
                if (!string.IsNullOrWhiteSpace(worldBuild.WorldSummary))
                    sb.AppendLine($"摘要：{worldBuild.WorldSummary}");
                sb.AppendLine();
            }

            // ── 势力（来自 GameMemory） ──
            var gm = GameMemory.Instance;
            if (gm.Factions.Count > 0)
            {
                sb.AppendLine("【已知势力】");
                foreach (var f in gm.Factions)
                {
                    sb.Append($"· {f.name}");
                    if (!string.IsNullOrWhiteSpace(f.type)) sb.Append($" [{f.type}]");
                    sb.AppendLine($": {f.stance}");
                }
                sb.AppendLine();
            }

            // ── 人物（L2 chars + 丰富化数据） ──
            if (gm.Chars.Count > 0)
            {
                sb.AppendLine("【主要人物】");
                foreach (var c in gm.Chars)
                {
                    sb.AppendLine($"· {c.name}，{c.role}，关系{c.relation:+0;-0}，{c.location}（约{c.reachable_in_days}天可达）");
                    if (!string.IsNullOrWhiteSpace(c.background_story))
                        sb.AppendLine($"  背景：{c.background_story}");
                    if (!string.IsNullOrWhiteSpace(c.values))
                        sb.AppendLine($"  价值观：{c.values}");
                    if (!string.IsNullOrWhiteSpace(c.current_goal))
                        sb.AppendLine($"  当前目标：{c.current_goal}");
                    if (!string.IsNullOrWhiteSpace(c.secret))
                        sb.AppendLine($"  秘密：{c.secret}");
                    if (!string.IsNullOrWhiteSpace(c.father) || !string.IsNullOrWhiteSpace(c.mother))
                        sb.AppendLine($"  父母：{c.father} {c.mother}");
                    if (c.siblings.Length > 0)
                        sb.AppendLine($"  兄弟姐妹：{string.Join("、", c.siblings)}");
                    if (c.core_friends.Length > 0)
                        sb.AppendLine($"  核心朋友：{string.Join("、", c.core_friends)}");
                }
                sb.AppendLine();
            }

            // ── 地点 ──
            if (gm.Places.Count > 0)
            {
                sb.AppendLine("【已知地点】");
                foreach (var p in gm.Places)
                {
                    sb.Append($"· {p.name}");
                    if (!string.IsNullOrWhiteSpace(p.type)) sb.Append($" [{p.type}]");
                    if (!string.IsNullOrWhiteSpace(p.description)) sb.Append($": {p.description}");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // ── 故事线 ──
            if (playerExpand != null && playerExpand.Storylines.Count > 0)
            {
                sb.AppendLine("【活跃故事线】");
                foreach (var sl in playerExpand.Storylines)
                {
                    sb.AppendLine($"· {sl.Title}");
                    if (!string.IsNullOrWhiteSpace(sl.Summary))
                        sb.AppendLine($"  概述：{sl.Summary}");
                    if (!string.IsNullOrWhiteSpace(sl.Hook))
                        sb.AppendLine($"  切入点：{sl.Hook}");
                    if (sl.InvolvedNpcs.Count > 0)
                        sb.AppendLine($"  涉及人物：{string.Join("、", sl.InvolvedNpcs)}");
                    if (sl.InvolvedLocations.Count > 0)
                        sb.AppendLine($"  涉及地点：{string.Join("、", sl.InvolvedLocations)}");
                    if (!string.IsNullOrWhiteSpace(sl.KeyTimePoint))
                        sb.AppendLine($"  关键时间：{sl.KeyTimePoint}");
                }
                sb.AppendLine();
            }

            // ── 主角 ──
            if (!string.IsNullOrWhiteSpace(playerInfo))
            {
                sb.AppendLine("【主角】");
                sb.AppendLine(playerInfo);
            }

            return sb.ToString().Trim();
        }

        /// <summary>解析 L4 构建的 LLM 回复。</summary>
        private static (bool ok, string text, Entities entities, string narration) ParseL4Reply(string raw)
        {
            string text = SanitizeJson(raw);
            int open = text.IndexOf('{');
            int close = text.LastIndexOf('}');
            if (open < 0 || close <= open) return (false, "", null, "");

            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(text.Substring(open, close - open + 1));
                var sb = new System.Text.StringBuilder();
                string narration = ((string)obj["nar"] ?? "").Trim();

                var entities = ExtractEntities(obj);

                // 将 macro_factions 合并到 entities.Factions
                if (obj["mf"] is Newtonsoft.Json.Linq.JArray macroFactions)
                {
                    foreach (var f in macroFactions)
                    {
                        string n = ((string)f["n"] ?? "").Trim();
                        string s = ((string)f["st"] ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(n))
                        {
                            sb.AppendLine($"· {n}：{s}");
                            entities.Factions.Add((n, "macro", s));
                        }
                    }
                }

                string summary = (string)obj["su"] ?? "";
                if (!string.IsNullOrWhiteSpace(summary)) sb.AppendLine(summary);

                return (sb.Length > 0, sb.ToString().Trim(), entities, narration);
            }
            catch
            {
                return (false, "", null, "");
            }
        }

        /// <summary>解析 L3 构建的 LLM 回复。</summary>
        private static (bool ok, string text, Entities entities, string narration) ParseL3Reply(string raw)
        {
            string text = SanitizeJson(raw);
            int open = text.IndexOf('{');
            int close = text.LastIndexOf('}');
            if (open < 0 || close <= open)
            {
                ACLS.Logging.Log.Error(ACLS.Logging.Log.Channels.Llm,
                    "ParseL3Reply: missing braces open={0} close={1} rawLen={2} first120={3}",
                    open, close, text.Length,
                    text.Length > 120 ? text.Substring(0, 120) : text);
                return (false, "", null, "");
            }

            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(text.Substring(open, close - open + 1));
                var sb = new System.Text.StringBuilder();
                string narration = ((string)obj["nar"] ?? "").Trim();

                var entities = ExtractEntities(obj);

                string region = (string)obj["reg"] ?? "";
                if (!string.IsNullOrWhiteSpace(region)) sb.AppendLine(region);

                // 将 regional_powers 合并到 entities.Factions
                if (obj["rp"] is Newtonsoft.Json.Linq.JArray powers)
                {
                    foreach (var p in powers)
                    {
                        string n = ((string)p["n"] ?? "").Trim();
                        string st = ((string)p["st"] ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(n))
                        {
                            sb.AppendLine($"· {n}：{st}");
                            entities.Factions.Add((n, "regional", st));
                        }
                    }
                }

                string tensions = (string)obj["rt"] ?? "";
                if (!string.IsNullOrWhiteSpace(tensions)) sb.AppendLine(tensions);
                string summary = (string)obj["su"] ?? "";
                if (!string.IsNullOrWhiteSpace(summary)) sb.AppendLine(summary);

                if (sb.Length == 0)
                {
                    ACLS.Logging.Log.Error(ACLS.Logging.Log.Channels.Llm,
                        "ParseL3Reply: sb empty after parse | reg='{0}' rpCount={1} rt='{2}' su='{3}' narLen={4}",
                        region,
                        (obj["rp"] as Newtonsoft.Json.Linq.JArray)?.Count ?? 0,
                        tensions.Length, summary.Length, narration.Length);
                }

                return (sb.Length > 0, sb.ToString().Trim(), entities, narration);
            }
            catch (System.Exception ex)
            {
                ACLS.Logging.Log.Error(ACLS.Logging.Log.Channels.Llm,
                    "ParseL3Reply: JObject.Parse threw: {0}\n---raw(0..400)---\n{1}\n---raw(end 200)---\n{2}",
                    ex.Message,
                    text.Length > 400 ? text.Substring(0, 400) : text,
                    text.Length > 200 ? text.Substring(text.Length - 200) : "");
                return (false, "", null, "");
            }
        }

        /// <summary>实体数据容器（chars/factions/places）。所有层共用此类型。</summary>
        private sealed class Entities
        {
            public List<(string name, string role, string location, int relation, int reachable_in_days,
                string father, string mother, string[] siblings, string[] other_relatives, string[] core_friends,
                bool is_important)> Chars = new();
            public List<(string name, string type, string stance)> Factions = new();
            public List<(string name, string type, string description)> Places = new();
        }

        /// <summary>从 JSON 对象中提取通用的 chars/factions/places 字段。</summary>
        private static Entities ExtractEntities(Newtonsoft.Json.Linq.JObject obj)
        {
            var result = new Entities();
            if (obj == null) return result;

            // chars
            if (obj["chars"] is Newtonsoft.Json.Linq.JArray chars)
            {
                foreach (var c in chars)
                {
                    string n = ((string)c["n"] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    result.Chars.Add((
                        n,
                        ((string)c["r"] ?? "").Trim(),
                        ((string)c["loc"] ?? "").Trim(),
                        c["rel"]?.ToObject<int>() ?? 0,
                        c["rid"]?.ToObject<int>() ?? 0,
                        ((string)c["fa"] ?? "").Trim(),
                        ((string)c["mo"] ?? "").Trim(),
                        c["sib"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                        c["orl"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                        c["cfr"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                        c["imp"]?.ToObject<bool>() ?? false
                    ));
                }
            }

            // factions
            if (obj["fac"] is Newtonsoft.Json.Linq.JArray factions)
            {
                foreach (var f in factions)
                {
                    string n = ((string)f["n"] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    result.Factions.Add((
                        n,
                        ((string)f["tp"] ?? "").Trim(),
                        ((string)f["st"] ?? "").Trim()
                    ));
                }
            }

            // places
            if (obj["pl"] is Newtonsoft.Json.Linq.JArray places)
            {
                foreach (var p in places)
                {
                    string n = ((string)p["n"] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    result.Places.Add((
                        n,
                        ((string)p["tp"] ?? "").Trim(),
                        ((string)p["desc"] ?? "").Trim()
                    ));
                }
            }

            return result;
        }

        /// <summary>将 Entities 中的实体注册到 GameMemory。</summary>
        private static void RegisterEntities(Entities entities)
        {
            if (entities == null) return;
            foreach (var c in entities.Chars)
                GameMemory.Instance.AddChar(new CharEntry
                {
                    name = c.name,
                    role = c.role,
                    location = c.location,
                    relation = c.relation,
                    reachable_in_days = c.reachable_in_days,
                    father = c.father,
                    mother = c.mother,
                    siblings = c.siblings,
                    other_relatives = c.other_relatives,
                    core_friends = c.core_friends,
                    is_important = c.is_important,
                });
            foreach (var f in entities.Factions)
                GameMemory.Instance.AddFaction(new FactionEntry { name = f.name, type = f.type, stance = f.stance });
            foreach (var p in entities.Places)
                GameMemory.Instance.AddPlace(new PlaceEntry { name = p.name, type = p.type, description = p.description });
        }

        /// <summary>解析 L2 构建的 LLM 回复。</summary>
        private static (bool ok, string text, Entities entities, string narration) ParseL2Reply(string raw)
        {
            string text = SanitizeJson(raw);
            int open = text.IndexOf('{');
            int close = text.LastIndexOf('}');
            if (open < 0 || close <= open) return (false, "", null, "");

            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(text.Substring(open, close - open + 1));
                var sb = new System.Text.StringBuilder();
                var result = new Entities();
                string narration = ((string)obj["nar"] ?? "").Trim();

                // Chars
                if (obj["chars"] is Newtonsoft.Json.Linq.JArray chars)
                {
                    sb.AppendLine("【人物】");
                    foreach (var c in chars)
                    {
                        string n = ((string)c["n"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        string r = ((string)c["r"] ?? "").Trim();
                        string l = ((string)c["loc"] ?? "").Trim();
                        int rel = c["rel"]?.ToObject<int>() ?? 0;
                        int days = c["rid"]?.ToObject<int>() ?? 0;
                        string father = ((string)c["fa"] ?? "").Trim();
                        string mother = ((string)c["mo"] ?? "").Trim();
                        var siblings = c["sib"]?.ToObject<string[]>() ?? Array.Empty<string>();
                        var otherRelatives = c["orl"]?.ToObject<string[]>() ?? Array.Empty<string>();
                        var coreFriends = c["cfr"]?.ToObject<string[]>() ?? Array.Empty<string>();
                        bool important = c["imp"]?.ToObject<bool>() ?? false;
                        sb.AppendLine($"· {n}（{r}，{l}，关系{rel:+#;-#;0}，约{days}天" +
                            $"{(important ? "【重要】" : "")}）");
                        result.Chars.Add((n, r, l, rel, days, father, mother, siblings, otherRelatives, coreFriends, important));
                    }
                }

                // Factions
                if (obj["fac"] is Newtonsoft.Json.Linq.JArray factions)
                {
                    sb.AppendLine("【势力】");
                    foreach (var f in factions)
                    {
                        string n = ((string)f["n"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        string t = ((string)f["tp"] ?? "").Trim();
                        string st = ((string)f["st"] ?? "").Trim();
                        sb.AppendLine($"▸ {n}（{t}）：{st}");
                        result.Factions.Add((n, t, st));
                    }
                }

                // Places
                if (obj["pl"] is Newtonsoft.Json.Linq.JArray places)
                {
                    sb.AppendLine("【地点】");
                    foreach (var p in places)
                    {
                        string n = ((string)p["n"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        string t = ((string)p["tp"] ?? "").Trim();
                        string d = ((string)p["desc"] ?? "").Trim();
                        sb.AppendLine($"· {n}（{t}）：{d}");
                        result.Places.Add((n, t, d));
                    }
                }

                return (result.Chars.Count > 0 || result.Factions.Count > 0 || result.Places.Count > 0,
                    sb.ToString().Trim(), result, narration);
            }
            catch
            {
                return (false, "", null, "");
            }
        }

        /// <summary>解析 L1 构建的 LLM 回复（仅场景文本）。兼容顶层字段和 l1_stage 嵌套对象。</summary>
        private static (bool ok, string text, string narration) ParseL1Reply(string raw)
        {
            string text = SanitizeJson(raw);
            int open = text.IndexOf('{');
            int close = text.LastIndexOf('}');
            if (open < 0 || close <= open)
            {
                Log.Warn(Log.Channels.Llm, "[L1] 未找到 JSON 大括号，raw长度={0}", raw?.Length ?? 0);
                Log.Debug(Log.Channels.Llm, "[L1] raw={0}", Truncate(raw ?? "", 300));
                return (false, "", "");
            }

            Newtonsoft.Json.Linq.JObject obj;
            try { obj = Newtonsoft.Json.Linq.JObject.Parse(text.Substring(open, close - open + 1)); }
            catch (System.Exception ex)
            {
                Log.Warn(Log.Channels.Llm, "[L1] JSON 解析异常: {0}", ex.Message);
                Log.Debug(Log.Channels.Llm, "[L1] json文本前300字={0}", Truncate(text.Substring(open, Math.Min(300, close - open + 1)), 300));
                return (false, "", "");
            }

            var sb = new System.Text.StringBuilder();
            string narration = ((string)obj["nar"] ?? "").Trim();

            // 支持两种格式：顶层字段，或嵌套在 l1_stage / l1s 内部（兼容旧字段名）
            var l1 = (obj["l1s"] ?? obj["l1_stage"]) as Newtonsoft.Json.Linq.JObject;
            string location = l1 != null
                ? ((string)(l1["loc"] ?? l1["location"]) ?? "").Trim()
                : ((string)(obj["loc"] ?? obj["location"]) ?? "").Trim();
            string scene = l1 != null
                ? ((string)(l1["sd"] ?? l1["scene_description"]) ?? "").Trim()
                : ((string)(obj["sd"] ?? obj["scene_description"]) ?? "").Trim();
            string situation = l1 != null
                ? ((string)(l1["is"] ?? l1["immediate_situation"]) ?? "").Trim()
                : ((string)(obj["is"] ?? obj["immediate_situation"]) ?? "").Trim();

            Log.Debug(Log.Channels.Llm, "[L1] 解析: location=[{0}] scene长度={1} situation长度={2}",
                location, scene.Length, situation.Length);

            if (!string.IsNullOrWhiteSpace(location)) sb.AppendLine($"[所在] {location}");
            if (!string.IsNullOrWhiteSpace(scene)) sb.AppendLine(scene);

            // 时辰 (tod) — 在 NPC 列表之前
            var tod = (l1?["tod"] ?? l1?["time_of_day"]) as Newtonsoft.Json.Linq.JObject;
            if (tod != null)
            {
                string shichen = ((string)(tod["sh"] ?? tod["shichen"]) ?? "").Trim();
                string common = ((string)(tod["cn"] ?? tod["common_name"]) ?? "").Trim();
                string ls = ((string)(tod["ls"] ?? tod["light_state"]) ?? "").Trim();
                string rm = ((string)(tod["rm"] ?? tod["reminders"]) ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(shichen) || !string.IsNullOrWhiteSpace(common) || !string.IsNullOrWhiteSpace(ls) || !string.IsNullOrWhiteSpace(rm))
                {
                    sb.Append("[时辰] ");
                    if (!string.IsNullOrWhiteSpace(shichen)) sb.Append(shichen);
                    if (!string.IsNullOrWhiteSpace(common)) sb.Append($"（{common}）");
                    if (!string.IsNullOrWhiteSpace(ls)) sb.Append($"·{ls}");
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(rm)) sb.AppendLine($"  提示：{rm}");
                }
            }

            // 气候 (cli) — 在 NPC 列表之前
            var cli = (l1?["cli"] ?? l1?["climate"]) as Newtonsoft.Json.Linq.JObject;
            if (cli != null)
            {
                string w = ((string)(cli["w"] ?? cli["weather"]) ?? "").Trim();
                string light = ((string)(cli["l"] ?? cli["light"]) ?? "").Trim();
                string wd = ((string)(cli["wd"] ?? cli["wind"]) ?? "").Trim();
                string tmp = ((string)(cli["tmp"] ?? cli["temperature"]) ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(w) || !string.IsNullOrWhiteSpace(light) || !string.IsNullOrWhiteSpace(wd) || !string.IsNullOrWhiteSpace(tmp))
                {
                    sb.Append("[气候] ");
                    var parts = new System.Collections.Generic.List<string>();
                    if (!string.IsNullOrWhiteSpace(w)) parts.Add(w);
                    if (!string.IsNullOrWhiteSpace(light)) parts.Add(light);
                    if (!string.IsNullOrWhiteSpace(wd)) parts.Add(wd);
                    if (!string.IsNullOrWhiteSpace(tmp)) parts.Add(tmp);
                    sb.AppendLine(string.Join("·", parts));
                }
            }

            var npcs = (l1?["an"] ?? l1?["active_npcs"]) as Newtonsoft.Json.Linq.JArray
                ?? (obj["an"] ?? obj["active_npcs"]) as Newtonsoft.Json.Linq.JArray;
            if (npcs != null)
            {
                sb.AppendLine("[聚落内]");
                foreach (var n in npcs)
                {
                    string name = ((string)(n["n"] ?? n["name"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string role = ((string)(n["r"] ?? n["role"]) ?? "").Trim();
                    string stance = ((string)(n["st"] ?? n["stance"]) ?? "").Trim();
                    int rel = (n["rv"] ?? n["relation_value"])?.ToObject<int>() ?? 0;
                    sb.AppendLine($"· {name}（{role}，关系{rel:+#;-#;0}）：{stance}");
                }
            }

            // 逻辑关联 NPC (lrn) — 在聚落内之后
            var lrn = (l1?["lrn"] ?? l1?["logic_related_npcs"]) as Newtonsoft.Json.Linq.JArray
                ?? (obj["lrn"] ?? obj["logic_related_npcs"]) as Newtonsoft.Json.Linq.JArray;
            if (lrn != null)
            {
                sb.AppendLine("[逻辑关联]");
                foreach (var n in lrn)
                {
                    string name = ((string)(n["n"] ?? n["name"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string loc = ((string)(n["loc"] ?? n["location"]) ?? "").Trim();
                    string rt = ((string)(n["rt"] ?? n["relation_type"]) ?? "").Trim();
                    string ca = ((string)(n["ca"] ?? n["current_action"]) ?? "").Trim();
                    int rel = (n["rv"] ?? n["relation_value"])?.ToObject<int>() ?? 0;
                    sb.AppendLine($"· {name}（{loc}，{rt}，关系{rel:+#;-#;0}）：{ca}");
                }
            }

            // 故事线 (sln) — 在 NPC 之后
            var sln = (l1?["sln"] ?? l1?["storylines"]) as Newtonsoft.Json.Linq.JArray
                ?? (obj["sln"] ?? obj["storylines"]) as Newtonsoft.Json.Linq.JArray;
            if (sln != null)
            {
                sb.AppendLine("[活跃故事线]");
                foreach (var s in sln)
                {
                    string ti = ((string)(s["ti"] ?? s["title"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(ti)) continue;
                    string st = ((string)(s["st"] ?? s["status"]) ?? "").Trim();
                    string cs = ((string)(s["cs"] ?? s["current_state"]) ?? "").Trim();
                    string nb = ((string)(s["nb"] ?? s["next_beat"]) ?? "").Trim();
                    var inps = s["inp"] ?? s["involved_npcs"];
                    var inpList = new System.Collections.Generic.List<string>();
                    if (inps is Newtonsoft.Json.Linq.JArray inpArr)
                        foreach (var x in inpArr) { string xs = ((string)x ?? "").Trim(); if (!string.IsNullOrWhiteSpace(xs)) inpList.Add(xs); }
                    sb.AppendLine($"· {st} {ti}");
                    if (!string.IsNullOrWhiteSpace(cs)) sb.AppendLine($"  当前：{cs}");
                    if (!string.IsNullOrWhiteSpace(nb)) sb.AppendLine($"  下一拍：{nb}");
                    if (inpList.Count > 0) sb.AppendLine($"  涉及人物：{string.Join("、", inpList)}");
                }
            }

            if (!string.IsNullOrWhiteSpace(situation)) sb.AppendLine($"[即时处境] {situation}");

            // exits：兼容字符串列表、对象列表({d,u,n} / {destination, urgency, note})
            var exits = (l1?["ex"] ?? l1?["exits"]) as Newtonsoft.Json.Linq.JArray
                ?? (obj["ex"] ?? obj["exits"]) as Newtonsoft.Json.Linq.JArray;
            if (exits != null)
            {
                sb.AppendLine("[出口]");
                foreach (var e in exits)
                {
                    string ex;
                    string urgency = "";
                    string note = "";
                    if (e.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                    {
                        ex = ((string)(e["d"] ?? e["destination"]) ?? "").Trim();
                        urgency = ((string)(e["u"] ?? e["urgency"]) ?? "").Trim();
                        note = ((string)(e["n"] ?? e["note"]) ?? "").Trim();
                    }
                    else
                    {
                        ex = ((string)e ?? "").Trim();
                    }

                    if (string.IsNullOrWhiteSpace(ex)) continue;
                    var line = new System.Text.StringBuilder($"· {ex}");
                    if (!string.IsNullOrWhiteSpace(urgency))
                    {
                        string prefix = urgency == "high" ? "🔴" : urgency == "medium" ? "🟠" : "🟢";
                        line.Append($" {prefix}");
                    }
                    if (!string.IsNullOrWhiteSpace(note)) line.Append($" {note}");
                    sb.AppendLine(line.ToString());
                }
            }

            if (sb.Length == 0)
            {
                Log.Warn(Log.Channels.Llm, "[L1] 构建的文本为空，但 JSON 解析成功。检查 l1_stage 字段是否缺失或为空。");
                Log.Debug(Log.Channels.Llm, "[L1] JSON top-level keys: {0}",
                    string.Join(", ", obj.Properties().Select(p => p.Name)));
            }

            return (sb.Length > 0, sb.ToString().Trim(), narration);
        }

        /// <summary>清理带 ``` 围栏的 LLM 输出，提取纯 JSON 部分。</summary>
        private static string SanitizeJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = raw.Trim();
            if (text.StartsWith("```"))
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(nl + 1);
                int fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0) text = text.Substring(0, fence);
                text = text.Trim();
            }
            return text;
        }
    }
}
