using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Logging;
using ACLS.Sim;

namespace ACLS.UI
{
    /// <summary>
    /// 新游戏第一步 UI：合并了「选择世界」和「创建角色」两个步骤。
    /// 玩家选一个预设（含世界+角色）或自定义填写两段描述，然后直接启动游戏链。
    /// </summary>
    public sealed class NewGameView : MonoBehaviour
    {
        private const float CardW = 780f;
        private const float CardVPadding = 100f;  // Card 上下各留 100px
        private const float PresetItemH = 80f;
        private const float PresetSpacing = 8f;

        private static readonly Color SelectedColor   = new Color(0.55f, 0.42f, 0.18f, 0.97f);
        private static readonly Color UnselectedColor = new Color(0.22f, 0.22f, 0.28f, 0.95f);

        // ---------- external refs ----------
        private World world;
        private ChatBridge chat;
        private GameStateMachine stateMachine;

        // ---------- shared UI ----------
        private GameObject dim;
        private GameObject card;
        private TextMeshProUGUI errorText;
        private bool _isLoading; // prevent double-click on start

        // ---------- browse mode ----------
        private GameObject browseGroup;
        private ScrollRect browseScroll;
        private RectTransform browseContent;
        private readonly List<Button> presetBtns = new List<Button>();
        private int _selectedIdx;

        // ---------- name / sex row (browse) ----------
        private GameObject nameRow;
        private TMP_InputField nameInput;
        private TMP_InputField courtesyInput;
        private Button maleBtn;
        private Button femaleBtn;
        private Sex _sex = Sex.Male;

        // ---------- external (custom view) ----------
        private CharacterCustomView customView;

        // ----------------------------------------------------------------

        public void Bind(World world, ChatBridge chat, GameStateMachine stateMachine, CharacterCustomView customView)
        {
            this.world = world;
            this.chat = chat;
            this.stateMachine = stateMachine;
            this.customView = customView;
            BuildUi();
            SetVisible(false);
        }

        public void SetVisible(bool v)
        {
            if (dim  != null) dim.SetActive(v);
            if (card != null) card.SetActive(v);
            if (v && world != null) world.Paused = true;
        }

        // ================================================================
        //  UI construction
        // ================================================================

        private void BuildUi()
        {
            // Dim backdrop
            dim = UiKit.CreatePanel(transform, "NgDim", Vector2.zero, Vector2.one,
                new Color(0, 0, 0, 0.6f));

            // Card
            card = new GameObject("NgCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(transform, false);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CardW, ResizeCardHeight());
            card.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.17f, 0.97f);

            BuildTitle();

            // --- browse group ---
            browseGroup = new GameObject("BrowseGroup", typeof(RectTransform));
            browseGroup.transform.SetParent(card.transform, false);
            var bgRt = (RectTransform)browseGroup.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            BuildBrowseScroll();
            BuildStartButton();

            // --- error label (shared, parented to card) ---
            BuildErrorLabel();
        }

        private void BuildTitle()
        {
            var t = UiKit.CreateText(card.transform, "NgTitle", 31, TextAlignmentOptions.Center);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 50);
            rt.anchoredPosition = new Vector2(0, -8);
            t.text = "选择你的身份";
        }

        // -------- browse: scroll view + presets + name row --------

        private void BuildBrowseScroll()
        {
            // Scroll view fills browseGroup between title area (~58px top) and start button (~64px bottom)
            var scrollGo = new GameObject("BrowseScroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(browseGroup.transform, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(0, 16 + 48 + 28 + 4);  // above bottom button+error area
            scrollRt.offsetMax = new Vector2(0, -58);                // below title
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);  // transparent

            var sr = browseScroll = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            // Viewport
            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            vpGo.GetComponent<Mask>().showMaskGraphic = false;

            // Content: VerticalLayoutGroup + ContentSizeFitter 驱动高度
            var contentGo = new GameObject("Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var ctRt = browseContent = (RectTransform)contentGo.transform;
            ctRt.anchorMin = new Vector2(0, 1);
            ctRt.anchorMax = new Vector2(1, 1);
            ctRt.pivot = new Vector2(0.5f, 1);
            ctRt.anchoredPosition = Vector2.zero;
            ctRt.sizeDelta = new Vector2(0, 0);

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = PresetSpacing;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRt;
            sr.content = ctRt;

            BuildPresets();
            //BuildNameRow();
        }

        private void BuildPresets()
        {
            int n = NewGamePresets.All.Count;

            for (int i = 0; i < n; i++)
            {
                var p = NewGamePresets.All[i];
                int captured = i;

                var go = new GameObject($"NgPreset_{p.Id}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button),
                    typeof(LayoutElement));
                go.transform.SetParent(browseContent, false);
                var le = go.GetComponent<LayoutElement>();
                le.minHeight = PresetItemH;
                le.preferredHeight = PresetItemH;
                le.flexibleHeight = 0f;
                go.GetComponent<Image>().color = UnselectedColor;
                go.GetComponent<Button>().onClick.AddListener(() => OnPresetClicked(captured));

                if (p.IsCustom)
                {
                    var icon = UiKit.CreateText(go.transform, "Icon", 25, TextAlignmentOptions.TopLeft);
                    var iRt = (RectTransform)icon.transform;
                    iRt.anchorMin = new Vector2(0, 1); iRt.anchorMax = new Vector2(1, 1);
                    iRt.pivot = new Vector2(0.5f, 1);
                    iRt.offsetMin = new Vector2(14, -34); iRt.offsetMax = new Vector2(-14, -10);
                    icon.text = "创建 自定义角色";

                    var desc = UiKit.CreateText(go.transform, "Desc", 17, TextAlignmentOptions.TopLeft);
                    desc.color = new Color(0.82f, 0.82f, 0.82f, 1f);
                    var dRt = (RectTransform)desc.transform;
                    dRt.anchorMin = new Vector2(0, 0); dRt.anchorMax = new Vector2(1, 1);
                    dRt.offsetMin = new Vector2(14, 6); dRt.offsetMax = new Vector2(-14, -38);
                    desc.text = "写出你自己的角色和世界故事，开始属于你的冒险。";
                }
                else
                {
                    var title = UiKit.CreateText(go.transform, "Title", 21, TextAlignmentOptions.TopLeft);
                    var tRt = (RectTransform)title.transform;
                    tRt.anchorMin = new Vector2(0, 1); tRt.anchorMax = new Vector2(1, 1);
                    tRt.pivot = new Vector2(0.5f, 1);
                    tRt.offsetMin = new Vector2(14, -32); tRt.offsetMax = new Vector2(-14, -8);
                    title.text = $"{p.Title}　·　{p.Era}";

                    var desc = UiKit.CreateText(go.transform, "Desc", 17, TextAlignmentOptions.TopLeft);
                    desc.color = new Color(0.82f, 0.82f, 0.82f, 1f);
                    var dRt = (RectTransform)desc.transform;
                    dRt.anchorMin = new Vector2(0, 0); dRt.anchorMax = new Vector2(1, 1);
                    dRt.offsetMin = new Vector2(14, 6); dRt.offsetMax = new Vector2(-14, -36);
                    desc.text = $"{p.Description}　|　{p.LocationName}";
                }

                presetBtns.Add(go.GetComponent<Button>());
            }

            ApplyPresetHighlight();
        }

        // -------- browse: name / sex row --------

        private void BuildNameRow()
        {
            nameRow = new GameObject("NameRow",
                typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            nameRow.transform.SetParent(browseContent, false);
            var nrt = (RectTransform)nameRow.transform;
            nrt.anchorMin = new Vector2(0, 1);
            nrt.anchorMax = new Vector2(1, 1);
            nrt.pivot = new Vector2(0.5f, 1);
            nrt.sizeDelta = new Vector2(0, 50f);
            nrt.anchoredPosition = Vector2.zero;
            var nle = nameRow.GetComponent<LayoutElement>();
            nle.minHeight = 50f;
            nle.preferredHeight = 50f;
            nle.flexibleHeight = 0f;
            var hlg = nameRow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // 姓名*
            var nameLab = UiKit.CreateText(nameRow.transform, "NameLab", 19, TextAlignmentOptions.Left);
            var nlLe = nameLab.gameObject.AddComponent<LayoutElement>();
            nlLe.minWidth = nlLe.preferredWidth = 60f;
            nlLe.flexibleWidth = 0f;
            nameLab.text = "姓名*:";

            nameInput = ChatPanelView.MakeTmpInput(nameRow.transform, "NameInput", "如 张明", 6);
            var niLe = nameInput.gameObject.AddComponent<LayoutElement>();
            niLe.minWidth = niLe.preferredWidth = 180f;
            niLe.flexibleWidth = 0f;

            // 字
            var courtLab = UiKit.CreateText(nameRow.transform, "CourtLab", 19, TextAlignmentOptions.Left);
            var clLe = courtLab.gameObject.AddComponent<LayoutElement>();
            clLe.minWidth = clLe.preferredWidth = 30f;
            clLe.flexibleWidth = 0f;
            courtLab.text = "字:";

            courtesyInput = ChatPanelView.MakeTmpInput(nameRow.transform, "CourtesyInput", "可空", 4);
            var ciLe = courtesyInput.gameObject.AddComponent<LayoutElement>();
            ciLe.minWidth = ciLe.preferredWidth = 150f;
            ciLe.flexibleWidth = 0f;

            // 性别
            maleBtn = UiKit.CreateButton(nameRow.transform, "Male", "男", () => { _sex = Sex.Male; ApplySexHighlight(); });
            var mLe = maleBtn.gameObject.AddComponent<LayoutElement>();
            mLe.minWidth = mLe.preferredWidth = 70f;
            mLe.flexibleWidth = 0f;

            femaleBtn = UiKit.CreateButton(nameRow.transform, "Female", "女", () => { _sex = Sex.Female; ApplySexHighlight(); });
            var fLe = femaleBtn.gameObject.AddComponent<LayoutElement>();
            fLe.minWidth = fLe.preferredWidth = 70f;
            fLe.flexibleWidth = 0f;

            ApplySexHighlight();

            // Default random name
            var pair = Names.RandomPlayerName();
            nameInput.text = pair.Given;
            courtesyInput.text = pair.Courtesy;
        }

        // -------- start button --------

        private void BuildStartButton()
        {
            var btn = UiKit.CreateButton(browseGroup.transform, "StartBtn", "开始游戏", OnStartClicked);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(24, 16);
            rt.offsetMax = new Vector2(-24, 16 + 48);
            btn.GetComponent<Image>().color = new Color(0.45f, 0.35f, 0.15f, 0.97f);
        }

        // -------- error --------

        private void BuildErrorLabel()
        {
            errorText = UiKit.CreateText(card.transform, "NgError", 17, TextAlignmentOptions.Center);
            errorText.color = new Color(1f, 0.45f, 0.45f, 1f);
            // Anchored to bottom of card, just above the start button.
            var rt = (RectTransform)errorText.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(24, 16 + 48 + 8);           // y=72 from bottom
            rt.offsetMax = new Vector2(-24, 16 + 48 + 8 + 28);     // h=28
            errorText.text = "";
        }

        private void ShowError(string msg)
        {
            if (errorText != null) errorText.text = msg ?? "";
        }

        // ================================================================
        //  Mode switching
        // ================================================================


        // Card 高度 = parent 高度 - 上下各 100px
        private float ResizeCardHeight()
        {
            var prt = transform as RectTransform;
            float parentH = prt != null ? prt.rect.height : 1080f;
            return Mathf.Max(400f, parentH - CardVPadding * 2f);
        }

        private void Awake()
        {
            // 屏幕大小变化时跟着缩放
            var prt = transform as RectTransform;
            if (prt != null && card != null)
            {
                var cardRt = (RectTransform)card.transform;
                cardRt.sizeDelta = new Vector2(CardW, ResizeCardHeight());
            }
        }

        // ================================================================
        //  Interactions
        // ================================================================

        private void OnPresetClicked(int idx)
        {
            var p = NewGamePresets.All[idx];
            if (p.IsCustom)
            {
                // 打开独立的自定义角色 view
                if (customView != null)
                {
                    SetVisible(false);
                    customView.SetVisible(true);
                }
                return;
            }

            _selectedIdx = idx;
            ApplyPresetHighlight();
        }

        private void ApplyPresetHighlight()
        {
            for (int i = 0; i < presetBtns.Count; i++)
            {
                bool sel = i == _selectedIdx && !NewGamePresets.All[i].IsCustom;
                presetBtns[i].GetComponent<Image>().color = sel ? SelectedColor : UnselectedColor;
            }
        }

        private void ApplySexHighlight()
        {
            if (maleBtn   != null) maleBtn.GetComponent<Image>().color   = _sex == Sex.Male   ? SelectedColor : UnselectedColor;
            if (femaleBtn != null) femaleBtn.GetComponent<Image>().color = _sex == Sex.Female ? SelectedColor : UnselectedColor;
        }

        // ================================================================
        //  Start game
        // ================================================================

        private void OnStartClicked()
        {
            if (_isLoading || world == null || chat == null) return;
            _isLoading = true;

            string name = (nameInput?.text ?? "").Trim();
            if (name.Length == 0)
            {
                ShowError("※ 姓名不能为空");
                if (nameInput != null) nameInput.ActivateInputField();
                _isLoading = false;
                return;
            }
            string courtesy = (courtesyInput?.text ?? "").Trim();
            BeginGame(name, courtesy, _sex);
        }

        // ---------- browse path: pre-made preset ----------

        private void BeginGame(string name, string courtesy, Sex sex)
        {
            SetVisible(false);

            var ngPreset = NewGamePresets.All[_selectedIdx];
            world.Stage.WorldDescription = ngPreset.WorldBlurb;

            WorldFactory.ConfigurePlayer(
                world,
                name: name,
                courtesy: courtesy,
                sex: sex,
                age: 22,
                locationName: ngPreset.LocationName,
                traitId: ngPreset.TraitId);

            string roleDesc =
                $"姓名：{name}\n" +
                $"字：{courtesy}\n" +
                $"性别：{(sex == Sex.Male ? "男" : "女")}\n" +
                $"年龄：22\n" +
                $"出身地：{ngPreset.LocationName}\n" +
                $"背景：{ngPreset.CharBlurb}";

            chat.StartWorldPipeline(roleDesc, ngPreset.WorldBlurb, success =>
            {
                if (!success)
                {
                    Log.Warn(Log.Channels.UI, "WorldPipeline failed, continuing with defaults");
                }

                stateMachine?.TransitionTo(GameState.Dialogue);

                var charPreset = NewGamePresets.ToCharacterPreset(ngPreset);
                // 流水线已完成 L1 场景构建，直接开始开场叙事
                chat.StartOpening(charPreset);
            });
        }
    }
}
