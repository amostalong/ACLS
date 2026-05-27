using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ACLS.Authoring;

namespace ACLS.UI
{
    public static class UiKit
    {
        public static TMP_FontAsset TmpFont;

        public static GameObject CreatePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = bg;
            return go;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name,
            int fontSize = 16, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 4);
            rt.offsetMax = new Vector2(-8, -4);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = TmpFont;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            return t;
        }

        public static Button CreateButton(Transform parent, string name, string text, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.28f, 0.95f);
            var btn = go.GetComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = CreateText(go.transform, "Label", 16, TextAlignmentOptions.Center);
            label.text = text;
            return btn;
        }

        public static TMP_FontAsset ResolveFont(int sizeHint = 16)
        {
            Font rawFont = ContentLoader.LoadSync<Font>("Assets/Content/Fonts/LXGWWenKai-Regular.ttf", "Fonts/LXGWWenKai-Regular");
            if (rawFont == null)
            {
                string[] candidates = {
                    "Microsoft YaHei", "Microsoft YaHei UI",
                    "SimHei", "SimSun", "NSimSun",
                    "PingFang SC", "Heiti SC", "Hiragino Sans GB",
                    "Noto Sans CJK SC", "Noto Sans SC",
                    "WenQuanYi Micro Hei", "Arial Unicode MS", "Arial",
                };
                rawFont = Font.CreateDynamicFontFromOSFont(candidates, sizeHint);
            }
            var mainAsset = TMP_FontAsset.CreateFontAsset(rawFont);

            // Fallback font: symbol/emoji fonts to cover Dingbats (U+2700-27BF) and
            // other Unicode blocks that CJK fonts don't include (geometric shapes,
            // arrows, currency symbols, emoji, etc.).
            var fallback = TryBuildFallbackFont();
            if (fallback != null)
            {
                mainAsset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            }

            return mainAsset;
        }

        private static TMP_FontAsset TryBuildFallbackFont(int sizeHint = 20)
        {
            string[] candidates =
            {
                "Segoe UI Symbol",      // Windows — broad Unicode coverage incl. Dingbats
                "Segoe UI Emoji",       // Windows — emoji + symbols
                "Apple Color Emoji",    // macOS/iOS — emoji + some symbols
                "Apple Symbols",        // macOS — symbol fallback
                "Noto Sans Symbols",    // Linux / cross-platform
                "Arial Unicode MS",     // macOS / Office — very broad coverage
            };
            Font raw = Font.CreateDynamicFontFromOSFont(candidates, sizeHint);
            if (raw == null) return null;
            return TMP_FontAsset.CreateFontAsset(raw);
        }
    }
}
