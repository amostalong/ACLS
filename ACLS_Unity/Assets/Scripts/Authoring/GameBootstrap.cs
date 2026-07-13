using UnityEngine;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Logging;
using ACLS.Sim;
using Cysharp.Threading.Tasks;

namespace ACLS.Authoring
{
    // Auto-spawned at scene load — works on any scene including the default
    // SampleScene. No prefab wiring required.
    public sealed class GameBootstrap : MonoBehaviour
    {
        private World world;
        private EventDispatcher dispatcher;
        private ChatBridge chat;
        private GameStateMachine stateMachine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (FindObjectOfType<GameBootstrap>() != null) return;
            var go = new GameObject("[GameRoot]");
            DontDestroyOnLoad(go);
            go.AddComponent<GameBootstrap>();
        }

        private bool _booted;

        private void Awake()
        {
            BootAsync().Forget();
        }

        private async UniTaskVoid BootAsync()
        {
            if (_booted) return;
            _booted = true;

            await YooAssetBootstrapper.InitializeAsync();

            // ═══ Editor only: 自动删除存档开关 ═══
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool("ACLS_AutoDeleteSave", false))
                SaveManager.DeleteSlot("slot0");
#endif

            // 1. 先检查存档——日志会先输出
            bool hasSave = SaveManager.SlotExists();
            SaveData saveData = null;
            if (hasSave)
            {
                SaveManager.TryLoad("slot0", out saveData);
                if (saveData?.World == null)
                {
                    Log.Warn(Log.Channels.System, "存档加载失败，按新建游戏流程走");
                    hasSave = false;
                }
            }

            // 2. 加载游戏数据库（人物/势力/地点）——有无存档都需要，供 lookup 工具查询
            GameDataLoader.Init();

            // 3. 注册系统组件（Traits / Events / Localization）——两边共享
            WorldFactory.RegisterPlaceholderLocalization();

            Registry.Clear();
            foreach (var t in WorldFactory.BuildPlaceholderTraits()) Registry.Register(t);
            foreach (var e in WorldFactory.BuildPlaceholderEvents()) Registry.Register(e);

            // 4. 构建或恢复世界
            if (hasSave && saveData != null)
            {
                world = saveData.World;
                Log.Info(Log.Channels.System, "从存档恢复世界: player={0}", world.Player?.Name ?? "(null)");
            }
            else
            {
                world = WorldFactory.BuildPlaceholderWorld();
            }

            // 5. 共用基础设施
            dispatcher = gameObject.AddComponent<EventDispatcher>();
            dispatcher.Bind(world);

            chat = gameObject.AddComponent<ChatBridge>();
            var (llm, configError) = TryCreateLlmClient();
            var promptConfig = ContentLoader.LoadSync<LlmPromptConfig>("Assets/Content/Config/LlmPromptConfig.asset", "LlmPromptConfig")
                ?? CreateDefaultPromptConfig();
            chat.Bind(world, llm, promptConfig, configError);

            var promptSelector = new PromptSelector(promptConfig);
            stateMachine = new GameStateMachine(world, chat, promptSelector);
            chat.StateMachine = stateMachine;

            // 7. UI
            UiBuilder.Build(world, chat, stateMachine);

            // 8. 跳转到正确的状态
            if (hasSave)
            {
                stateMachine.TransitionTo(GameState.Dialogue);
                // TODO: 通知 LLM 继续（恢复上下文后让 LLM 重新生成当前场景叙述）
            }
            else
            {
                stateMachine.TransitionTo(GameState.WorldSelection);
            }

            world.Paused = true;
        }

        /// <summary>
        /// 异步初始化 YooAsset 资源系统。
        /// 从 Awake 触发，不阻塞同步启动流程。
        /// </summary>
        private static (ILlmClient client, string error) TryCreateLlmClient()
        {
            var cfg = ContentLoader.LoadSync<LlmConfig>("Assets/Content/Config/LlmConfig.asset", "LlmConfig");
            if (cfg == null)
                return (null, $"未找到 Assets/Content/Config/LlmConfig.asset。请创建（Create → ACLS → LLM Config）并添加至少一个 Profile；若在 Player 中运行，请先构建 YooAsset 默认包（{YooAssetBootstrapper.DefaultPackageName}）并随包发布。");
            if (cfg.Profiles == null || cfg.Profiles.Count == 0)
                return (null, "LlmConfig 里没有任何 Profile。请在 Inspector 里点「＋ Add Profile」并勾选「激活」。");
            if (cfg.Active == null)
                return (null, "LlmConfig 没有激活的 Profile。请在 Inspector 里展开一条 Profile 并勾选「激活」。");
            if (!cfg.IsConfigured)
            {
                string missing = string.IsNullOrWhiteSpace(cfg.ApiKey) ? "ApiKey" :
                                 string.IsNullOrWhiteSpace(cfg.Model)  ? "Model"  :
                                 "BaseUrl（OpenAi 兼容时必填）";
                return (null, $"当前激活 Profile「{cfg.Active.ProfileName}」未填完整：缺 {missing}");
            }
            string baseUrlForLog = string.IsNullOrWhiteSpace(cfg.BaseUrl) ? "(default)" : cfg.BaseUrl;
            Log.Info(Log.Channels.System, "LlmConfig loaded: profile={0}, provider={1}, model={2}, baseUrl={3}, keyLen={4}", cfg.Active.ProfileName, cfg.Provider, cfg.Model, baseUrlForLog, cfg.ApiKey?.Length ?? 0);
            ILlmClient client = cfg.Provider switch
            {
                LlmProvider.Anthropic => new AnthropicClient(cfg.BaseUrl, cfg.ApiKey, cfg.Model, cfg.MaxTokens, cfg.VerboseLogging),
                LlmProvider.OpenAiCompatible => new OpenAiCompatibleClient(cfg.BaseUrl, cfg.ApiKey, cfg.Model, cfg.MaxTokens, cfg.VerboseLogging),
                _ => null,
            };
            return (client, null);
        }

        // Fallback when no LlmPromptConfig.asset exists in Resources.
        // Tries to load .md files from Resources/Prompts/; if missing falls
        // back to a minimal built-in prompt so the game can still boot.
        private static LlmPromptConfig CreateDefaultPromptConfig()
        {
            var cfg = ScriptableObject.CreateInstance<LlmPromptConfig>();

            var sysMd = ContentLoader.LoadSync<TextAsset>("Assets/Content/Prompts/SysPrompt.md", "Prompts/SysPrompt");

            cfg.SystemPromptMd = sysMd ?? CreateInlineTextAsset("SystemPrompt.md", BuiltInSystemPrompt());

            if (sysMd == null)
                Log.Info(Log.Channels.System, "未找到 Resources/Prompts/SysPrompt.md，使用内置默认提示词。建议在 Resources/Prompts/ 下放 .md 文件以便自定义。");

            return cfg;
        }

        private static TextAsset CreateInlineTextAsset(string fileName, string text)
        {
            var ta = new TextAsset(text);
            ta.name = fileName;
            return ta;
        }

        private static string BuiltInSystemPrompt()
        {
            return "你是一款三国题材 CK3-like 互动小说的旁白与导演。每条 user message 前会附当前世界状态。\n" +
                "\n" +
                "【世界观】东汉末年（184 年正月起）。朝政日衰、宦官擅权、太平道兴起。叙事必须严格基于 184 年东汉史实。\n" +
                "\n" +
                "【叙事铁律】\n" +
                "① 玩家说的每一句话都必须回应——不可跳过、不可无视、不可只回一半。\n" +
                "② 不知道就直说\"不知道\"，不能绕开。多重话题逐一回应，不得遗漏。\n" +
                "③ 情绪靠动作和对话传递，不做心理旁白（不写\"你感到愤怒\"）。\n" +
                "④ 场景描写：感官先行（视→听→嗅→触），不超过 3-4 句。对话用汉末口吻，禁用现代词（\"OK\"\"没问题\"\"搞定\"）。结果描述简洁，动词精准。\n" +
                "\n" +
                "【节奏】战斗：快节奏短句动作驱动；谋划：慢节奏对话留白；日常：细节丰富推进关系。\n" +
                "\n" +
                "【场景格式】每个新场景开头标注：【中平X年·X月X日·时辰·地点】。赶路无事件直接跳过，一句过渡即可，不提问。\n" +
                "\n" +
                "【历史约束】\n" +
                "- 地名禁用现代名：四川→益州，重庆→江州，宜宾→僰道，乐山→南安，眉山→武阳，泸州→江阳。\n" +
                "- 张角 184 年末病逝；曹操 29 岁议郎；刘备 23 岁织席贩履；孙坚 29 岁盐渎丞；董卓 46 岁并州刺史。\n" +
                "- 刘焉尚未改州牧（188 年）；巴郡未分拆（194 年后）；不能用\"巴西郡\"\"汉嘉郡\"\"朱提郡\"。\n" +
                "- 无马镫；主兵器为环首刀、矛、弓弩。1 汉里≈415m。\n" +
                "\n" +
                "【吸引力法则】\n" +
                "- 玩家始终有目标→克服障碍→收获→新目标。不让玩家\"不知道下一步该干嘛\"。\n" +
                "- 世界暗中在动。NPC 不在视线里也在做事。给合理线索让玩家享受\"原来如此\"。\n" +
                "- 选择有回响：今天的决定，三天后、七天后、一个月后有后果。善有善报，恶有恶果。\n" +
                "- 可压两三件事同时烧（时间紧、资源不够、信息不全），但必须在叙事中留线索和出口。\n" +
                "\n" +
                "user message 若含「[开场]」字样，表明玩家刚在建角面板选定身份（含一段背景 blurb）；请据此描写主角的第一次登场场景与 1-4 个开局选项。后续回合按常规推进。\n" +
                "\n" +
                "【时间规则】对话不等于过几天。每次回复默认 days_passed = 1（次日）。\n" +
                "只有 outcome_narration 明确描写了「数日过去」「一周后」「次日清晨」等跨度时，才写对应的 1..7 天。\n" +
                "跳到确切日期（节日、历史事件锚点）才填 date 字段，且只填比当前日期更晚的日期。\n" +
                "超过 7 天的跳跃会被丢弃，date 倒退也会被丢弃。\n" +
                "\n" +
                "每次回复严格使用 JSON：\n" +
                "{\n" +
                "  \"date\": \"<当前叙事日期，格式如 0184年01月08日；只在需要跳到确切日期时填，否则留空或省略>\",\n" +
                "  \"narration\": \"<2-4 段中文叙事>\",\n" +
                "  \"scene_participants\": [ {\"name\": \"<人名>\", \"role\": \"<你/妻/友/客/敌/旁观/...>\"} ],\n" +
                "  \"choices\": [\n" +
                "    {\n" +
                "      \"label\": \"<10 字以内的选项标题>\",\n" +
                "      \"outcome_narration\": \"<2-3 段中文叙事>\",\n" +
                "      \"days_passed\": <1..7 的整数；1 表示次日，7 表示一周；对话不等于过几天，默认填 1>,\n" +
                "      \"effects\": [\n" +
                "        {\"kind\": \"AdjustStat\",    \"stat\": \"Wu|Tong|Zhi|Zheng|Mei\", \"delta\": <±整数>},\n" +
                "        {\"kind\": \"AdjustGold\",    \"delta\": <±整数>},\n" +
                "        {\"kind\": \"AdjustOpinion\", \"target\": \"<人名>\", \"delta\": <±整数>},\n" +
                "        {\"kind\": \"AddTrait\",      \"trait\": \"cautious|decisive|studious\"},\n" +
                "        {\"kind\": \"RemoveTrait\",   \"trait\": \"cautious|decisive|studious\"}\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}\n" +
                "\n" +
                "返回 1-4 个选项。effects 数组可空。\n" +
                "字数控制：narration ≤ 250 字；每个 outcome_narration 100-180 字；scene_participants ≤ 5 人。\n" +
                "stat delta 单次幅度 ±1~3，不超过 ±5。\n" +
                "不要任何 JSON 之外的文字（含 ``` 围栏）。";
        }
    }
}
