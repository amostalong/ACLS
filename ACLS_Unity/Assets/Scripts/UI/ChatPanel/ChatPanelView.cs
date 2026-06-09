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
        private const float InputRowHeight = 56f;
        private const float ChoicesRowHeight = 56f;

        private ChatBridge bridge;
        private RectTransform historyContent;
        private ScrollRect scroll;
        private TextMeshProUGUI statusLabel;
        private TextMeshProUGUI thinkingLabel;
        private ScrollRect thinkingScroll;
        private TMP_InputField input;
        private Button sendBtn;
        private Button cancelBtn;
        private RectTransform choicesContainer;
        private List<Button> choiceButtons = new List<Button>();
        private TextMeshProUGUI streamingMsg;
        private int streamingGen;
        private string streamingTimestamp;  // 当前流式消息的开始时间

        // ── 等待动画状态 ──
        private Coroutine loadingDotsCoroutine;

        // ── 打字机状态 ──
        // 基础间隔；通过 minInterval 防止短消息播得太慢
        private const float TypewriterCharInterval = 0.025f;
        private Coroutine typewriterCoroutine;
        private string typewriterTarget;
        private IReadOnlyList<LlmReply.Choice> pendingChoices;
        private bool isTypewriting;

        public void Bind(ChatBridge bridge)
        {
            this.bridge = bridge;

            BuildUi();

            if (bridge.History?.All != null)
                foreach (var m in bridge.History.All)
                    AppendMessage(m);

            bridge.OnMessage += AppendMessage;
            bridge.OnBusyChanged += OnBusyChanged;
            bridge.OnChoicesChanged += RefreshChoices;
            bridge.OnThinkingChanged += UpdateThinking;
            bridge.OnMessageDelta += OnMessageDelta;
            bridge.OnSystemMessage += OnSystemMessage;
            bridge.OnStreamingBegin += OnStreamingBegin;

            if (!bridge.Ready)
            {
                AppendSystemNotice(string.IsNullOrEmpty(bridge.ConfigError)
                    ? "未找到 LlmConfig。请到 Assets/Resources/ 下创建 ACLS/LLM Config 资产并填 ApiKey。"
                    : bridge.ConfigError);
                SetInteractable(false);
            }

            RefreshChoices(bridge.CurrentChoices);
            UpdateStatusLabel();
            UpdateThinking(bridge.CurrentThinking);
        }

        private void OnDestroy()
        {
            if (loadingDotsCoroutine != null) { StopCoroutine(loadingDotsCoroutine); loadingDotsCoroutine = null; }
            if (bridge != null)
            {
                bridge.OnMessage -= AppendMessage;
                bridge.OnBusyChanged -= OnBusyChanged;
                bridge.OnChoicesChanged -= RefreshChoices;
                bridge.OnThinkingChanged -= UpdateThinking;
                bridge.OnMessageDelta -= OnMessageDelta;
                bridge.OnSystemMessage -= OnSystemMessage;
                bridge.OnStreamingBegin -= OnStreamingBegin;
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

            var right = new GameObject("Thinking", typeof(RectTransform));
            right.transform.SetParent(transform, false);
            var rightRt = (RectTransform)right.transform;
            rightRt.anchorMin = new Vector2(0.6f, 0);
            rightRt.anchorMax = new Vector2(1, 1);
            rightRt.offsetMin = Vector2.zero;
            rightRt.offsetMax = Vector2.zero;

            BuildHistory(left.transform);
            BuildStatusBar(left.transform);
            BuildChoicesRow(left.transform);
            BuildInputRow(left.transform);
            BuildThinking(right.transform);
        }

        private void BuildHistory(Transform parent)
        {
            var scrollGo = new GameObject("History",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight + ChoicesRowHeight + 8);
            scrollRt.offsetMax = new Vector2(-8, -8);
            scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.9f);

            scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Mask));
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
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
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
            var rowGo = new GameObject("Choices",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(parent, false);
            choicesContainer = (RectTransform)rowGo.transform;
            choicesContainer.anchorMin = new Vector2(0, 0);
            choicesContainer.anchorMax = new Vector2(1, 0);
            choicesContainer.pivot = new Vector2(0.5f, 0);
            choicesContainer.offsetMin = new Vector2(8, InputRowHeight + StatusBarHeight);
            choicesContainer.offsetMax = new Vector2(-8, InputRowHeight + StatusBarHeight + ChoicesRowHeight);

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.padding = new RectOffset(0, 0, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
        }

        private void RefreshChoices(IReadOnlyList<LlmReply.Choice> choices)
        {
            // ── 打字机播放中：缓冲，不立即显示 ──
            if (isTypewriting)
            {
                pendingChoices = choices;
                return;
            }

            ApplyChoices(choices);
        }

        private void ApplyChoices(IReadOnlyList<LlmReply.Choice> choices)
        {
            for (int i = choicesContainer.childCount - 1; i >= 0; i--)
                Destroy(choicesContainer.GetChild(i).gameObject);
            choiceButtons.Clear();

            if (choices == null || choices.Count == 0) return;

            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var label = choices[i].Label;
                var btn = UiKit.CreateButton(choicesContainer, "Choice", label, () => OnChoiceClicked(index));
                choiceButtons.Add(btn);
            }

            bool active = bridge.Ready && !bridge.Busy;
            foreach (var b in choiceButtons) b.interactable = active;
        }

        private void OnChoiceClicked(int index)
        {
            if (bridge == null || !bridge.Ready || bridge.Busy) return;
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

            input = MakeTmpInput(rowGo.transform, "Input", "输入你的行动……", charLimit: 240);
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
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ScrollRect));
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
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Mask));
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
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
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
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(ContentSizeFitter));
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

        private void AppendMessage(ChatMessage m)
        {
            // ── 旁白消息结束：启动打字机动画 ──
            if (streamingMsg != null && m.Role == ChatRole.Assistant)
            {
                // 独立 meta 行：token 用量，不走打字机、响应到达立即显示
                if (bridge != null) AppendMetaItem(bridge.LastUsage, bridge.CumulativeUsage);

                // 关键：streamingMsg.text 已经是流式阶段累积的最终文本（由 OnMessageDelta 逐次写入），
                // 如果再用 m.Content 重新构造 typewriterTarget，可能在末尾与 streamingMsg 不一致，
                // 导致打字机“停止在最后几个字”。这里以 streamingMsg.text 为准。
                Log.Info(Log.Channels.UI, "[Typewriter] 流已结束、准备收尾: gen={0} msgLen={1} contentLen={2}",
                    streamingGen, streamingMsg?.text.Length ?? 0, m.Content?.Length ?? 0);

                // 某些回合会传过来 OnMessage（例如 pipeline 步骈的 narration）但根本没有流式填充，
                // 此时 streamingMsg.text 还是 “思考中” 占位符 → 换成正式内容。
                if (streamingMsg != null && !string.IsNullOrEmpty(m.Content)
                    && (streamingMsg.text.Contains("思考中") || streamingMsg.text.Length < m.Content.Length / 2))
                {
                    string ts = streamingTimestamp;
                    string label = "<color=#f5d57a><b>旁白</b></color>";
                    string full = $"{label} {ts}\n{Escape(m.Content)}";
                    streamingMsg.text = full;
                    typewriterTarget = full;
                }
                else
                {
                    typewriterTarget = streamingMsg != null ? streamingMsg.text : "";
                }

                // 启动收尾（主要起 Reset 状态、刷新 pending choices、推进 streamingGen 的作用）
                if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
                typewriterCoroutine = StartCoroutine(TypewriterRoutine());
                return;
            }

            string prefix = m.Role switch
            {
                ChatRole.User => "<color=#7fb6ff><b>你</b></color>",
                ChatRole.Assistant => "<color=#f5d57a><b>旁白</b></color>",
                ChatRole.System => "<color=#ff8888><b>系统</b></color>",
                _ => "",
            };
            var lineText = $"{prefix} {Timestamp()}\n{Escape(m.Content)}";

            CreateMessageItem(lineText);

            // 独立 meta 行：assistant 消息附加 token 用量
            if (m.Role == ChatRole.Assistant && bridge != null)
                AppendMetaItem(bridge.LastUsage, bridge.CumulativeUsage);

            ScrollToBottom();
        }

        private void AppendMetaItem(LlmUsage last, LlmUsage cumulative)
        {
            if (!last.HasData) return;
            string rt = bridge != null && bridge.LastResponseTime > 0f
                ? $" · {bridge.LastResponseTime:F1}s"
                : "";
            string metaText = $"<size=14><color=#555555><align=right>in {last.InputTokens} · out {last.OutputTokens} · ∑{cumulative.Total}{rt}</align></color></size>";
            CreateMessageItem(metaText);
        }

        private void AppendSystemNotice(string text)
        {
            CreateMessageItem($"<color=#ff8888><b>系统</b></color> {Timestamp()}\n<color=#888888>· {Escape(text)}</color>");
            ScrollToBottom();
        }

        private TextMeshProUGUI CreateMessageItem(string richText)
        {
            var go = new GameObject("Msg",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(ContentSizeFitter));
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

        private void OnBusyChanged(bool busy)
        {
            if (!busy)
            {
                // 停止等待动画（出错/中断时清理）
                if (loadingDotsCoroutine != null) { StopCoroutine(loadingDotsCoroutine); loadingDotsCoroutine = null; }
            }
            SetInteractable(!busy && bridge.Ready);
            sendBtn.gameObject.SetActive(!busy);
            cancelBtn.gameObject.SetActive(busy);
            UpdateStatusLabel();
            if (!busy) input.ActivateInputField();
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
            thinkingLabel.text = Escape(text);
            ScrollThinkingToBottom();
        }

        private static string Timestamp() =>
            $"<color=#cccccc><size=12>{System.DateTime.Now:HH:mm:ss}</size></color>";

        private void OnStreamingBegin()
        {
            // 停止上次的等待动画
            if (loadingDotsCoroutine != null) { StopCoroutine(loadingDotsCoroutine); loadingDotsCoroutine = null; }

            // 有正在播放的打字机→先写完当前消息，再开始新的
            if (typewriterCoroutine != null)
            {
                StopCoroutine(typewriterCoroutine);
                typewriterCoroutine = null;
                isTypewriting = false;
                Log.Info(Log.Channels.UI, "[Typewriter] 被新流中断，强制收尾: gen={0} targetLen={1}",
                    streamingGen, typewriterTarget?.Length ?? 0);
                if (!string.IsNullOrEmpty(typewriterTarget))
                {
                    FinishTypewriting(typewriterTarget);
                }
                else
                {
                    // 打字机未真正启动，直接清掉流式槽
                    streamingMsg = null;
                    streamingGen++;
                }
            }
            pendingChoices = null;

            streamingTimestamp = Timestamp();
            streamingGen++;
            // 创建加载占位消息，流式文本到达后自动替换
            streamingMsg = CreateMessageItem($"<color=#f5d57a><b>旁白</b></color> {streamingTimestamp}\n<color=#7c7c8a>思考中</color>");
            loadingDotsCoroutine = StartCoroutine(LoadingDotsRoutine());
        }

        /// <summary>思考中·→··→··· 循环动画</summary>
        private System.Collections.IEnumerator LoadingDotsRoutine()
        {
            int dotCount = 0;
            while (true)
            {
                dotCount = (dotCount % 4) + 1;
                string dots = new string('·', dotCount);
                if (streamingMsg != null)
                    streamingMsg.text = $"<color=#f5d57a><b>旁白</b></color> {streamingTimestamp}\n<color=#7c7c8a>思考中{dots}</color>";
                yield return new WaitForSeconds(0.5f);
            }
        }

        private void OnMessageDelta(string partialNarration)
        {
            // 流式文本到达，停止等待动画
            if (loadingDotsCoroutine != null)
            {
                StopCoroutine(loadingDotsCoroutine);
                loadingDotsCoroutine = null;
            }

            int capturedGen = streamingGen;
            if (capturedGen != streamingGen) return;

            if (streamingMsg == null)
                streamingMsg = CreateMessageItem("");

            if (capturedGen != streamingGen)
            {
                streamingMsg = null;
                return;
            }

            string label = "<color=#f5d57a><b>旁白</b></color>";
            string full = $"{label} {streamingTimestamp}\n{Escape(partialNarration)}";

            // 打字机动画中：只缓存目标文本，不覆盖当前显示
            if (isTypewriting)
            {
                typewriterTarget = full;
            }
            else
            {
                streamingMsg.text = full;
                typewriterTarget = full;
            }
            ScrollToBottom();
        }

        /// <summary>打字机协程：逐字显示剩余字符。
        /// 关键：循环内部始终从字段读取最新 typewriterTarget，保证中途到达的新 delta 也能补齐。</summary>
        private System.Collections.IEnumerator TypewriterRoutine()
        {
            isTypewriting = true;

            int startLen = streamingMsg != null ? streamingMsg.text.Length : 0;
            int targetLen = (typewriterTarget ?? "").Length;
            Log.Info(Log.Channels.UI, "[Typewriter] 启动: gen={0} startLen={1} targetLen={2}",
                streamingGen, startLen, targetLen);

            // 协程期间 typewriterTarget 可能会被 OnMessageDelta 延长；
            // 循环条件用当前最新字段，确保能"追"上最新目标。
            while (true)
            {
                string latestTarget = typewriterTarget ?? "";
                int latestLen = latestTarget.Length;
                int currentLen = streamingMsg != null ? streamingMsg.text.Length : 0;

                if (currentLen >= latestLen)
                {
                    // 已显示到最新目标→结束
                    FinishTypewriting(latestTarget);
                    yield break;
                }

                // 写下一个字符
                int nextLen = currentLen + 1;
                if (streamingMsg != null)
                    streamingMsg.text = latestTarget.Substring(0, nextLen);

                yield return new WaitForSeconds(TypewriterCharInterval);
            }
        }

        /// <summary>打字机完成：设置完整文本，清空流式槽，刷新缓冲的 choices</summary>
        private void FinishTypewriting(string finalText)
        {
            int finalLen = finalText?.Length ?? 0;
            Log.Info(Log.Channels.UI, "[Typewriter] 完成: gen={0} finalLen={1} streamingMsgNull={2}",
                streamingGen, finalLen, streamingMsg == null);

            if (streamingMsg != null)
                streamingMsg.text = finalText;

            isTypewriting = false;
            typewriterCoroutine = null;

            // 完成：streamingMsg 转为普通消息，不再接受后续 delta
            streamingMsg = null;
            streamingGen++;

            // 刷新缓冲的 choices
            if (pendingChoices != null)
            {
                var toShow = pendingChoices;
                pendingChoices = null;
                ApplyChoices(toShow);
            }
            ScrollToBottom();
        }

        private void OnSystemMessage(string message)
        {
            CreateMessageItem($"<color=#5bc0be><b>系统</b></color>\n<color=#b0b0c0>{Escape(message)}</color>");
            ScrollToBottom();
        }

        private void ScrollThinkingToBottom()
        {
            if (thinkingScroll == null) return;
            StartCoroutine(ScrollThinkingNextFrame());
        }

        private System.Collections.IEnumerator ScrollThinkingNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            thinkingScroll.verticalNormalizedPosition = 0f;
        }

        private void ScrollToBottom()
        {
            StartCoroutine(ScrollNextFrame());
        }

        private System.Collections.IEnumerator ScrollNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        private static string Escape(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("<", "＜").Replace(">", "＞");
        }

        // Builds a TMP_InputField with TextArea + Placeholder + Text children.
        internal static TMP_InputField MakeTmpInput(Transform parent, string name, string placeholder, int charLimit)
        {
            var go = new GameObject(name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.10f, 1f);

            var field = go.GetComponent<TMP_InputField>();
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = charLimit;

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
            ph.alignment = TextAlignmentOptions.Left;
            ph.enableWordWrapping = false;
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
            t.alignment = TextAlignmentOptions.Left;
            t.enableWordWrapping = false;
            t.richText = false;
            field.textComponent = t;

            return field;
        }
    }
}
