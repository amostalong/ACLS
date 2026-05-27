using UnityEngine;
using ACLS.Data;
using ACLS.Llm;
using ACLS.Sim;
using Cysharp.Threading.Tasks;

namespace ACLS.Authoring
{
    // Auto-spawned at scene load — works on any scene including the default
    // SampleScene. No prefab wiring required.
    public sealed class GameBootstrap : MonoBehaviour
    {
        private World world;
        private GameClockDriver clock;
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

            WorldFactory.RegisterPlaceholderLocalization();

            Registry.Clear();
            foreach (var t in WorldFactory.BuildPlaceholderTraits()) Registry.Register(t);
            foreach (var e in WorldFactory.BuildPlaceholderEvents()) Registry.Register(e);

            world = WorldFactory.BuildPlaceholderWorld();

            clock = gameObject.AddComponent<GameClockDriver>();
            clock.Bind(world);

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

            UiBuilder.Build(world, clock, chat, stateMachine);
            stateMachine.TransitionTo(GameState.WorldSelection);

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
            Debug.Log($"[ACLS] LlmConfig loaded: profile={cfg.Active.ProfileName}, provider={cfg.Provider}, model={cfg.Model}, baseUrl={baseUrlForLog}, keyLen={cfg.ApiKey?.Length ?? 0}");
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
            var expMd = ContentLoader.LoadSync<TextAsset>("Assets/Content/Prompts/CharacterExpansion.md", "Prompts/CharacterExpansion");

            cfg.SystemPromptMd = sysMd ?? CreateInlineTextAsset("SystemPrompt.md", BuiltInSystemPrompt());
            cfg.WorldCreatePromptMd = expMd ?? CreateInlineTextAsset("CharacterExpansion.md", BuiltInExpansionPrompt());

            if (sysMd == null || expMd == null)
                Debug.Log("[ACLS] 未找到 Resources/Prompts/*.md，使用内置默认提示词。建议在 Resources/Prompts/ 下放 .md 文件以便自定义。");

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
                "user message 若含「[开场]」字样，表明玩家刚在建角面板选定身份（含一段背景 blurb）；请据此描写主角的第一次登场场景与 3-4 个开局选项。后续回合按常规推进。\n" +
                "\n" +
                "每次回复严格使用 JSON：\n" +
                "{\n" +
                "  \"narration\": \"<2-4 段中文叙事>\",\n" +
                "  \"scene_participants\": [ {\"name\": \"<人名>\", \"role\": \"<你/妻/友/客/敌/旁观/...>\"} ],\n" +
                "  \"choices\": [\n" +
                "    {\n" +
                "      \"label\": \"<10 字以内的选项标题>\",\n" +
                "      \"outcome_narration\": \"<2-3 段中文叙事>\",\n" +
                "      \"days_passed\": <1..90 的整数>,\n" +
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
                "返回 3-4 个选项。effects 数组可空。\n" +
                "字数控制：narration ≤ 250 字；每个 outcome_narration 100-180 字；scene_participants ≤ 5 人。\n" +
                "stat delta 单次幅度 ±1~3，不超过 ±5。\n" +
                "不要任何 JSON 之外的文字（含 ``` 围栏）。";
        }

        private static string BuiltInExpansionPrompt()
        {
            return "你是一位精通东汉末年历史的角色设定师。玩家刚创建了一个新角色，请基于以下信息，为该角色生成详细的背景设定。\n" +
                "\n" +
                "【角色基础信息】\n" +
                "姓名：{name}\n" +
                "字：{courtesy}\n" +
                "性别：{sex}\n" +
                "年龄：{age} 岁\n" +
                "出身地：{location}\n" +
                "背景简述：{blurb}\n" +
                "当前日期：{date}\n" +
                "核心特质：{trait}\n" +
                "\n" +
                "【时代背景】\n" +
                "184 年正月，黄巾起义在即。朝政日衰，宦官擅权，太平道兴起。\n" +
                "\n" +
                "【要求】\n" +
                "请用 JSON 格式返回角色的详细设定：\n" +
                "{\n" +
                "  \"family_background\": \"<2-3 句，描述父母、兄弟、家庭状况>\",\n" +
                "  \"social_circle\": [\n" +
                "    {\"name\": \"<人名>\", \"relation\": \"<与主角的关系>\", \"attitude_toward_player\": \"<态度>\"}\n" +
                "  ],\n" +
                "  \"recent_goal\": \"<主角近期最想达成的事，1 句>\",\n" +
                "  \"secret\": \"<主角的一个秘密或软肋，1 句>\",\n" +
                "  \"values\": \"<主角的核心价值观，1 句>\",\n" +
                "  \"starting_assets\": {\n" +
                "    \"connections\": [\"人脉1\", \"人脉2\"],\n" +
                "    \"knowledge\": [\"情报1\", \"情报2\"],\n" +
                "    \"items\": [\"随身物品1\", \"随身物品2\"]\n" +
                "  }\n" +
                "}\n" +
                "\n" +
                "要求：\n" +
                "- 所有人名使用东汉风格（姓氏+名，如 赵嵩、刘勇）\n" +
                "- 社会关系 2-5 人，每人必须有明确的态度（友善/中立/戒备/敌对）\n" +
                "- secret 必须合理且有趣，能驱动后续剧情（不可过于离奇）\n" +
                "- values 要与核心特质一致\n" +
                "- 不得与已知史实人物产生血缘/婚约等不当关联\n" +
                "- 不要任何 JSON 之外的文字（含 ``` 围栏）。";
        }
    }
}
