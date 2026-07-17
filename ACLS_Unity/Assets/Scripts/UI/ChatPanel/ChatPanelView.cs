using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Llm;
using ACLS.Logging;

namespace ACLS.UI
{
    public sealed class ChatPanelView : MonoBehaviour
    {
        private const float StatusBarHeight = 28f;
        private const float InputRowHeight = 88f;          // 输入框高度, 容纳 2-3 行中文
        private const float MinBlockInterval = 0.5f;   // 两个 block 显示完成之间最小间隔

        private ChatBridge bridge;
        private RectTransform historyContent;
        private ScrollRect scroll;
        private TextMeshProUGUI statusLabel;
        private TextMeshProUGUI thinkingLabel;
        private TextMeshProUGUI usageLabel;
        private TextMeshProUGUI toolActivityLabel;
        private ScrollRect thinkingScroll;
        private ScrollRect toolScroll;
        private TMP_InputField input;
        private Button sendBtn;
        private Button cancelBtn;
        private RectTransform choicesContainer;
        private RectTransform _scrollRt;
        private List<Button> choiceButtons = new List<Button>();

        // ── Block queue ──
        private readonly Queue<ChatBlock> _pending = new Queue<ChatBlock>();
        private ChatBlock _activeBlock;
        private TypewriterSlot _activeSlot;
        private float _lastDoneTime = float.NegativeInfinity;
        private Coroutine _gapWaiter;

        // ── Typewriter pool ──
        private readonly List<TypewriterSlot> _slotPool = new List<TypewriterSlot>();

        public void Bind(ChatBridge bridge)
        {
            this.bridge = bridge;

            BuildUi();

            if (bridge.History?.All != null)
                foreach (var m in bridge.History.All)
                {
                    // ToolCall / ToolResult are internal LLM plumbing — never
                    // surface them as chat rows, even on history replay.
                    if (m.Role == ChatRole.ToolCall || m.Role == ChatRole.ToolResult) continue;
                    PushBlock(ChatBlock.Static(m.Role, HeaderPrefixFor(m.Role), m.Content));
                }

            bridge.OnBlock += OnBlock;
            bridge.OnBusyChanged += OnBusyChanged;
            bridge.OnChoicesChanged += RefreshChoices;
            bridge.OnThinkingChanged += UpdateThinking;
            bridge.OnUsageReported += OnUsageReported;
            bridge.OnToolActivity += OnToolActivity;

            if (!bridge.Ready)
            {
                PushBlock(ChatBlock.Static(ChatRole.System, "系统",
                    string.IsNullOrEmpty(bridge.ConfigError)
                        ? "未找到 LlmConfig。请到 Assets/Resources/ 下创建 ACLS/LLM Config 资产并填 ApiKey。"
                        : bridge.ConfigError));
                SetInteractable(false);
            }

            RefreshChoices(bridge.CurrentChoices);
            UpdateStatusLabel();
            UpdateThinking(bridge.CurrentThinking);
        }

        private void OnDestroy()
        {
            if (_gapWaiter != null) { StopCoroutine(_gapWaiter); _gapWaiter = null; }
            for (int i = _slotPool.Count - 1; i >= 0; i--)
            {
                if (_slotPool[i] != null) Destroy(_slotPool[i].gameObject);
            }
            _slotPool.Clear();
            _activeBlock = null;
            _activeSlot = null;
            _pending.Clear();

            if (bridge != null)
            {
                bridge.OnBlock -= OnBlock;
                bridge.OnBusyChanged -= OnBusyChanged;
                bridge.OnChoicesChanged -= RefreshChoices;
                bridge.OnThinkingChanged -= UpdateThinking;
                bridge.OnUsageReported -= OnUsageReported;
                bridge.OnToolActivity -= OnToolActivity;
            }
        }

        private void Update()
        {
            if (input == null || bridge == null) return;
            if (string.IsNullOrEmpty(input.text)) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                TrySubmit();
            }
        }

        // -------- UI construction --------

        private void BuildUi()
        {
            var bg = UiKit.CreatePanel(transform, "Bg",
                Vector2.zero, Vector2.one, new Color(0.10f, 0.10f, 0.14f, 0.92f));
            bg.transform.SetAsFirstSibling();

            var left = new GameObject("Left", typeof(RectTransform));
            left.transform.SetParent(transform, false);
            var leftRt = (RectTransform)left.transform;
            leftRt.anchorMin = new Vector2(0, 0);
            leftRt.anchorMax = new Vector2(0.6f, 1);
            leftRt.offsetMin = Vector2.zero;
            leftRt.offsetMax = Vector2.zero;

            // Right side: split vertically. Top ~60% is Thinking (LLM
            // reasoning stream + token usage footer). Bottom ~40% is the
            // Tool Activity panel — a dedicated monitor for tool calls and
            // their results, independent of the thinking stream.
            var right = new GameObject("Right", typeof(RectTransform));
            right.transform.SetParent(transform, false);
            var rightRt = (RectTransform)right.transform;
            rightRt.anchorMin = new Vector2(0.6f, 0);
            rightRt.anchorMax = new Vector2(1, 1);
            rightRt.offsetMin = Vector2.zero;
            rightRt.offsetMax = Vector2.zero;

            var thinkingPanel = NewFullPanel("ThinkingPanel", right.transform, new Vector2(0, 0.4f), new Vector2(1, 1));
            var toolPanel = NewFullPanel("ToolPanel", right.transform, new Vector2(0, 0), new Vector2(1, 0.4f));

            BuildHistory(left.transform);
            BuildStatusBar(left.transform);
            BuildChoicesRow(left.transform);
            BuildInputRow(left.transform);
            BuildThinking(thinkingPanel.transform);
            BuildToolPanel(toolPanel.transform);
        }

        private static GameObject NewFullPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private void BuildHistory(Transform parent)
        {
            var scrollGo = new GameObject("History",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            _scrollRt = scrollRt;
            scrollRt.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight + 8);
            scrollRt.offsetMax = new Vector2(-8, -8);
            scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.9f);

            scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(8, 8);
            vpRt.offsetMax = new Vector2(-8, -8);
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            vpGo.GetComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = vpRt;

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            historyContent = (RectTransform)contentGo.transform;
            historyContent.anchorMin = new Vector2(0, 1);
            historyContent.anchorMax = new Vector2(1, 1);
            historyContent.pivot = new Vector2(0.5f, 1);
            historyContent.anchoredPosition = Vector2.zero;
            historyContent.sizeDelta = new Vector2(0, 0);
            scroll.content = historyContent;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildStatusBar(Transform parent)
        {
            statusLabel = UiKit.CreateText(parent, "Status", 17, TextAlignmentOptions.Left);
            var rt = (RectTransform)statusLabel.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(8, InputRowHeight);
            rt.offsetMax = new Vector2(-8, InputRowHeight + StatusBarHeight);
            statusLabel.color = new Color(0.7f, 0.7f, 0.78f, 1f);
        }

        private void BuildChoicesRow(Transform parent)
        {
            var rowGo = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rowGo.transform.SetParent(parent, false);
            choicesContainer = (RectTransform)rowGo.transform;
            choicesContainer.anchorMin = new Vector2(0, 0);
            choicesContainer.anchorMax = new Vector2(1, 0);
            choicesContainer.pivot = new Vector2(0.5f, 0);
            choicesContainer.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight);
            choicesContainer.offsetMax = new Vector2(-8, InputRowHeight + StatusBarHeight);   // 初始零高，ContentSizeFitter 自动撑开

            var vlg = rowGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(16, 16, 6, 6);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = rowGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RefreshChoices(IReadOnlyList<LlmReply.Choice> choices)
        {
            Log.Debug(Log.Channels.UI, $"[Timing] RefreshChoices activeSlot={(_activeSlot != null ? "yes" : "no")} IsDone={(_activeSlot?.IsDone ?? false)} t={Time.unscaledTime:F3} lastDone={_lastDoneTime:F3}");
            // 在打字机期间 buffer 住 choices，等 active block 完成后在 OnSlotDone 里消费
            if (_activeSlot != null && !_activeSlot.IsDone)
            {
                _pendingChoicesToShow = choices;
                return;
            }
            ApplyChoices(choices);
        }

        private IReadOnlyList<LlmReply.Choice> _pendingChoicesToShow;

        private void OnBusyChanged(bool busy)
        {
            SetInteractable(!busy && bridge.Ready);
            sendBtn.gameObject.SetActive(!busy);
            cancelBtn.gameObject.SetActive(busy);
            UpdateStatusLabel();
            if (!busy) input.ActivateInputField();
        }

        private void ApplyChoices(IReadOnlyList<LlmReply.Choice> choices)
        {
            Log.Debug(Log.Channels.UI, $"[Timing] ApplyChoices t={Time.unscaledTime:F3} lastDone={_lastDoneTime:F3} diff={(Time.unscaledTime - _lastDoneTime):F3}s count={choices?.Count ?? 0}");
            // Always tear down the old buttons first, so a (null / empty) new
            // payload clears any leftover label from the previous turn instead
            // of leaving orphan Text children on screen.
            for (int i = choicesContainer.childCount - 1; i >= 0; i--)
                Destroy(choicesContainer.GetChild(i).gameObject);
            choiceButtons.Clear();

            if (choices == null || choices.Count == 0)
            {
                choicesContainer.gameObject.SetActive(false);
                if (_scrollRt != null)
                    _scrollRt.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight + 8);
                return;
            }
            choicesContainer.gameObject.SetActive(true);

            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var label = choices[i].Label;
                string prefix = ((char)('A' + i)).ToString() + ". ";
                var btn = UiKit.CreateButton(choicesContainer, "Choice", prefix + label, () => OnChoiceClicked(index));
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 36f;
                le.preferredHeight = 36f;
                var labelTmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (labelTmp != null)
                {
                    labelTmp.enableWordWrapping = true;
                    labelTmp.alignment = TextAlignmentOptions.Left;
                }
                choiceButtons.Add(btn);
            }

            bool active = bridge.Ready && !bridge.Busy;
            foreach (var b in choiceButtons) b.interactable = active;

            // 强制重建布局让 ContentSizeFitter 撑开容器高度，然后更新滚动区域
            Canvas.ForceUpdateCanvases();
            float containerHeight = choicesContainer.rect.height;
            if (_scrollRt != null)
                _scrollRt.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight + containerHeight + 8);
        }

        private void OnChoiceClicked(int index)
        {
            if (bridge == null || !bridge.Ready || bridge.Busy) return;
            // 点击后立即隐藏选项
            choicesContainer.gameObject.SetActive(false);
            if (_scrollRt != null)
                _scrollRt.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight + 8);
            bridge.Choose(index);
        }

        private void BuildInputRow(Transform parent)
        {
            var rowGo = new GameObject("InputRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.anchorMin = new Vector2(0, 0);
            rowRt.anchorMax = new Vector2(1, 0);
            rowRt.pivot = new Vector2(0.5f, 0);
            rowRt.offsetMin = new Vector2(8, 8);
            rowRt.offsetMax = new Vector2(-8, InputRowHeight);

            input = MakeTmpInput(rowGo.transform, "Input", "输入你的行动…", charLimit: 240);
            var inputRt = (RectTransform)input.transform;
            inputRt.anchorMin = new Vector2(0, 0);
            inputRt.anchorMax = new Vector2(1, 1);
            inputRt.offsetMin = new Vector2(0, 0);
            inputRt.offsetMax = new Vector2(-92, 0);

            sendBtn = UiKit.CreateButton(rowGo.transform, "Send", "发送", TrySubmit);
            var sendRt = (RectTransform)sendBtn.transform;
            sendRt.anchorMin = new Vector2(1, 0);
            sendRt.anchorMax = new Vector2(1, 1);
            sendRt.pivot = new Vector2(1, 0.5f);
            sendRt.sizeDelta = new Vector2(86, 0);
            sendRt.anchoredPosition = new Vector2(0, 0);

            cancelBtn = UiKit.CreateButton(rowGo.transform, "Cancel", "中断", OnCancelClicked);
            var cancelRt = (RectTransform)cancelBtn.transform;
            cancelRt.anchorMin = new Vector2(1, 0);
            cancelRt.anchorMax = new Vector2(1, 1);
            cancelRt.pivot = new Vector2(1, 0.5f);
            cancelRt.sizeDelta = new Vector2(86, 0);
            cancelRt.anchoredPosition = new Vector2(0, 0);
            cancelBtn.gameObject.SetActive(false);
        }

        private void BuildThinking(Transform parent)
        {
            var bg = UiKit.CreatePanel(parent, "Bg",
                Vector2.zero, Vector2.one, new Color(0.05f, 0.05f, 0.08f, 0.9f));
            bg.transform.SetAsFirstSibling();

            var title = UiKit.CreateText(parent, "Title", 17, TextAlignmentOptions.Left);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.offsetMin = new Vector2(8, -StatusBarHeight);
            titleRt.offsetMax = new Vector2(-8, 0);
            title.color = new Color(0.7f, 0.7f, 0.78f, 1f);
            title.text = "THINKING";

            var scrollGo = new GameObject("ThinkingScroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(8, 8);
            scrollRt.offsetMax = new Vector2(-8, -StatusBarHeight - 8);
            scrollGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            thinkingScroll = scrollGo.GetComponent<ScrollRect>();
            thinkingScroll.horizontal = false;
            thinkingScroll.vertical = true;
            thinkingScroll.movementType = ScrollRect.MovementType.Clamped;
            thinkingScroll.scrollSensitivity = 30f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(8, 8);
            vpRt.offsetMax = new Vector2(-8, -8);
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            vpGo.GetComponent<Mask>().showMaskGraphic = false;
            thinkingScroll.viewport = vpRt;

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, 0);
            thinkingScroll.content = contentRt;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            textGo.transform.SetParent(contentGo.transform, false);
            thinkingLabel = textGo.GetComponent<TextMeshProUGUI>();
            thinkingLabel.font = UiKit.TmpFont;
            thinkingLabel.fontSize = 17;
            thinkingLabel.color = new Color(0.9f, 0.9f, 0.95f, 1f);
            thinkingLabel.alignment = TextAlignmentOptions.TopLeft;
            thinkingLabel.richText = false;
            thinkingLabel.enableWordWrapping = true;
            thinkingLabel.overflowMode = TextOverflowModes.Overflow;
            thinkingLabel.text = "";

            var textCsf = textGo.GetComponent<ContentSizeFitter>();
            textCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            textCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Usage label: token / time stats, appended below the thinking
            //    body. Lives inside ThinkingScroll.Content so it scrolls with
            //    the thinking stream, but never appears in the main ChatPanel.
            var usageGo = new GameObject("Usage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            usageGo.transform.SetParent(contentGo.transform, false);
            usageLabel = usageGo.GetComponent<TextMeshProUGUI>();
            usageLabel.font = UiKit.TmpFont;
            usageLabel.fontSize = 13;
            usageLabel.color = new Color(0.55f, 0.55f, 0.6f, 1f);
            usageLabel.alignment = TextAlignmentOptions.TopRight;
            usageLabel.richText = false;
            usageLabel.enableWordWrapping = false;
            usageLabel.overflowMode = TextOverflowModes.Overflow;
            usageLabel.text = "";

            var usageCsf = usageGo.GetComponent<ContentSizeFitter>();
            usageCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            usageCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // Lower half of the right side. Dedicated monitor for tool calls —
        // each line shows LLM's request ("▶ name(args)") followed by the
        // tool's result ("◀ name → result"). Updated independently of the
        // Thinking stream so a busy tool loop doesn't crowd out the reasoning.
        private void BuildToolPanel(Transform parent)
        {
            var bg = UiKit.CreatePanel(parent, "Bg",
                Vector2.zero, Vector2.one, new Color(0.05f, 0.05f, 0.08f, 0.9f));
            bg.transform.SetAsFirstSibling();

            var title = UiKit.CreateText(parent, "Title", 17, TextAlignmentOptions.Left);
            var titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.offsetMin = new Vector2(8, -StatusBarHeight);
            titleRt.offsetMax = new Vector2(-8, 0);
            title.color = new Color(0.65f, 0.78f, 0.95f, 1f);
            title.text = "TOOL ACTIVITY";

            var scrollGo = new GameObject("ToolScroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(8, 8);
            scrollRt.offsetMax = new Vector2(-8, -StatusBarHeight - 8);
            scrollGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            toolScroll = scrollGo.GetComponent<ScrollRect>();
            toolScroll.horizontal = false;
            toolScroll.vertical = true;
            toolScroll.movementType = ScrollRect.MovementType.Clamped;
            toolScroll.scrollSensitivity = 30f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(8, 8);
            vpRt.offsetMax = new Vector2(-8, -8);
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            vpGo.GetComponent<Mask>().showMaskGraphic = false;
            toolScroll.viewport = vpRt;

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, 0);
            toolScroll.content = contentRt;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Single TMP that accumulates one line per tool event.
            var toolGo = new GameObject("ToolActivity",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            toolGo.transform.SetParent(contentGo.transform, false);
            toolActivityLabel = toolGo.GetComponent<TextMeshProUGUI>();
            toolActivityLabel.font = UiKit.TmpFont;
            toolActivityLabel.fontSize = 13;
            toolActivityLabel.color = new Color(0.7f, 0.82f, 0.95f, 1f);
            toolActivityLabel.alignment = TextAlignmentOptions.TopLeft;
            toolActivityLabel.richText = false;
            toolActivityLabel.enableWordWrapping = true;
            toolActivityLabel.overflowMode = TextOverflowModes.Overflow;
            toolActivityLabel.text = "";

            var toolCsf = toolGo.GetComponent<ContentSizeFitter>();
            toolCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            toolCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // -------- behavior --------

        private void TrySubmit()
        {
            if (bridge == null || !bridge.Ready || bridge.Busy) return;
            string text = input.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            input.text = "";
            input.ActivateInputField();
            bridge.SubmitFreeform(text);
        }

        private void SetInteractable(bool on)
        {
            input.interactable = on;
            sendBtn.interactable = on;
            foreach (var b in choiceButtons) b.interactable = on;
        }

        private void OnCancelClicked()
        {
            if (bridge == null || !bridge.Busy) return;
            bridge.CancelCurrent();
        }

        private void UpdateStatusLabel()
        {
            if (statusLabel == null) return;
            if (bridge == null || !bridge.Ready)
            {
                statusLabel.text = "<color=#ff8888>LLM 未配置</color>";
                return;
            }
            if (bridge.Busy)
            {
                statusLabel.text = "<color=#f5d57a>思考中…</color>";
                return;
            }
            int n = bridge.CurrentChoices?.Count ?? 0;
            statusLabel.text = n > 0
                ? $"<color=#7c7c8a>请选择行动（{n} 项）</color>"
                : "<color=#7c7c8a>就绪</color>";
        }

        private void UpdateThinking(string text)
        {
            if (thinkingLabel == null) return;
            thinkingLabel.text = text ?? "";
            ScrollThinkingToBottom();
        }

        // Append one line of usage meta to the Thinking panel. We keep a running
        // history here (newline-separated) instead of overwriting, so multiple
        // LLM rounds in a single tool loop are all visible.
        private void OnUsageReported(LlmUsage last, LlmUsage cumulative)
        {
            if (usageLabel == null) return;
            if (!last.HasData) return;
            float rt = ChatBridge_LastResponseTime();
            string rtSuffix = rt > 0f ? $" · {rt:F1}s" : "";
            string line = $"in {last.InputTokens} · out {last.OutputTokens} · ∑{cumulative.Total}{rtSuffix}";
            if (string.IsNullOrEmpty(usageLabel.text))
                usageLabel.text = line;
            else
                usageLabel.text = usageLabel.text + "\n" + line;
            ScrollThinkingToBottom();
        }

        // Append a tool activity line. Each LLM tool call fires this twice:
        // once with result="" right after the LLM requests the tool, and once
        // with the actual result string after the tool runs. We render them
        // in place so the player sees the call → result pair.
        private void OnToolActivity(int round, string toolName, string args, string result)
        {
            if (toolActivityLabel == null) return;
            if (string.IsNullOrEmpty(toolName)) return;
            string shortArgs = TruncateForActivity(args, 80);
            string line;
            if (string.IsNullOrEmpty(result))
            {
                // Request line — colored as a request marker.
                line = $"R{round} ▶ {toolName}({shortArgs})";
            }
            else
            {
                string shortResult = TruncateForActivity(result, 120);
                line = $"R{round} ◀ {toolName} → {shortResult}";
            }
            if (string.IsNullOrEmpty(toolActivityLabel.text))
                toolActivityLabel.text = line;
            else
                toolActivityLabel.text = toolActivityLabel.text + "\n" + line;
            ScrollToolToBottom();
        }

        private static string TruncateForActivity(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // Small bridge accessor so OnUsageReported doesn't have to know about
        // ChatBridge's internal property naming.
        private float ChatBridge_LastResponseTime()
        {
            return bridge != null ? bridge.LastResponseTime : 0f;
        }

        // ── Block pipeline ──

        private void OnBlock(ChatBlock block)
        {
            // Defensive guard: tool plumbing never makes it into the panel,
            // regardless of who subscribes upstream.
            if (block.Role == ChatRole.ToolCall || block.Role == ChatRole.ToolResult) return;

            // Skip blocks with no displayable content. Streaming blocks may push
            // an empty text on the first tick (the typewriter then ignores them);
            // Static blocks that arrive empty would otherwise render a blank Msg.
            if (block.IsStreaming)
            {
                if (string.IsNullOrWhiteSpace(block.CurrentStreamText)) return;
            }
            else if (string.IsNullOrWhiteSpace(block.StaticText))
            {
                return;
            }

            // Re-tick of the active block (data changed) — typewriter's coroutine
            // polls the block each frame, so no action needed here.
            if (block == _activeBlock) return;

            // Active block exists — queue this one behind it
            if (_activeBlock != null)
            {
                _pending.Enqueue(block);
                return;
            }

            // No active block — check min interval from last done
            TryStartBlock(block);
        }

        private void TryStartBlock(ChatBlock block)
        {
            float elapsed = Time.unscaledTime - _lastDoneTime;
            float gap = MinBlockInterval - elapsed;
            if (gap > 0)
            {
                if (_gapWaiter != null) StopCoroutine(_gapWaiter);
                _gapWaiter = StartCoroutine(StartAfterGap(gap, block));
                return;
            }
            DoStartBlock(block);
        }

        private IEnumerator StartAfterGap(float delay, ChatBlock block)
        {
            yield return new WaitForSecondsRealtime(delay);
            _gapWaiter = null;
            // Re-check: maybe another block started in the meantime
            if (_activeBlock == null)
                DoStartBlock(block);
        }

        private void DoStartBlock(ChatBlock block)
        {
            string header = FormatHeader(block);
            var tmp = CreateMessageItem(header + "\n");

            if (!block.IsStreaming)
            {
                // Static: 直接写入整段文本,无打字机
                tmp.text = header + "\n" + block.StaticText;
                // 静态 block 视为立即完成 — 走 OnBlockDone 走完队列/间隔
                StartCoroutine(StaticBlockDoneNextFrame(block));
                return;
            }

            // Streaming: 取一个打字机槽
            var slot = RentSlot();
            slot.Assign(tmp, header, block);
            slot.OnDone += OnSlotDone;
            _activeBlock = block;
            _activeSlot = slot;
            ScrollToBottom();
        }

        private IEnumerator StaticBlockDoneNextFrame(ChatBlock block)
        {
            _activeBlock = block;
            yield return null;
            OnBlockFinished();
        }

        private void OnSlotDone(TypewriterSlot slot)
        {
            slot.OnDone -= OnSlotDone;
            ReturnSlot(slot);
            OnBlockFinished();
        }

        private void OnBlockFinished()
        {
            float blockDoneTime = Time.unscaledTime;
            Log.Debug(Log.Channels.UI, $"[Timing] OnBlockFinished blockDone={blockDoneTime:F3} hasPending={_pendingChoicesToShow != null}");
            _activeBlock = null;
            _activeSlot = null;
            _lastDoneTime = blockDoneTime;
            ScrollToBottom();

            // Flush buffered choices if any arrived during the active block
            if (_pendingChoicesToShow != null)
            {
                var toShow = _pendingChoicesToShow;
                _pendingChoicesToShow = null;
                float flushAt = Time.unscaledTime;
                float gap = flushAt - blockDoneTime;
                Log.Debug(Log.Channels.UI, $"[Timing] ===> Choice-shown gap: {gap:F3}s after block done. (count={toShow?.Count ?? 0})");
                ApplyChoices(toShow);
            }

            if (_pending.Count > 0)
                TryStartBlock(_pending.Dequeue());
        }

        // ── Header formatting ──

        private static string HeaderPrefixFor(ChatRole role) => role switch
        {
            ChatRole.Assistant => "旁白",
            ChatRole.User => "你",
            ChatRole.System => "系统",
            _ => "",
        };

        private string FormatHeader(ChatBlock block)
        {
            string prefix = ColorizePrefix(block.HeaderPrefix, block.Role);
            string ts = $"<color=#cccccc><size=12>{block.Timestamp:HH:mm:ss}</size></color>";
            return $"{prefix} {ts}";
        }

        private static string ColorizePrefix(string prefix, ChatRole role)
        {
            if (string.IsNullOrEmpty(prefix)) return "";
            return role switch
            {
                ChatRole.Assistant => $"<color=#f5d57a><b>{prefix}</b></color>",
                ChatRole.User => $"<color=#7fb6ff><b>{prefix}</b></color>",
                ChatRole.System => $"<color=#ff8888><b>{prefix}</b></color>",
                _ => prefix,
            };
        }

        // ── TMP item creation ──

        private TextMeshProUGUI CreateMessageItem(string richText)
        {
            var go = new GameObject("Msg",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            go.transform.SetParent(historyContent, false);

            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = UiKit.TmpFont;
            t.fontSize = 19;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.richText = true;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.text = richText;

            var csf = go.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return t;
        }

        // ── Typewriter pool ──

        private TypewriterSlot RentSlot()
        {
            foreach (var s in _slotPool)
            {
                if (!s.gameObject.activeInHierarchy) return s;
            }
            var go = new GameObject("TypewriterSlot", typeof(TypewriterSlot));
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var slot = go.GetComponent<TypewriterSlot>();
            _slotPool.Add(slot);
            return slot;
        }

        private void ReturnSlot(TypewriterSlot slot)
        {
            slot.gameObject.SetActive(false);
        }

        // ── PushBlock is for history replay only (Bind-time) ──

        private void PushBlock(ChatBlock block)
        {
            // History replay: 直接生成完整 Msg TMP,不进队列不打字机
            string header = FormatHeader(block);
            CreateMessageItem(header + "\n" + (block.StaticText ?? ""));
            ScrollToBottom();
        }

        // ── Scrolling ──

        private void ScrollThinkingToBottom()
        {
            if (thinkingScroll == null) return;
            StartCoroutine(ScrollThinkingNextFrame());
        }

        private IEnumerator ScrollThinkingNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            thinkingScroll.verticalNormalizedPosition = 0f;
        }

        private void ScrollToolToBottom()
        {
            if (toolScroll == null) return;
            StartCoroutine(ScrollToolNextFrame());
        }

        private IEnumerator ScrollToolNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            toolScroll.verticalNormalizedPosition = 0f;
        }

        private void ScrollToBottom()
        {
            StartCoroutine(ScrollNextFrame());
        }

        private IEnumerator ScrollNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        // ── TMP_InputField builder (unchanged) ──

        internal static TMP_InputField MakeTmpInput(Transform parent, string name, string placeholder, int charLimit)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.10f, 1f);

            var field = go.GetComponent<TMP_InputField>();
            field.lineType = TMP_InputField.LineType.MultiLineNewline;   // allow enter for newline, not submit
            field.characterLimit = charLimit;
            field.richText = false;  // 输入的 <b> 等标签必须当纯文本显示, 不可被解析
            field.restoreOriginalTextOnEscape = true;

            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(go.transform, false);
            var areaRt = (RectTransform)areaGo.transform;
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(10, 4);
            areaRt.offsetMax = new Vector2(-10, -4);
            field.textViewport = areaRt;

            var phGo = new GameObject("Placeholder",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            phGo.transform.SetParent(areaGo.transform, false);
            var phRt = (RectTransform)phGo.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.font = UiKit.TmpFont;
            ph.fontSize = 19;
            ph.color = new Color(1, 1, 1, 0.35f);
            ph.alignment = TextAlignmentOptions.TopLeft;
            ph.enableWordWrapping = true;   // placeholder wraps like real input
            ph.richText = false;             // placeholder 也不解析 rich tags
            ph.text = placeholder;
            field.placeholder = ph;

            var textGo = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(areaGo.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var t = textGo.GetComponent<TextMeshProUGUI>();
            t.font = UiKit.TmpFont;
            t.fontSize = 19;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.enableWordWrapping = true;   // wrap on multi-line so 输入框高度用上
            t.richText = false;             // 用户输入的 <b> 等符号当纯文本显示
            field.textComponent = t;

            return field;
        }
    }
}
