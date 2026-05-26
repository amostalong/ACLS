using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;
using ACLS.Llm;
using ACLS.Loc;
using ACLS.Sim;

namespace ACLS.UI
{
    public sealed class HudView : MonoBehaviour
    {
        private World world;
        private GameClockDriver clock;
        private ChatBridge chat;
        private DebugPanelView debugPanel;
        private TextMeshProUGUI dateLabel;
        private TextMeshProUGUI statusLabel;

        public void Bind(World world, GameClockDriver clock, ChatBridge chat)
        {
            this.world = world;
            this.clock = clock;
            this.chat = chat;

            var bg = UiKit.CreatePanel(transform, "Bg",
                Vector2.zero, Vector2.one, new Color(0.08f, 0.08f, 0.12f, 0.95f));
            bg.transform.SetAsFirstSibling();

            dateLabel = UiKit.CreateText(transform, "Date", 26, TextAlignmentOptions.Left);
            var dateRt = (RectTransform)dateLabel.transform;
            dateRt.anchorMin = new Vector2(0, 0);
            dateRt.anchorMax = new Vector2(0, 1);
            dateRt.pivot = new Vector2(0, 0.5f);
            dateRt.offsetMin = new Vector2(24, 0);
            dateRt.offsetMax = new Vector2(304, 0);

            var btnRoot = new GameObject("Buttons",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            btnRoot.transform.SetParent(transform, false);
            var brRt = (RectTransform)btnRoot.transform;
            brRt.anchorMin = new Vector2(0.5f, 0);
            brRt.anchorMax = new Vector2(0.5f, 1);
            brRt.pivot = new Vector2(0.5f, 0.5f);
            brRt.sizeDelta = new Vector2(500, -16);
            brRt.anchoredPosition = Vector2.zero;
            var hlg = btnRoot.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            AddBtn(brRt, "暂停 (空格)", 140, () => clock.TogglePause());
            AddBtn(brRt, "1 慢",        80, () => clock.SetSpeed(1));
            AddBtn(brRt, "2 中",        80, () => clock.SetSpeed(2));
            AddBtn(brRt, "3 快",        80, () => clock.SetSpeed(3));
            AddBtn(brRt, "调试",        64, () => debugPanel?.Toggle());

            statusLabel = UiKit.CreateText(transform, "Status", 18, TextAlignmentOptions.Right);
            var stRt = (RectTransform)statusLabel.transform;
            stRt.anchorMin = new Vector2(1, 0);
            stRt.anchorMax = new Vector2(1, 1);
            stRt.pivot = new Vector2(1, 0.5f);
            stRt.offsetMin = new Vector2(-560, 0);
            stRt.offsetMax = new Vector2(-24, 0);

            if (chat != null) chat.OnUsageReported += OnUsageReported;

            Refresh();
        }

        public void SetDebugPanel(DebugPanelView panel)
        {
            debugPanel = panel;
        }

        private void OnDestroy()
        {
            if (chat != null) chat.OnUsageReported -= OnUsageReported;
        }

        private void OnUsageReported(LlmUsage last, LlmUsage total) => Refresh();

        private void AddBtn(Transform parent, string label, int width, System.Action cb)
        {
            var btn = UiKit.CreateButton(parent, label, label, cb);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
        }

        private void Update()
        {
            if (world == null) return;
            Refresh();
        }

        private static readonly char[] SpinnerFrames = { '|', '/', '-', '\\' };
        private const float SpinnerFps = 8f;

        private static char CurrentSpinner()
        {
            int idx = (int)(Time.realtimeSinceStartup * SpinnerFps) % SpinnerFrames.Length;
            if (idx < 0) idx = 0;
            return SpinnerFrames[idx];
        }

        private void Refresh()
        {
            if (dateLabel != null) dateLabel.text = world.Date.ToString();
            if (statusLabel == null) return;

            string head;
            if (chat != null && chat.Busy)
            {
                head = $"<color=#f5d57a><b>[{CurrentSpinner()}]</b> 与旁白通信中…</color>";
            }
            else
            {
                string speedText = clock.Speed switch
                {
                    1 => L10n.T("hud.speed.slow"),
                    3 => L10n.T("hud.speed.fast"),
                    _ => L10n.T("hud.speed.normal"),
                };
                head = world.Paused
                    ? $"<color=#f4cc6a>{L10n.T("hud.pause")}</color>  ·  {speedText}"
                    : $"{L10n.T("hud.running")}  ·  {speedText}";
            }

            string tokens = "";
            if (chat != null && chat.CallCount > 0)
            {
                int lastIn = chat.LastUsage.InputTokens;
                int lastOut = chat.LastUsage.OutputTokens;
                int totalAll = chat.CumulativeUsage.Total;
                tokens = $"  ·  <color=#9bb3d4>↑{Format(lastIn)} ↓{Format(lastOut)} · ∑{Format(totalAll)} · {chat.CallCount} 次</color>";
            }

            statusLabel.text = head + tokens;
        }

        private static string Format(int n)
        {
            if (n < 1000) return n.ToString();
            if (n < 10000) return (n / 1000f).ToString("0.0") + "k";
            return (n / 1000) + "k";
        }
    }
}
