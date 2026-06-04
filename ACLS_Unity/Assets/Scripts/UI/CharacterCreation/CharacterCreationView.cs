using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Sim;

namespace ACLS.UI
{
    public sealed class CharacterCreationView : MonoBehaviour
    {
        private const float CardW = 720f;
        private const float CardH = 820f;
        private const float PresetItemH = 86f;
        private const float PresetSpacing = 8f;

        private static readonly Color SelectedColor = new Color(0.55f, 0.42f, 0.18f, 0.97f);
        private static readonly Color UnselectedColor = new Color(0.22f, 0.22f, 0.28f, 0.95f);

        private World world;
        private ChatBridge chat;
        private GameStateMachine stateMachine;

        private GameObject dim;
        private GameObject card;
        private TMP_InputField nameInput;
        private TMP_InputField courtesyInput;
        private TextMeshProUGUI errorText;
        private Button maleBtn;
        private Button femaleBtn;
        private readonly List<Button> presetBtns = new List<Button>();

        private Sex selectedSex = Sex.Male;
        private int selectedPresetIndex = 0;

        public void Bind(World world, ChatBridge chat, GameStateMachine stateMachine)
        {
            this.world = world;
            this.chat = chat;
            this.stateMachine = stateMachine;
            BuildUi();
            if (dim != null) dim.SetActive(false);
            if (card != null) card.SetActive(false);
        }

        public void SetVisible(bool v)
        {
            if (dim != null) dim.SetActive(v);
            if (card != null) card.SetActive(v);
            if (v && world != null) world.Paused = true;
            if (v && nameInput != null) nameInput.ActivateInputField();
        }

        // -------- UI construction --------

        private void BuildUi()
        {
            dim = UiKit.CreatePanel(transform, "Dim", Vector2.zero, Vector2.one,
                new Color(0, 0, 0, 0.55f));

            card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(transform, false);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CardW, CardH);
            card.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.17f, 0.97f);

            BuildTitle();
            BuildNameRow(yFromTop: 70);
            BuildErrorLabel(yFromTop: 132);
            BuildSexRow(yFromTop: 168);
            BuildBackgroundLabel(yFromTop: 228);
            BuildPresetCards(yFromTop: 256);
            BuildStartButton(yFromBottom: 16);
        }

        private void BuildTitle()
        {
            var title = UiKit.CreateText(card.transform, "Title", 31, TextAlignmentOptions.Center);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.sizeDelta = new Vector2(0, 50);
            titleRt.anchoredPosition = new Vector2(0, -8);
            title.text = "创建角色";
        }

        private void BuildNameRow(float yFromTop)
        {
            var rowGo = new GameObject("NameRow", typeof(RectTransform));
            rowGo.transform.SetParent(card.transform, false);
            var rt = (RectTransform)rowGo.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(28, -(yFromTop + 50));
            rt.offsetMax = new Vector2(-28, -yFromTop);

            var nameLab = UiKit.CreateText(rowGo.transform, "NameLab", 19, TextAlignmentOptions.Left);
            var nameLabRt = (RectTransform)nameLab.transform;
            nameLabRt.anchorMin = new Vector2(0, 0);
            nameLabRt.anchorMax = new Vector2(0, 1);
            nameLabRt.pivot = new Vector2(0, 0.5f);
            nameLabRt.sizeDelta = new Vector2(76, 0);
            nameLabRt.anchoredPosition = new Vector2(0, 0);
            nameLab.text = "姓名*：";

            nameInput = ChatPanelView.MakeTmpInput(rowGo.transform, "NameInput", "如 张明", charLimit: 6);
            var niRt = (RectTransform)nameInput.transform;
            niRt.anchorMin = new Vector2(0, 0);
            niRt.anchorMax = new Vector2(0, 1);
            niRt.pivot = new Vector2(0, 0.5f);
            niRt.sizeDelta = new Vector2(220, 0);
            niRt.anchoredPosition = new Vector2(80, 0);

            var courtLab = UiKit.CreateText(rowGo.transform, "CourtLab", 19, TextAlignmentOptions.Left);
            var courtLabRt = (RectTransform)courtLab.transform;
            courtLabRt.anchorMin = new Vector2(0, 0);
            courtLabRt.anchorMax = new Vector2(0, 1);
            courtLabRt.pivot = new Vector2(0, 0.5f);
            courtLabRt.sizeDelta = new Vector2(54, 0);
            courtLabRt.anchoredPosition = new Vector2(316, 0);
            courtLab.text = "字：";

            courtesyInput = ChatPanelView.MakeTmpInput(rowGo.transform, "CourtesyInput", "可空", charLimit: 4);
            var ciRt = (RectTransform)courtesyInput.transform;
            ciRt.anchorMin = new Vector2(0, 0);
            ciRt.anchorMax = new Vector2(0, 1);
            ciRt.pivot = new Vector2(0, 0.5f);
            ciRt.sizeDelta = new Vector2(180, 0);
            ciRt.anchoredPosition = new Vector2(370, 0);

            var pair = ACLS.Sim.Names.RandomPlayerName();
            nameInput.text = pair.Given;
            courtesyInput.text = pair.Courtesy;
        }

        private void BuildErrorLabel(float yFromTop)
        {
            errorText = UiKit.CreateText(card.transform, "Error", 17, TextAlignmentOptions.Left);
            errorText.color = new Color(1f, 0.45f, 0.45f, 1f);
            var errRt = (RectTransform)errorText.transform;
            errRt.anchorMin = new Vector2(0, 1);
            errRt.anchorMax = new Vector2(1, 1);
            errRt.pivot = new Vector2(0.5f, 1);
            errRt.offsetMin = new Vector2(28, -(yFromTop + 24));
            errRt.offsetMax = new Vector2(-28, -yFromTop);
            errorText.text = "";
        }

        private void BuildSexRow(float yFromTop)
        {
            var rowGo = new GameObject("SexRow", typeof(RectTransform));
            rowGo.transform.SetParent(card.transform, false);
            var rt = (RectTransform)rowGo.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(28, -(yFromTop + 44));
            rt.offsetMax = new Vector2(-28, -yFromTop);

            var sexLab = UiKit.CreateText(rowGo.transform, "SexLab", 19, TextAlignmentOptions.Left);
            var sexLabRt = (RectTransform)sexLab.transform;
            sexLabRt.anchorMin = new Vector2(0, 0);
            sexLabRt.anchorMax = new Vector2(0, 1);
            sexLabRt.pivot = new Vector2(0, 0.5f);
            sexLabRt.sizeDelta = new Vector2(76, 0);
            sexLabRt.anchoredPosition = new Vector2(0, 0);
            sexLab.text = "性别：";

            maleBtn = UiKit.CreateButton(rowGo.transform, "Male", "男", () => SelectSex(Sex.Male));
            var mRt = (RectTransform)maleBtn.transform;
            mRt.anchorMin = new Vector2(0, 0);
            mRt.anchorMax = new Vector2(0, 1);
            mRt.pivot = new Vector2(0, 0.5f);
            mRt.sizeDelta = new Vector2(96, 0);
            mRt.anchoredPosition = new Vector2(80, 0);

            femaleBtn = UiKit.CreateButton(rowGo.transform, "Female", "女", () => SelectSex(Sex.Female));
            var fRt = (RectTransform)femaleBtn.transform;
            fRt.anchorMin = new Vector2(0, 0);
            fRt.anchorMax = new Vector2(0, 1);
            fRt.pivot = new Vector2(0, 0.5f);
            fRt.sizeDelta = new Vector2(96, 0);
            fRt.anchoredPosition = new Vector2(184, 0);

            ApplySexHighlight();
        }

        private void BuildBackgroundLabel(float yFromTop)
        {
            var bgLabel = UiKit.CreateText(card.transform, "BgLabel", 21, TextAlignmentOptions.Left);
            var bgLabelRt = (RectTransform)bgLabel.transform;
            bgLabelRt.anchorMin = new Vector2(0, 1);
            bgLabelRt.anchorMax = new Vector2(1, 1);
            bgLabelRt.pivot = new Vector2(0.5f, 1);
            bgLabelRt.offsetMin = new Vector2(28, -(yFromTop + 24));
            bgLabelRt.offsetMax = new Vector2(-28, -yFromTop);
            bgLabel.text = "背景：";
        }

        private void BuildPresetCards(float yFromTop)
        {
            int n = CharacterPresets.All.Count;
            for (int i = 0; i < n; i++)
            {
                var preset = CharacterPresets.All[i];
                int captured = i;

                var go = new GameObject(
                    $"Preset_{preset.Id}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                go.transform.SetParent(card.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                float top = yFromTop + i * (PresetItemH + PresetSpacing);
                rt.offsetMin = new Vector2(28, -(top + PresetItemH));
                rt.offsetMax = new Vector2(-28, -top);

                go.GetComponent<Image>().color = UnselectedColor;
                go.GetComponent<Button>().onClick.AddListener(() => SelectPreset(captured));

                var titleT = UiKit.CreateText(go.transform, "PTitle", 21, TextAlignmentOptions.TopLeft);
                var titleRt = (RectTransform)titleT.transform;
                titleRt.anchorMin = new Vector2(0, 1);
                titleRt.anchorMax = new Vector2(1, 1);
                titleRt.pivot = new Vector2(0.5f, 1);
                titleRt.offsetMin = new Vector2(12, -32);
                titleRt.offsetMax = new Vector2(-12, -8);
                titleT.text = $"{preset.Title}　·　{preset.LocationName} / {preset.TraitLabel}";

                var blurbT = UiKit.CreateText(go.transform, "PBlurb", 17, TextAlignmentOptions.TopLeft);
                blurbT.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                var blurbRt = (RectTransform)blurbT.transform;
                blurbRt.anchorMin = new Vector2(0, 0);
                blurbRt.anchorMax = new Vector2(1, 1);
                blurbRt.offsetMin = new Vector2(12, 6);
                blurbRt.offsetMax = new Vector2(-12, -36);
                blurbT.text = preset.Blurb;

                presetBtns.Add(go.GetComponent<Button>());
            }

            ApplyPresetHighlight();
        }

        private void BuildStartButton(float yFromBottom)
        {
            var startBtn = UiKit.CreateButton(card.transform, "Start", "开始游戏", OnStartClicked);
            var rt = (RectTransform)startBtn.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(28, yFromBottom);
            rt.offsetMax = new Vector2(-28, yFromBottom + 48);
            startBtn.GetComponent<Image>().color = new Color(0.45f, 0.35f, 0.15f, 0.97f);
        }

        // -------- selection state --------

        private void SelectSex(Sex s)
        {
            selectedSex = s;
            ApplySexHighlight();
        }

        private void ApplySexHighlight()
        {
            if (maleBtn != null)
                maleBtn.GetComponent<Image>().color = selectedSex == Sex.Male ? SelectedColor : UnselectedColor;
            if (femaleBtn != null)
                femaleBtn.GetComponent<Image>().color = selectedSex == Sex.Female ? SelectedColor : UnselectedColor;
        }

        private void SelectPreset(int i)
        {
            selectedPresetIndex = i;
            ApplyPresetHighlight();
        }

        private void ApplyPresetHighlight()
        {
            for (int i = 0; i < presetBtns.Count; i++)
            {
                presetBtns[i].GetComponent<Image>().color =
                    (i == selectedPresetIndex) ? SelectedColor : UnselectedColor;
            }
        }

        // -------- submit --------

        private void OnStartClicked()
        {
            if (world == null || chat == null) return;
            string name = (nameInput?.text ?? "").Trim();
            if (name.Length == 0)
            {
                if (errorText != null) errorText.text = "※ 姓名不能为空";
                if (nameInput != null) nameInput.ActivateInputField();
                return;
            }
            string courtesy = (courtesyInput?.text ?? "").Trim();
            var preset = CharacterPresets.All[selectedPresetIndex];

            WorldFactory.ConfigurePlayer(
                world,
                name: name,
                courtesy: courtesy,
                sex: selectedSex,
                age: 22,
                locationName: preset.LocationName,
                traitId: preset.TraitId);

            if (dim != null) dim.SetActive(false);
            if (card != null) card.SetActive(false);

            stateMachine?.TransitionTo(GameState.Dialogue);
            chat.StartStageCreate(preset, _ => chat.StartOpening(preset));
        }
    }
}
