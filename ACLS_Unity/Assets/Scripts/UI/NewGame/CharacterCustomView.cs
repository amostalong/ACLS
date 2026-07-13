using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Logging;
using ACLS.Sim;

namespace ACLS.UI
{
    /// <summary>
    /// 自定义角色 + 自定义世界输入的独立视图。
    /// 从 NewGameView 的"创建自定义角色"按钮打开，填完两份描述后走 LLM 流水线。
    /// </summary>
    public sealed class CharacterCustomView : MonoBehaviour
    {
        private const float CardW = 780f;
        private const float CardVPadding = 100f;

        private static readonly Color SelectedColor   = new Color(0.55f, 0.42f, 0.18f, 0.97f);
        private static readonly Color UnselectedColor = new Color(0.22f, 0.22f, 0.28f, 0.95f);

        private World world;
        private ChatBridge chat;
        private GameStateMachine stateMachine;
        private NewGameView newGameView;

        private GameObject dim;
        private GameObject card;
        private TextMeshProUGUI errorText;
        private bool _isLoading;

        private TMP_InputField charDescInput;
        private TMP_InputField worldDescInput;

        public void Bind(World world, ChatBridge chat, GameStateMachine stateMachine, NewGameView newGameView)
        {
            this.world = world;
            this.chat = chat;
            this.stateMachine = stateMachine;
            this.newGameView = newGameView;
            BuildUi();
            SetVisible(false);
        }

        public void SetVisible(bool v)
        {
            if (dim  != null) dim.SetActive(v);
            if (card != null) card.SetActive(v);
            if (v && world != null) world.Paused = true;
        }

        // -------- UI construction --------

        private void BuildUi()
        {
            dim = UiKit.CreatePanel(transform, "CcDim", Vector2.zero, Vector2.one,
                new Color(0, 0, 0, 0.6f));

            card = new GameObject("CcCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(transform, false);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CardW, ResizeCardHeight());
            card.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.17f, 0.97f);

            BuildTitle();
            BuildBackButton();
            BuildInputs();
            BuildErrorLabel();
            BuildStartButton();
        }

        private float ResizeCardHeight()
        {
            var prt = transform as RectTransform;
            float parentH = prt != null ? prt.rect.height : 1080f;
            return Mathf.Max(400f, parentH - CardVPadding * 2f);
        }

        private void BuildTitle()
        {
            var t = UiKit.CreateText(card.transform, "CcTitle", 29, TextAlignmentOptions.Center);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 50);
            rt.anchoredPosition = new Vector2(0, -8);
            t.text = "自定义角色与世界";
        }

        private void BuildBackButton()
        {
            var btn = UiKit.CreateButton(card.transform, "Back", "← 返回", () =>
            {
                if (_isLoading) return;
                SetVisible(false);
                newGameView?.SetVisible(true);
            });
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(100, 36);
            rt.anchoredPosition = new Vector2(20, -14);
        }

        private void BuildInputs()
        {
            // 两个输入区纵向居中放在一起
            var rowGo = new GameObject("Inputs", typeof(RectTransform));
            rowGo.transform.SetParent(card.transform, false);
            var rt = (RectTransform)rowGo.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(28, 16 + 48 + 8 + 28 + 8);  // 底部 start + error 之上
            rt.offsetMax = new Vector2(-28, -68);                  // 标题 + 返回之下

            // 角色描述
            var charLab = UiKit.CreateText(rowGo.transform, "CharLab", 19, TextAlignmentOptions.Left);
            var chRt = (RectTransform)charLab.transform;
            chRt.anchorMin = new Vector2(0, 1); chRt.anchorMax = new Vector2(1, 1);
            chRt.pivot = new Vector2(0.5f, 1);
            chRt.sizeDelta = new Vector2(0, 24);
            chRt.anchoredPosition = new Vector2(0, 0);
            charLab.text = "角色描述（姓名、出身、性格、特长等）：";

            charDescInput = ChatPanelView.MakeTmpInput(rowGo.transform, "CharDesc",
                "例：一个出身寒门的年轻士人，立志在乱世中一展抱负……", 500);
            var cdRt = (RectTransform)charDescInput.transform;
            cdRt.anchorMin = new Vector2(0, 1); cdRt.anchorMax = new Vector2(1, 1);
            cdRt.pivot = new Vector2(0.5f, 1);
            cdRt.sizeDelta = new Vector2(0, 200);
            cdRt.anchoredPosition = new Vector2(0, -32);
            charDescInput.lineType = TMP_InputField.LineType.MultiLineNewline;

            // 世界描述
            var worldLab = UiKit.CreateText(rowGo.transform, "WorldLab", 19, TextAlignmentOptions.Left);
            var wlRt = (RectTransform)worldLab.transform;
            wlRt.anchorMin = new Vector2(0, 1); wlRt.anchorMax = new Vector2(1, 1);
            wlRt.pivot = new Vector2(0.5f, 1);
            wlRt.sizeDelta = new Vector2(0, 24);
            wlRt.anchoredPosition = new Vector2(0, -254);
            worldLab.text = "世界描述（朝代、势力、风土、规则等）：";

            worldDescInput = ChatPanelView.MakeTmpInput(rowGo.transform, "WorldDesc",
                "例：架空武侠世界，宋代风格，江湖门派林立，朝堂暗流涌动……", 500);
            var wdRt = (RectTransform)worldDescInput.transform;
            wdRt.anchorMin = new Vector2(0, 1); wdRt.anchorMax = new Vector2(1, 1);
            wdRt.pivot = new Vector2(0.5f, 1);
            wdRt.sizeDelta = new Vector2(0, 200);
            wdRt.anchoredPosition = new Vector2(0, -286);
            worldDescInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        }

        private void BuildErrorLabel()
        {
            errorText = UiKit.CreateText(card.transform, "CcError", 17, TextAlignmentOptions.Center);
            errorText.color = new Color(1f, 0.45f, 0.45f, 1f);
            var rt = (RectTransform)errorText.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(24, 16 + 48 + 8);
            rt.offsetMax = new Vector2(-24, 16 + 48 + 8 + 28);
            errorText.text = "";
        }

        private void BuildStartButton()
        {
            var btn = UiKit.CreateButton(card.transform, "StartBtn", "开始游戏", OnStartClicked);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(24, 16);
            rt.offsetMax = new Vector2(-24, 16 + 48);
            btn.GetComponent<Image>().color = new Color(0.45f, 0.35f, 0.15f, 0.97f);
        }

        // -------- submit --------

        private void OnStartClicked()
        {
            if (_isLoading || world == null || chat == null) return;
            _isLoading = true;

            string cd = (charDescInput?.text ?? "").Trim();
            string wd = (worldDescInput?.text ?? "").Trim();
            if (cd.Length == 0 || wd.Length == 0)
            {
                ShowError("※ 角色描述和世界描述不能为空");
                _isLoading = false;
                return;
            }

            SetVisible(false);
            newGameView?.SetVisible(false);

            world.Stage.WorldDescription = wd;
            chat.StartWorldBuild(cd, wd, success =>
            {
                if (!success)
                    Log.Warn(Log.Channels.UI, "WorldPipeline failed, continuing with defaults");

                // EraTrend 锚点表按预设加载
                if (world != null)
                {
                    var go = this.gameObject;
                    if (go != null && go.GetComponent<EraTrendInjector>() == null)
                        go.AddComponent<EraTrendInjector>().Bind(world, world.Stage.SelectedPresetId);
                }

                stateMachine?.TransitionTo(GameState.Dialogue);

                if (success && world.Player != null)
                {
                    int traitId = world.Player.Traits != null && world.Player.Traits.Count > 0
                        ? world.Player.Traits[0]
                        : WorldFactory.TRAIT_STUDIOUS;
                    string traitLabel = traitId switch
                    {
                        WorldFactory.TRAIT_CAUTIOUS => "谨慎",
                        WorldFactory.TRAIT_DECISIVE => "果决",
                        WorldFactory.TRAIT_STUDIOUS => "好学",
                        _ => "好学",
                    };
                    string locName = world.GetLocation(world.Player.Identity?.LocationId ?? 0)?.Name ?? "颍川";
                    string blurb = !string.IsNullOrWhiteSpace(world.Player.BackgroundStory)
                        ? world.Player.BackgroundStory
                        : cd;

                    var tempPreset = new CharacterPresets.Preset
                    {
                        Id = "custom",
                        Title = "自定义角色",
                        LocationName = locName,
                        TraitId = traitId,
                        TraitLabel = traitLabel,
                        Blurb = blurb,
                    };

                    chat.StartOpening(tempPreset);
                    return;
                }

                // 失败回退
                WorldFactory.ConfigurePlayer(
                    world,
                    name: "无名",
                    courtesy: "",
                    sex: Sex.Male,
                    age: 22,
                    locationName: "颍川",
                    traitId: WorldFactory.TRAIT_STUDIOUS);

                var fallbackPreset = new CharacterPresets.Preset
                {
                    Id = "custom",
                    Title = "自定义角色",
                    LocationName = "颍川",
                    TraitId = WorldFactory.TRAIT_STUDIOUS,
                    TraitLabel = "好学",
                    Blurb = cd,
                };

                chat.StartStageCreate(fallbackPreset, _ => chat.StartOpening(fallbackPreset));
            });
        }

        private void ShowError(string msg)
        {
            if (errorText != null) errorText.text = msg ?? "";
        }
    }
}
