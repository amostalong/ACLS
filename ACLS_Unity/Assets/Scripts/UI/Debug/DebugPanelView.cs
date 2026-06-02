using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.UI
{
    // Full-screen debug overlay. Toggle via HUD button or SetVisible().
    // Tab: LLM — raw REQUEST/RESPONSE JSON.
    // Tab: 世界 — World Stage L4~L1 + Player info.
    public sealed class DebugPanelView : MonoBehaviour
    {
        private enum Tab { Llm, World }

        private GameObject root;
        private Tab activeTab = Tab.Llm;
        private GameObject llmRoot;
        private GameObject worldRoot;
        private GameObject tabBar;
        private TextMeshProUGUI headerLabel;
        private TextMeshProUGUI requestText;
        private TextMeshProUGUI responseText;
        private ScrollRect requestScroll;
        private ScrollRect responseScroll;
        private TextMeshProUGUI worldText;
        private ScrollRect worldScroll;
        private int currentIndex = -1;

        public void Build()
        {
            BuildUi();
            root.SetActive(false);
            LlmDebugLog.OnEntry += OnNewEntrySafe;
        }

        private void OnDestroy()
        {
            LlmDebugLog.OnEntry -= OnNewEntrySafe;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
                Toggle();
        }

        private void OnNewEntry(LlmDebugLog.Entry _)
        {
            currentIndex = LlmDebugLog.Entries.Count - 1;
            if (root.activeSelf && activeTab == Tab.Llm) RefreshLlm();
        }

        private void OnNewEntrySafe(LlmDebugLog.Entry _)
        {
            currentIndex = LlmDebugLog.Entries.Count - 1;
            UniTask.Post(() => {
                if (root.activeSelf && activeTab == Tab.Llm) RefreshLlm();
            });
        }

        public void Toggle()
        {
            bool show = !root.activeSelf;
            root.SetActive(show);
            if (show) RefreshAll();
        }

        private void SetTab(Tab tab)
        {
            activeTab = tab;
            if (llmRoot != null) llmRoot.SetActive(tab == Tab.Llm);
            if (worldRoot != null) worldRoot.SetActive(tab == Tab.World);
            RefreshAll();
        }

        // -------- navigation --------

        private void Prev()
        {
            if (LlmDebugLog.Entries.Count == 0) return;
            currentIndex = Mathf.Max(0, currentIndex - 1);
            RefreshLlm();
        }

        private void Next()
        {
            if (LlmDebugLog.Entries.Count == 0) return;
            currentIndex = Mathf.Min(LlmDebugLog.Entries.Count - 1, currentIndex + 1);
            RefreshLlm();
        }

        private void RefreshAll()
        {
            if (activeTab == Tab.Llm) RefreshLlm();
            else RefreshWorld();
        }

        private void RefreshLlm()
        {
            var entries = LlmDebugLog.Entries;
            if (entries.Count == 0 || currentIndex < 0)
            {
                headerLabel.text = "尚无记录 — 等待第一次 LLM 调用";
                if (requestText  != null) requestText.text  = "";
                if (responseText != null) responseText.text = "";
                return;
            }

            if (currentIndex >= entries.Count) currentIndex = entries.Count - 1;
            var e = entries[currentIndex];

            headerLabel.text =
                $"[{currentIndex + 1} / {entries.Count}]   {e.Provider}   {e.Timestamp}";

            if (requestText  != null) requestText.text  = e.PrettyRequest;
            if (responseText != null) responseText.text = e.PrettyResponse;

            ScrollToTop(requestScroll);
            ScrollToTop(responseScroll);
        }

        private void RefreshWorld()
        {
            if (worldText == null) return;

            var clock = Object.FindObjectOfType<GameClockDriver>();
            var w = clock?.World;
            if (w == null)
            {
                worldText.text = "<color=#ff6666>World 未初始化</color>";
                return;
            }

            var s = w.Stage;
            var p = w.Player;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<color=#77bbff><size=18>=== 世界数据 ===</size></color>\n");

            sb.AppendLine($"<color=#ffcc66>日期:</color> {w.Date}");
            sb.AppendLine($"<color=#ffcc66>暂停:</color> {w.Paused}");
            sb.AppendLine($"<color=#ffcc66>金钱:</color> {w.Gold}");
            sb.AppendLine($"<color=#ffcc66>Flags:</color> [{string.Join(", ", w.Flags)}]");
            sb.AppendLine();

            if (p != null)
            {
                sb.AppendLine($"<color=#77bbff><size=16>=== 玩家 ===</size></color>");
                sb.AppendLine($"姓名: {p.Name}（{p.Courtesy}）");
                sb.AppendLine($"性别: {(p.Sex == Sex.Male ? "男" : "女")}  年龄: {p.AgeAt(w.Date)}");
                sb.AppendLine($"特质: {TraitLabel(p.Traits)}");
                sb.AppendLine($"当前目标: {p.CurrentGoal}");
                sb.AppendLine($"秘密: {p.Secret}");
                sb.AppendLine($"家族背景: {p.BackgroundStory}");
                sb.AppendLine();
            }

            sb.AppendLine($"<color=#77bbff><size=16>=== Stage 数据 ===</size></color>");
            sb.AppendLine($"WorldDescription: {s.WorldDescription}");
            sb.AppendLine($"IsWorldBuilt={s.IsWorldBuilt}  IsStageBuilt={s.IsStageBuilt}");
            sb.AppendLine();

            sb.AppendLine($"<color=#77bbff>--- L4World (宏观) ---</color>");
            sb.AppendLine(s.L4World);
            sb.AppendLine();
            sb.AppendLine($"<color#77bbff>--- L3Expanse (区域) ---</color>");
            sb.AppendLine(s.L3Expanse);
            sb.AppendLine();
            sb.AppendLine($"<color#77bbff>--- L2Arena (近域) ---</color>");
            sb.AppendLine(s.L2Arena);
            sb.AppendLine();
            sb.AppendLine($"<color#77bbff>--- L1Stage (贴身) ---</color>");
            sb.AppendLine(s.L1Stage);
            sb.AppendLine();

            sb.AppendLine($"<color=#77bbff><size=16>=== LLM 调用统计 ===</size></color>");
            var chat = Object.FindObjectOfType<ChatBridge>();
            if (chat != null)
            {
                sb.AppendLine($"调用次数: {chat.CallCount}");
                sb.AppendLine($"累计 Token: {chat.CumulativeUsage.Total}");
                sb.AppendLine($"最后输入: {chat.LastUsage.InputTokens}  最后输出: {chat.LastUsage.OutputTokens}");
            }
            else sb.AppendLine("ChatBridge 未找到");

            worldText.text = sb.ToString();
            ScrollToTop(worldScroll);
        }

        private static string TraitLabel(List<int> traits)
        {
            if (traits == null || traits.Count == 0) return "无";
            var labels = new List<string>();
            foreach (var id in traits)
            {
                labels.Add(id switch
                {
                    WorldFactory.TRAIT_CAUTIOUS => "谨慎",
                    WorldFactory.TRAIT_DECISIVE => "果决",
                    WorldFactory.TRAIT_STUDIOUS => "好学",
                    _ => $"未知({id})",
                });
            }
            return string.Join(", ", labels);
        }

        private static void ScrollToTop(ScrollRect sr)
        {
            if (sr != null) sr.verticalNormalizedPosition = 1f;
        }

        // -------- UI construction --------

        private void BuildUi()
        {
            root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = new Vector2(0.01f, 0.01f);
            rootRt.anchorMax = new Vector2(0.99f, 0.99f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // Card background.
            var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(root.transform, false);
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = Vector2.zero;
            cardRt.anchorMax = Vector2.one;
            cardRt.offsetMin = Vector2.zero;
            cardRt.offsetMax = Vector2.zero;
            card.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.10f, 0.98f);

            BuildTopBar();
            BuildTabs();

            // LLM content (existing two-column layout)
            llmRoot = new GameObject("LlmContent", typeof(RectTransform));
            llmRoot.transform.SetParent(root.transform, false);
            var llmRt = (RectTransform)llmRoot.transform;
            llmRt.anchorMin = Vector2.zero;
            llmRt.anchorMax = Vector2.one;
            llmRt.offsetMin = new Vector2(0, 44 + 36);
            llmRt.offsetMax = Vector2.zero;
            BuildColumns(llmRoot.transform);

            // World content (single scroll column)
            worldRoot = new GameObject("WorldContent", typeof(RectTransform));
            worldRoot.transform.SetParent(root.transform, false);
            var worldRt = (RectTransform)worldRoot.transform;
            worldRt.anchorMin = Vector2.zero;
            worldRt.anchorMax = Vector2.one;
            worldRt.offsetMin = new Vector2(0, 44 + 36);
            worldRt.offsetMax = Vector2.zero;
            worldRoot.SetActive(false);

            worldText = BuildScrollColumn(worldRoot.transform, "WorldCol", "",
                Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4),
                0, out worldScroll);
            worldText.richText = true;
            worldText.fontSize = 20;
        }

        private void BuildTabs()
        {
            tabBar = new GameObject("TabBar", typeof(RectTransform));
            tabBar.transform.SetParent(root.transform, false);
            var tabRt = (RectTransform)tabBar.transform;
            tabRt.anchorMin = new Vector2(0, 1);
            tabRt.anchorMax = new Vector2(1, 1);
            tabRt.pivot     = new Vector2(0.5f, 1);
            tabRt.sizeDelta = new Vector2(0, 36);
            tabRt.anchoredPosition = new Vector2(0, -44);

            var layout = tabBar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            AddTabBtn("📡 LLM 记录", Tab.Llm);
            AddTabBtn("🌍 世界数据", Tab.World);
        }

        private void AddTabBtn(string label, Tab tab)
        {
            var btn = UiKit.CreateButton(tabBar.transform, "Tab_" + tab, label, () => SetTab(tab));
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 130;
            le.minWidth = 80;
        }

        private void BuildTopBar()
        {
            const float barH = 44f;

            var bar = new GameObject("TopBar", typeof(RectTransform));
            bar.transform.SetParent(root.transform, false);
            var barRt = (RectTransform)bar.transform;
            barRt.anchorMin = new Vector2(0, 1);
            barRt.anchorMax = new Vector2(1, 1);
            barRt.pivot     = new Vector2(0.5f, 1);
            barRt.sizeDelta = new Vector2(0, barH);
            barRt.anchoredPosition = Vector2.zero;

            // ← Prev
            var prevBtn = UiKit.CreateButton(bar.transform, "Prev", "← 上条", Prev);
            var prevRt = (RectTransform)prevBtn.transform;
            prevRt.anchorMin = new Vector2(0, 0);
            prevRt.anchorMax = new Vector2(0, 1);
            prevRt.pivot     = new Vector2(0, 0.5f);
            prevRt.sizeDelta = new Vector2(88, -8);
            prevRt.anchoredPosition = new Vector2(8, 0);

            // Header label — center.
            headerLabel = UiKit.CreateText(bar.transform, "Header", 21, TextAlignmentOptions.Center);
            var hRt = (RectTransform)headerLabel.transform;
            hRt.anchorMin = Vector2.zero;
            hRt.anchorMax = Vector2.one;
            hRt.offsetMin = new Vector2(104, 4);
            hRt.offsetMax = new Vector2(-208, -4);
            headerLabel.text = "尚无记录";
            headerLabel.color = new Color(0.75f, 0.85f, 1f, 1f);

            // Next →
            var nextBtn = UiKit.CreateButton(bar.transform, "Next", "下条 →", Next);
            var nextRt = (RectTransform)nextBtn.transform;
            nextRt.anchorMin = new Vector2(1, 0);
            nextRt.anchorMax = new Vector2(1, 1);
            nextRt.pivot     = new Vector2(1, 0.5f);
            nextRt.sizeDelta = new Vector2(88, -8);
            nextRt.anchoredPosition = new Vector2(-108, 0);

            // ✕ Close (red-tinted)
            var closeBtn = UiKit.CreateButton(bar.transform, "Close", "× 关闭", Toggle);
            closeBtn.GetComponent<Image>().color = new Color(0.48f, 0.14f, 0.14f, 0.95f);
            var closeRt = (RectTransform)closeBtn.transform;
            closeRt.anchorMin = new Vector2(1, 0);
            closeRt.anchorMax = new Vector2(1, 1);
            closeRt.pivot     = new Vector2(1, 0.5f);
            closeRt.sizeDelta = new Vector2(96, -8);
            closeRt.anchoredPosition = new Vector2(-4, 0);
        }

        private void BuildColumns(Transform parent)
        {
            const float topBarH = 0f;
            const float labelH  = 26f;

            var cols = new GameObject("Cols", typeof(RectTransform));
            cols.transform.SetParent(parent, false);
            var colsRt = (RectTransform)cols.transform;
            colsRt.anchorMin = Vector2.zero;
            colsRt.anchorMax = Vector2.one;
            colsRt.offsetMin = new Vector2(0, 0);
            colsRt.offsetMax = new Vector2(0, -topBarH);

            requestText  = BuildScrollColumn(cols.transform, "RequestCol",  "REQUEST  (发送到 API 的 JSON)",
                new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(4, 4), new Vector2(-2, -4),
                labelH, out requestScroll);

            responseText = BuildScrollColumn(cols.transform, "ResponseCol", "RESPONSE  (API 原始返回)",
                new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(2, 4), new Vector2(-4, -4),
                labelH, out responseScroll);
        }

        private static TextMeshProUGUI BuildScrollColumn(
            Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax,
            float labelH, out ScrollRect scrollOut)
        {
            var col = new GameObject(name, typeof(RectTransform));
            col.transform.SetParent(parent, false);
            var colRt = (RectTransform)col.transform;
            colRt.anchorMin = anchorMin;
            colRt.anchorMax = anchorMax;
            colRt.offsetMin = offsetMin;
            colRt.offsetMax = offsetMax;

            // Column label header.
            var labelText = UiKit.CreateText(col.transform, "Label", 19, TextAlignmentOptions.Left);
            labelText.color = new Color(0.55f, 0.70f, 0.95f, 1f);
            labelText.text  = label;
            var labelRt = (RectTransform)labelText.transform;
            labelRt.anchorMin = new Vector2(0, 1);
            labelRt.anchorMax = new Vector2(1, 1);
            labelRt.pivot     = new Vector2(0.5f, 1);
            labelRt.sizeDelta = new Vector2(0, labelH);
            labelRt.anchoredPosition = Vector2.zero;

            // Scroll rect below the label.
            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(col.transform, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0, 0);
            scrollRt.offsetMax = new Vector2(0, -labelH);
            scrollGo.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 1f);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.movementType      = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            scrollOut = sr;

            var vpGo = new GameObject("VP",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(6, 6);
            vpRt.offsetMax = new Vector2(-6, -6);
            vpGo.GetComponent<Image>().color   = new Color(1, 1, 1, 0.01f);
            vpGo.GetComponent<Mask>().showMaskGraphic = false;
            sr.viewport = vpRt;

            // Content = TextMeshProUGUI with its own ContentSizeFitter.
            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot     = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            sr.content = contentRt;

            var t = contentGo.GetComponent<TextMeshProUGUI>();
            t.font             = UiKit.TmpFont;
            t.fontSize         = 18;
            t.color            = new Color(0.82f, 0.88f, 0.82f, 1f);
            t.alignment        = TextAlignmentOptions.TopLeft;
            t.enableWordWrapping = true;
            t.overflowMode     = TextOverflowModes.Overflow;
            t.richText         = false;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            return t;
        }
    }
}
