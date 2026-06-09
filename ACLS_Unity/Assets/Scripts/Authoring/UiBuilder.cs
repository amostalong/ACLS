using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ACLS.Sim;
using ACLS.UI;

namespace ACLS.Authoring
{
    // Layout reference: 1920x1080. Pixel-based anchors keep widths/heights
    // stable across resolutions (CanvasScaler set to height-priority).
    public static class UiBuilder
    {
        public const float HUD_HEIGHT = 64f;

        public static void Build(World world, GameClockDriver clock, ChatBridge chat, GameStateMachine stateMachine)
        {
            UiKit.TmpFont = UiKit.ResolveFont(20);

            EnsureEventSystem();

            var canvasGo = new GameObject("[ACLS Canvas]",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(canvasGo);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;  // height-priority: HUD always 64px tall regardless of aspect

            // HUD — top bar, fixed 64px height, full width.
            var hudGo = NewChild(canvas.transform, "HUD");
            var hudRt = (RectTransform)hudGo.transform;
            hudRt.anchorMin = new Vector2(0, 1);
            hudRt.anchorMax = new Vector2(1, 1);
            hudRt.pivot = new Vector2(0.5f, 1);
            hudRt.anchoredPosition = Vector2.zero;
            hudRt.sizeDelta = new Vector2(0, HUD_HEIGHT);
            hudGo.AddComponent<HudView>().Bind(world, clock, chat);

            // ChatPanel — fills entire area below HUD (full width, no avatar bar).
            if (chat != null)
            {
                var chatGo = NewChild(canvas.transform, "ChatPanel");
                var chatRt = (RectTransform)chatGo.transform;
                chatRt.anchorMin = Vector2.zero;
                chatRt.anchorMax = new Vector2(1, 1);
                chatRt.offsetMin = new Vector2(0, 0);
                chatRt.offsetMax = new Vector2(0, -HUD_HEIGHT);
                chatGo.AddComponent<ChatPanelView>().Bind(chat);
            }

            // EventModal — full-canvas overlay, hidden until needed.
            var modalGo = NewChild(canvas.transform, "EventModal");
            var modalRt = (RectTransform)modalGo.transform;
            modalRt.anchorMin = Vector2.zero;
            modalRt.anchorMax = Vector2.one;
            modalRt.offsetMin = Vector2.zero;
            modalRt.offsetMax = Vector2.zero;
            modalGo.AddComponent<EventModalView>().Bind(world);

            // ── NewGameView ── 合并的世界选择+角色创建，无存档时显示。
            var ngGo = NewChild(canvas.transform, "NewGame");
            var ngRt = (RectTransform)ngGo.transform;
            ngRt.anchorMin = Vector2.zero;
            ngRt.anchorMax = Vector2.one;
            ngRt.offsetMin = Vector2.zero;
            ngRt.offsetMax = Vector2.zero;
            var ngView = ngGo.AddComponent<NewGameView>();

            // ── CharacterCustomView ── 自定义角色/世界输入，独立于 NewGame 的全屏卡。
            var ccGo = NewChild(canvas.transform, "CharacterCustom");
            var ccRt = (RectTransform)ccGo.transform;
            ccRt.anchorMin = Vector2.zero;
            ccRt.anchorMax = Vector2.one;
            ccRt.offsetMin = Vector2.zero;
            ccRt.offsetMax = Vector2.zero;
            var ccView = ccGo.AddComponent<CharacterCustomView>();

            ngView.Bind(world, chat, stateMachine, ccView);
            ccView.Bind(world, chat, stateMachine, ngView);

            // 无存档时显示 NewGameView（有存档时在 BootAsync 中已恢复世界，Player 非空）
            if (world.Player == null)
                ngView.SetVisible(true);

            // 旧视图保留供参考，但不参与流程（可安全删除）。
            // CharacterCreationView (legacy)
            var creationGo = NewChild(canvas.transform, "CharacterCreation");
            var creationRt = (RectTransform)creationGo.transform;
            creationRt.anchorMin = Vector2.zero;
            creationRt.anchorMax = Vector2.one;
            creationRt.offsetMin = Vector2.zero;
            creationRt.offsetMax = Vector2.zero;
            var creationView = creationGo.AddComponent<CharacterCreationView>();
            creationView.Bind(world, chat, stateMachine);
            creationView.SetVisible(false);

            // WorldSelectionView (legacy)
            var worldSelGo = NewChild(canvas.transform, "WorldSelection");
            var worldSelRt = (RectTransform)worldSelGo.transform;
            worldSelRt.anchorMin = Vector2.zero;
            worldSelRt.anchorMax = Vector2.one;
            worldSelRt.offsetMin = Vector2.zero;
            worldSelRt.offsetMax = Vector2.zero;
            var worldSelView = worldSelGo.AddComponent<WorldSelectionView>();
            worldSelView.Bind(world, chat, stateMachine, onWorldBuilt: () => creationView.SetVisible(true));
            worldSelView.SetVisible(false);

            // DebugPanel — top-most overlay, hidden until toggled via HUD button.
            var debugGo = NewChild(canvas.transform, "DebugPanel");
            var debugRt = (RectTransform)debugGo.transform;
            debugRt.anchorMin = Vector2.zero;
            debugRt.anchorMax = Vector2.one;
            debugRt.offsetMin = Vector2.zero;
            debugRt.offsetMax = Vector2.zero;
            var debugView = debugGo.AddComponent<DebugPanelView>();
            debugView.Build();
            hudGo.GetComponent<HudView>().SetDebugPanel(debugView);
        }

        private static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("[EventSystem]");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            Object.DontDestroyOnLoad(go);
        }
    }
}
