using System.Collections;
using TMPro;
using UnityEngine;
using ACLS.Logging;

namespace ACLS.UI
{
    /// <summary>
    /// Typewriter animation controller. Bound to an external TextMeshProUGUI
    /// and a ChatBlock. The block is the single source of truth for the
    /// text-to-show; the coroutine polls it each frame.
    ///
    /// Polling model:
    ///   - For Streaming blocks: reads ChatBlock.CurrentStreamText each tick.
    ///     Shown count catches up if the cumulative text jumps ahead.
    ///   - For Static blocks: shows StaticText immediately, no animation.
    ///
    /// Completion:
    ///   - Static: text shown in one frame.
    ///   - Streaming: when shown == target length AND block.StreamFlushed == true.
    ///
    /// Not externally interruptible — the coroutine owns its own lifecycle.
    /// </summary>
    public sealed class TypewriterSlot : MonoBehaviour
    {
        private const float CharInterval = 0.025f;
        private const float DotsInterval = 0.5f;

        private TextMeshProUGUI _display;
        private string _header;
        private ChatBlock _block;

        private int _shown;
        private bool _done;

        private Coroutine _routine;

        public bool IsDone => _done;

        public event System.Action<TypewriterSlot> OnDone;

        public void Assign(TextMeshProUGUI tmp, string header, ChatBlock block)
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }

            gameObject.SetActive(true);

            _display = tmp;
            _header = header ?? "";
            _block = block;
            _shown = 0;
            _done = false;

            _routine = StartCoroutine(AnimateRoutine());
        }

        private void OnDestroy()
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }
            _display = null;
            _block = null;
        }

        private IEnumerator AnimateRoutine()
        {
            if (_display == null || _block == null) yield break;

            // Phase 1: streaming 且还没收到第一段数据 — 显示"思考中"省略号
            if (_block.IsStreaming)
            {
                int dots = 0;
                while (string.IsNullOrEmpty(_block.CurrentStreamText) && !_block.StreamFlushed)
                {
                    dots = (dots % 4) + 1;
                    _display.text = _header + "\n<color=#7c7c8a>思考中" + new string('\u00b7', dots) + "</color>";
                    yield return new WaitForSeconds(DotsInterval);
                }
            }

            // Phase 2: 逐字显示。target 来自 block,逐字追上(不跳变)
            string lastSeen = null;
            while (true)
            {
                string target = _block.IsStreaming ? _block.CurrentStreamText : _block.StaticText;
                if (target == null) target = "";

                // 当 target 被替换为更长文本时,shown 最多追到旧 target 长度
                // 这样后续 delta 仍走逐字动画
                if (target != lastSeen)
                {
                    int oldLen = lastSeen?.Length ?? 0;
                    if (target.Length < _shown) _shown = target.Length;  // 文本变短了,截断
                    if (oldLen > _shown) _shown = oldLen;                // 不要超越已显示
                    lastSeen = target;
                }

                if (_shown < target.Length)
                {
                    _shown++;
                    _display.text = _header + "\n" + target.Substring(0, _shown);
                    yield return new WaitForSeconds(CharInterval);
                    continue;
                }

                // 所有字符已显示。完成条件: 非 streaming(Static) 或 streaming 且已 flush。
                if (!_block.IsStreaming || _block.StreamFlushed)
                {
                    _display.text = _header + "\n" + target;
                    _done = true;
                    Log.Info(Log.Channels.UI, "[Typewriter] 打字完成: shown={0}", _shown);
                    var d = OnDone;
                    OnDone = null;
                    d?.Invoke(this);
                    yield break;
                }

                // 已显示到当前 target 长度但未 flush: 等待下一次数据
                yield return null;
            }
        }
    }
}
