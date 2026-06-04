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
        private const float CardH = 880f;
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
        private readonly List<Button> presetBtns = new List<Button>();
        private int _selectedIdx;

        // ---------- name / sex row (browse) ----------
        private GameObject nameRow;
        private TMP_InputField nameInput;
        private TMP_InputField courtesyInput;
        private Button maleBtn;
        private Button femaleBtn;
        private Sex _sex = Sex.Male;

        // ---------- custom mode ----------
        private GameObject customGroup;
        private TMP_InputField charDescInput;
        private TMP_InputField worldDescInput;

        // ----------------------------------------------------------------

        public void Bind(World world, ChatBridge chat, GameStateMachine stateMachine)
        {
            this.world = world;
            this.chat = chat;
            this.stateMachine = stateMachine;
            BuildUi();
            SetVisible(false);
        }

        public void SetVisible(bool v)
        {
            if (dim  != null) dim.SetActive(v);
            if (card != null) card.SetActive(v);
            if (v && world != null) world.Paused = true;
            if (v) SwitchMode(ViewMode.Browse);
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
            rt.sizeDelta = new Vector2(CardW, CardH);
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

            BuildPresets();
            BuildNameRow();
            BuildStartButton(isCustom: false);

            // --- custom group ---
            customGroup = new GameObject("CustomGroup", typeof(RectTransform));
            customGroup.transform.SetParent(card.transform, false);
            var cgRt = (RectTransform)customGroup.transform;
            cgRt.anchorMin = Vector2.zero;
            cgRt.anchorMax = Vector2.one;
            cgRt.offsetMin = Vector2.zero;
            cgRt.offsetMax = Vector2.zero;
            BuildCustomPage();
            BuildStartButton(isCustom: true);

            // --- error label (shared, parented to card) ---
            BuildErrorLabel();

            SwitchMode(ViewMode.Browse);
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

        // -------- browse: presets --------

        private void BuildPresets()
        {
            int n = NewGamePresets.All.Count;

            for (int i = 0; i < n; i++)
            {
                var p = NewGamePresets.All[i];
                int captured = i;
                float y = 58f + i * (PresetItemH + PresetSpacing);

                var go = new GameObject($"NgPreset_{p.Id}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(browseGroup.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(24, -(y + PresetItemH));
                rt.offsetMax = new Vector2(-24, -y);
                go.GetComponent<Image>().color = UnselectedColor;
                go.GetComponent<Button>().onClick.AddListener(() => OnPresetClicked(captured));

                if (p.IsCustom)
                {
                    // Custom card
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
                    // Normal preset card
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
                    desc.text = $"{p.Description}　|　{p.TraitLabel}　|　{p.LocationName}";
                }

                presetBtns.Add(go.GetComponent<Button>());
            }

            ApplyPresetHighlight();
        }

        // -------- browse: name / sex row --------

        private void BuildNameRow()
        {
            float y = 58f + NewGamePresets.All.Count * (PresetItemH + PresetSpacing) + 12f;

            nameRow = new GameObject("NameRow", typeof(RectTransform));
            nameRow.transform.SetParent(browseGroup.transform, false);
            var rt = (RectTransform)nameRow.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(24, -(y + 50f));
            rt.offsetMax = new Vector2(-24, -y);

            // 姓名*
            var nameLab = UiKit.CreateText(nameRow.transform, "NameLab", 19, TextAlignmentOptions.Left);
            var nlRt = (RectTransform)nameLab.transform;
            nlRt.anchorMin = new Vector2(0, 0); nlRt.anchorMax = new Vector2(0, 1);
            nlRt.pivot = new Vector2(0, 0.5f);
            nlRt.sizeDelta = new Vector2(60, 0);
            nlRt.anchoredPosition = Vector2.zero;
            nameLab.text = "姓名*:";

            nameInput = ChatPanelView.MakeTmpInput(nameRow.transform, "NameInput", "如 张明", 6);
            var niRt = (RectTransform)nameInput.transform;
            niRt.anchorMin = new Vector2(0, 0); niRt.anchorMax = new Vector2(0, 1);
            niRt.pivot = new Vector2(0, 0.5f);
            niRt.sizeDelta = new Vector2(180, 0);
            niRt.anchoredPosition = new Vector2(64, 0);

            // 字
            var courtLab = UiKit.CreateText(nameRow.transform, "CourtLab", 19, TextAlignmentOptions.Left);
            var clRt = (RectTransform)courtLab.transform;
            clRt.anchorMin = new Vector2(0, 0); clRt.anchorMax = new Vector2(0, 1);
            clRt.pivot = new Vector2(0, 0.5f);
            clRt.sizeDelta = new Vector2(30, 0);
            clRt.anchoredPosition = new Vector2(256, 0);
            courtLab.text = "字:";

            courtesyInput = ChatPanelView.MakeTmpInput(nameRow.transform, "CourtesyInput", "可空", 4);
            var ciRt = (RectTransform)courtesyInput.transform;
            ciRt.anchorMin = new Vector2(0, 0); ciRt.anchorMax = new Vector2(0, 1);
            ciRt.pivot = new Vector2(0, 0.5f);
            ciRt.sizeDelta = new Vector2(150, 0);
            ciRt.anchoredPosition = new Vector2(290, 0);

            // 性别
            maleBtn = UiKit.CreateButton(nameRow.transform, "Male", "男", () => { _sex = Sex.Male; ApplySexHighlight(); });
            var mRt = (RectTransform)maleBtn.transform;
            mRt.anchorMin = new Vector2(0, 0); mRt.anchorMax = new Vector2(0, 1);
            mRt.pivot = new Vector2(0, 0.5f);
            mRt.sizeDelta = new Vector2(70, 0);
            mRt.anchoredPosition = new Vector2(460, 0);

            femaleBtn = UiKit.CreateButton(nameRow.transform, "Female", "女", () => { _sex = Sex.Female; ApplySexHighlight(); });
            var fRt = (RectTransform)femaleBtn.transform;
            fRt.anchorMin = new Vector2(0, 0); fRt.anchorMax = new Vector2(0, 1);
            fRt.pivot = new Vector2(0, 0.5f);
            fRt.sizeDelta = new Vector2(70, 0);
            fRt.anchoredPosition = new Vector2(536, 0);

            ApplySexHighlight();

            // Default random name
            var pair = Names.RandomPlayerName();
            nameInput.text = pair.Given;
            courtesyInput.text = pair.Courtesy;
        }

        // -------- custom mode --------

        private void BuildCustomPage()
        {
            // Back button
            var backBtn = UiKit.CreateButton(customGroup.transform, "BackBtn", "← 返回", () => SwitchMode(ViewMode.Browse));
            var bRt = (RectTransform)backBtn.transform;
            bRt.anchorMin = new Vector2(0, 1); bRt.anchorMax = new Vector2(0, 1);
            bRt.pivot = new Vector2(0, 1);
            bRt.sizeDelta = new Vector2(100, 36);
            bRt.anchoredPosition = new Vector2(16, -12);

            // Title
            var title = UiKit.CreateText(customGroup.transform, "CustomTitle", 27, TextAlignmentOptions.Center);
            var tRt = (RectTransform)title.transform;
            tRt.anchorMin = new Vector2(0, 1); tRt.anchorMax = new Vector2(1, 1);
            tRt.pivot = new Vector2(0.5f, 1);
            tRt.sizeDelta = new Vector2(0, 40);
            tRt.anchoredPosition = new Vector2(0, -54);
            title.text = "自定义角色与世界";

            // 角色描述
            var charLab = UiKit.CreateText(customGroup.transform, "CharLab", 19, TextAlignmentOptions.Left);
            var chRt = (RectTransform)charLab.transform;
            chRt.anchorMin = new Vector2(0, 1); chRt.anchorMax = new Vector2(1, 1);
            chRt.pivot = new Vector2(0.5f, 1);
            chRt.offsetMin = new Vector2(28, -(110 + 200 + 10));
            chRt.offsetMax = new Vector2(-28, -(110 + 200 + 10 - 24));
            charLab.text = "角色描述：";

            charDescInput = ChatPanelView.MakeTmpInput(customGroup.transform, "CharDesc",
                "例：一个出身寒门的年轻士人，立志在乱世中一展抱负……", 500);
            var cdRt = (RectTransform)charDescInput.transform;
            cdRt.anchorMin = new Vector2(0, 1); cdRt.anchorMax = new Vector2(1, 1);
            cdRt.pivot = new Vector2(0.5f, 1);
            cdRt.offsetMin = new Vector2(28, -(110 + 10));
            cdRt.offsetMax = new Vector2(-28, -110);
            charDescInput.lineType = TMP_InputField.LineType.MultiLineNewline;
            // Make viewport taller (default MakeTmpInput creates SingleLine viewport,
            // but the RectTransform offsets are set above so it spans the full area).

            // 世界描述
            var worldLab = UiKit.CreateText(customGroup.transform, "WorldLab", 19, TextAlignmentOptions.Left);
            var wlRt = (RectTransform)worldLab.transform;
            wlRt.anchorMin = new Vector2(0, 1); wlRt.anchorMax = new Vector2(1, 1);
            wlRt.pivot = new Vector2(0.5f, 1);
            wlRt.offsetMin = new Vector2(28, -110);
            wlRt.offsetMax = new Vector2(-28, -(110 - 24));
            worldLab.text = "世界描述：";

            worldDescInput = ChatPanelView.MakeTmpInput(customGroup.transform, "WorldDesc",
                "例：一个架空武侠世界，宋代风格，江湖门派林立……", 500);
            var wdRt = (RectTransform)worldDescInput.transform;
            wdRt.anchorMin = new Vector2(0, 1); wdRt.anchorMax = new Vector2(1, 1);
            wdRt.pivot = new Vector2(0.5f, 1);
            wdRt.offsetMin = new Vector2(28, 0);
            wdRt.offsetMax = new Vector2(-28, -0);
            worldDescInput.lineType = TMP_InputField.LineType.MultiLineNewline;

            customGroup.SetActive(false);
        }

        // -------- start buttons --------

        private void BuildStartButton(bool isCustom)
        {
            Transform parent = isCustom ? customGroup.transform : browseGroup.transform;
            var btn = UiKit.CreateButton(parent, "StartBtn", "开始游戏", () => OnStartClicked(isCustom));
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

        private enum ViewMode { Browse, Custom }
        private ViewMode currentMode = ViewMode.Browse;

        private void SwitchMode(ViewMode mode)
        {
            currentMode = mode;
            bool browse = mode == ViewMode.Browse;
            browseGroup.SetActive(browse);
            customGroup.SetActive(!browse);
            if (errorText != null) errorText.text = "";
            _isLoading = false;
        }

        // ================================================================
        //  Interactions
        // ================================================================

        private void OnPresetClicked(int idx)
        {
            var p = NewGamePresets.All[idx];
            if (p.IsCustom)
            {
                SwitchMode(ViewMode.Custom);
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

        private void OnStartClicked(bool fromCustom)
        {
            if (_isLoading || world == null || chat == null) return;
            _isLoading = true;

            if (fromCustom)
            {
                string cd = (charDescInput?.text ?? "").Trim();
                string wd = (worldDescInput?.text ?? "").Trim();
                if (cd.Length == 0 || wd.Length == 0)
                {
                    ShowError("※ 角色描述和世界描述不能为空");
                    _isLoading = false;
                    return;
                }
                BeginGame(customCharDesc: cd, customWorldDesc: wd);
            }
            else
            {
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
                $"特质：{ngPreset.TraitLabel}\n" +
                $"背景：{ngPreset.CharBlurb}";

            chat.StartWorldPipeline(roleDesc, ngPreset.WorldBlurb, success =>
            {
                if (!success)
                {
                    Log.Warn(Log.Channels.UI, "WorldPipeline failed, continuing with defaults");
                }

                stateMachine?.TransitionTo(GameState.Dialogue);

                var charPreset = NewGamePresets.ToCharacterPreset(ngPreset);
                chat.ExpandCharacter(charPreset, _ =>
                {
                    // 流水线已完成 L1 场景构建，直接开始开场叙事
                    chat.StartOpening(charPreset);
                });
            });
        }

        // ---------- custom path: two text fields ----------

        private void BeginGame(string customCharDesc, string customWorldDesc)
        {
            SetVisible(false);

            // For custom, we treat both descriptions as blurb inputs.
            // The character preset uses defaults + the custom char description as its blurb.
            world.Stage.WorldDescription = customWorldDesc;

            chat.StartWorldBuild(customCharDesc, customWorldDesc, success =>
            {
                if (!success)
                {
                    Log.Warn(Log.Channels.UI, "WorldPipeline failed, continuing with defaults");
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
                        : customCharDesc;

                    var tempPreset = new CharacterPresets.Preset
                    {
                        Id = "custom",
                        Title = "自定义角色",
                        LocationName = locName,
                        TraitId = traitId,
                        TraitLabel = traitLabel,
                        Blurb = blurb,
                    };

                    chat.ExpandCharacter(tempPreset, _ =>
                    {
                        chat.StartOpening(tempPreset);
                    });
                    return;
                }

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
                    Blurb = customCharDesc,
                };

                chat.ExpandCharacter(fallbackPreset, _ =>
                {
                    chat.StartStageCreate(fallbackPreset, __ => chat.StartOpening(fallbackPreset));
                });
            });
        }
    }
}
