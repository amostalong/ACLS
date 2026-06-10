using TMPro;
using UnityEngine;
using ACLS.Logging;

namespace ACLS.UI
{
    /// <summary>
    /// 打字机文本动画控制器。不自带 TMP，通过 Assign() 绑定外部 TMP。
    ///
    /// 主动驱动模型：内部每 tick 自增 _shownCount，从 _target 读取要显示的当前累计文本。
    /// 上游 (LLM 流) 通过 Feed(fullText) 报告最新累计文本（不是 delta，是当前完整文本）。
    /// 流暂停/抖动不影响打字节奏。
    ///
    /// 完成条件：_shownCount >= _target.Length && _flushed == true。
    /// 不可打断：外部不提供 FinishNow，只有协程自己决定何时完成。
    /// </summary>
    public sealed class TypewriterSlot : MonoBehaviour
    {
        private const float CharInterval = 0.025f;
        private const float DotsInterval = 0.5f;

        private TextMeshProUGUI _display;
        private string _headerLine;

        private string _target = "";
        private int _shownCount;
        private bool _hasInput;
        private bool _flushed;

        private Coroutine _routine;
        private bool _done;

        public bool IsDone => _done;

        public event System.Action<TypewriterSlot> OnDone;

        // ── 绑定 ──

        public void Assign(TextMeshProUGUI tmp, string headerLine)
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }

            gameObject.SetActive(true);

            _display = tmp;
            _headerLine = headerLine;
            _target = "";
            _shownCount = 0;
            _hasInput = false;
            _flushed = false;
            _done = false;

            if (_display != null)
                _display.text = _headerLine + "\n<color=#7c7c8a>思考中</color>";

            _routine = StartCoroutine(AnimateRoutine());
        }

        /// <summary>上游报告的当前累计完整文本。覆盖语义。</summary>
        public void Feed(string fullText)
        {
            if (_done || _display == null) return;
            if (string.IsNullOrEmpty(fullText)) return;
            _target = fullText;
            _hasInput = true;
        }

        /// <summary>告知流结束。打字机打完 _target 中所有字符后触发 OnDone。</summary>
        public void Flush()
        {
            if (_done || _display == null) return;
            _flushed = true;
        }

        private void OnDestroy()
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }
            _display = null;
        }

        // ── 动画 ──

        private System.Collections.IEnumerator AnimateRoutine()
        {
            // Phase 1 — 等待数据
            int dots = 0;
            while (!_hasInput)
            {
                dots = (dots % 4) + 1;
                if (_display != null)
                    _display.text = _headerLine + "\n<color=#7c7c8a>思考中" + new string('\u00b7', dots) + "</color>";
                yield return new WaitForSeconds(DotsInterval);
            }

            // Phase 2 — 逐字显示
            // 主动驱动：每 tick 自增 _shownCount，不依赖上游推送节奏
            while (true)
            {
                int targetLen = _target?.Length ?? 0;

                if (_shownCount < targetLen)
                {
                    _shownCount++;
                    if (_display != null)
                        _display.text = _headerLine + "\n" + _target.Substring(0, _shownCount);
                    yield return new WaitForSeconds(CharInterval);
                    continue;
                }

                // 所有字符已显示 → 检查是否已完成
                if (_flushed)
                {
                    if (_display != null)
                        _display.text = _headerLine + "\n" + (_target ?? "");
                    _done = true;
                    Log.Info(Log.Channels.UI, "[Typewriter] 打字完成: shown={0}", _shownCount);
                    var d = OnDone;
                    OnDone = null;
                    d?.Invoke(this);
                    yield break;
                }

                // 尚未 Flush → 等待上游输送更多数据
                yield return null;
            }
        }
    }
}
