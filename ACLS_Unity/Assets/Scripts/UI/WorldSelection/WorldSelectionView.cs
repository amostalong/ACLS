using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Sim;

namespace ACLS.UI
{
    public sealed class WorldSelectionView : MonoBehaviour
    {
        private const float CardW = 760f;
        private const float CardH = 1170f;       // 原本 780，增到 1.5 倍
        private const float PresetItemH = 132f;  // 原本 88，增到 1.5 倍
        private const float PresetSpacing = 8f;
        private const float CustomInputH = 100f;

        private static readonly Color SelectedColor   = new Color(0.55f, 0.42f, 0.18f, 0.97f);
        private static readonly Color UnselectedColor = new Color(0.22f, 0.22f, 0.28f, 0.95f);

        private World world;
        private ChatBridge chat;
        private GameStateMachine stateMachine;
        private Action onWorldBuilt;

        private GameObject dim;
        private GameObject card;
        private TextMeshProUGUI errorText;
        private TMP_InputField customInput;
        private GameObject customInputGo;
        private readonly List<Button> presetBtns = new List<Button>();

        private int selectedPresetIndex = 0;

        public void Bind(World world, ChatBridge chat, GameStateMachine stateMachine, Action onWorldBuilt)
        {
            this.world = world;
            this.chat = chat;
            this.stateMachine = stateMachine;
            this.onWorldBuilt = onWorldBuilt;
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
            dim = UiKit.CreatePanel(transform, "WsDim", Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.6f));

            card = new GameObject("WsCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(transform, false);
            var rt = (RectTransform)card.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CardW, CardH);
            card.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.17f, 0.97f);

            BuildTitle();
            BuildPresetList(yFromTop: 58f);
            BuildErrorLabel(yFromTop: 700f);
            BuildNextButton(yFromBottom: 16f);
        }

        private void BuildTitle()
        {
            var t = UiKit.CreateText(card.transform, "WsTitle", 31, TextAlignmentOptions.Center);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 50);
            rt.anchoredPosition = new Vector2(0, -8);
            t.text = "选择你的世界";
        }

        private void BuildPresetList(float yFromTop)
        {
            int n = WorldPresets.All.Count;
            for (int i = 0; i < n; i++)
            {
                var preset = WorldPresets.All[i];
                int captured = i;
                float top = yFromTop + i * (PresetItemH + PresetSpacing);

                var go = new GameObject($"WsPreset_{preset.Id}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(card.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                float itemH = preset.IsCustom ? PresetItemH + CustomInputH + 4f : PresetItemH;
                rt.offsetMin = new Vector2(28, -(top + itemH));
                rt.offsetMax = new Vector2(-28, -top);
                go.GetComponent<Image>().color = UnselectedColor;
                go.GetComponent<Button>().onClick.AddListener(() => SelectPreset(captured));

                var titleT = UiKit.CreateText(go.transform, "PTitle", 21, TextAlignmentOptions.TopLeft);
                var titleRt = (RectTransform)titleT.transform;
                titleRt.anchorMin = new Vector2(0, 1); titleRt.anchorMax = new Vector2(1, 1);
                titleRt.pivot = new Vector2(0.5f, 1);
                titleRt.offsetMin = new Vector2(12, -34); titleRt.offsetMax = new Vector2(-12, -8);
                titleT.text = $"{preset.Title}　·　{preset.Era}";

                // 描述区：ScrollRect + TextMeshProUGUI。文字多时可滚动。
                var scrollGo = new GameObject("PDescScroll",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
                scrollGo.transform.SetParent(go.transform, false);
                var scrollRt = (RectTransform)scrollGo.transform;
                scrollRt.anchorMin = new Vector2(0, 0);
                scrollRt.anchorMax = new Vector2(1, 1);
                scrollRt.offsetMin = new Vector2(12, preset.IsCustom ? CustomInputH + 8f : 6f);
                scrollRt.offsetMax = new Vector2(-12, -36);
                scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
                var sr = scrollGo.GetComponent<ScrollRect>();
                sr.horizontal = false;
                sr.vertical = true;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 40f;

                var vpGo = new GameObject("VP",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
                vpGo.transform.SetParent(scrollGo.transform, false);
                var vpRt = (RectTransform)vpGo.transform;
                vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
                vpRt.offsetMin = new Vector2(6, 6); vpRt.offsetMax = new Vector2(-6, -6);
                vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
                vpGo.GetComponent<Mask>().showMaskGraphic = false;
                sr.viewport = vpRt;

                var contentGo = new GameObject("Content",
                    typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
                contentGo.transform.SetParent(vpGo.transform, false);
                var contentRt = (RectTransform)contentGo.transform;
                contentRt.anchorMin = new Vector2(0, 1);
                contentRt.anchorMax = new Vector2(1, 1);
                contentRt.pivot = new Vector2(0.5f, 1);
                contentRt.anchoredPosition = Vector2.zero;
                contentRt.sizeDelta = Vector2.zero;
                sr.content = contentRt;

                var descT = contentGo.GetComponent<TextMeshProUGUI>();
                descT.font = UiKit.TmpFont;
                descT.fontSize = 17;
                descT.color = new Color(0.82f, 0.82f, 0.82f, 1f);
                descT.alignment = TextAlignmentOptions.TopLeft;
                descT.enableWordWrapping = true;
                descT.overflowMode = TextOverflowModes.Overflow;
                descT.richText = false;
                descT.text = preset.Description ?? preset.Blurb;

                var csf = contentGo.GetComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Custom world: embed a text input inside the card item.
                if (preset.IsCustom)
                {
                    customInputGo = new GameObject("CustomInputGo", typeof(RectTransform));
                    customInputGo.transform.SetParent(go.transform, false);
                    var ciRt = (RectTransform)customInputGo.transform;
                    ciRt.anchorMin = new Vector2(0, 0); ciRt.anchorMax = new Vector2(1, 0);
                    ciRt.pivot = new Vector2(0.5f, 0);
                    ciRt.offsetMin = new Vector2(12, 8); ciRt.offsetMax = new Vector2(-12, 8 + CustomInputH);
                    customInput = ChatPanelView.MakeTmpInput(customInputGo.transform, "CustomInput",
                        "例如：架空武侠世界，宋代风格，江湖门派林立…", charLimit: 200);
                    var inputRt = (RectTransform)customInput.transform;
                    inputRt.anchorMin = Vector2.zero; inputRt.anchorMax = Vector2.one;
                    inputRt.offsetMin = Vector2.zero; inputRt.offsetMax = Vector2.zero;
                    customInputGo.SetActive(false);
                }

                presetBtns.Add(go.GetComponent<Button>());
            }
            ApplyPresetHighlight();
        }

        private void BuildErrorLabel(float yFromTop)
        {
            errorText = UiKit.CreateText(card.transform, "WsError", 17, TextAlignmentOptions.Center);
            errorText.color = new Color(1f, 0.45f, 0.45f, 1f);
            var rt = (RectTransform)errorText.transform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(28, -(yFromTop + 24)); rt.offsetMax = new Vector2(-28, -yFromTop);
            errorText.text = "";
        }

        private void BuildNextButton(float yFromBottom)
        {
            var btn = UiKit.CreateButton(card.transform, "WsNext", "下一步", OnNextClicked);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(28, yFromBottom); rt.offsetMax = new Vector2(-28, yFromBottom + 48);
            btn.GetComponent<Image>().color = new Color(0.45f, 0.35f, 0.15f, 0.97f);
        }

        // -------- selection --------

        private void SelectPreset(int i)
        {
            selectedPresetIndex = i;
            ApplyPresetHighlight();
            if (customInputGo != null)
                customInputGo.SetActive(WorldPresets.All[i].IsCustom);
        }

        private void ApplyPresetHighlight()
        {
            for (int i = 0; i < presetBtns.Count; i++)
                presetBtns[i].GetComponent<Image>().color =
                    i == selectedPresetIndex ? SelectedColor : UnselectedColor;
        }

        // -------- submit --------

        private void OnNextClicked()
        {
            if (world == null || chat == null) return;

            var preset = WorldPresets.All[selectedPresetIndex];
            string desc;
            if (preset.IsCustom)
            {
                desc = (customInput?.text ?? "").Trim();
                if (desc.Length == 0) desc = WorldPresets.All[0].Blurb; // fallback to 三国
            }
            else
            {
                desc = preset.Blurb;
            }

            world.Stage.WorldDescription = desc;
            world.Stage.SelectedPresetId = preset.Id ?? "";
            if (errorText != null) errorText.text = "";

            SetVisible(false);

            // Fire world build, then hand off to character creation via callback.
            chat.StartWorldBuild("", desc, success =>
            {
                if (!success && errorText != null)
                    errorText.text = "※ 世界构建失败，将以默认设置继续";
                // EraTrend 锚点表按预设加载：三国剧本加载史实锚点，其他剧本加载空表。
                if (world != null)
                {
                    var go = gameObject;
                    if (go != null && go.GetComponent<EraTrendInjector>() == null)
                        go.AddComponent<EraTrendInjector>().Bind(world, world.Stage.SelectedPresetId);
                }
                stateMachine?.TransitionTo(GameState.CharacterCreation);
                onWorldBuilt?.Invoke();
            });
        }
    }
}
