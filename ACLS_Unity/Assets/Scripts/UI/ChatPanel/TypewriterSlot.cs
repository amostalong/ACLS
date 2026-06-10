using System.Text;
using TMPro;
using UnityEngine;

namespace ACLS.UI
{
    /// <summary>
    /// 打字机文本动画控制器。不自带 TMP，通过 Assign() 绑定外部 TMP。
    ///
    /// 主动驱动模型：内部每 tick 自增 _shownCount，从 _pending 累积缓冲区取字符显示。
    /// 流数据只负责扩展 _pending（通过 Feed），流暂停/抖动不影响打字节奏。
    /// 唯一的"打完"条件是 _pending 长度 <= _shownCount 且 _flushed == true。
    /// </summary>
    public sealed class TypewriterSlot : MonoBehaviour
    {
        private const float CharInterval = 0.025f;
        private const float DotsInterval = 0.5f;
        private const float FlushGraceSeconds = 0.5f;

        private TextMeshProUGUI _display;
        private string _headerLine;

        private readonly StringBuilder _pending = new StringBuilder();
        private int _shownCount;
        private bool _hasInput;
        private bool _flushed;
        private float _flushTime;

        private Coroutine _routine;
        private bool _done;

        public bool IsDone => _done;

        /// <summary>打字完成事件。调用方应在此处理回收。</summary>
        public event System.Action<TypewriterSlot> OnDone;

        // ── 绑定 ──

        /// <summary>绑定到外部 TMP 对象。调用方负责 TMP 的生命周期。</summary>
        public void Assign(TextMeshProUGUI tmp, string headerLine)
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }

            gameObject.SetActive(true);

            _display = tmp;
            _headerLine = headerLine;
            _pending.Clear();
            _shownCount = 0;
            _hasInput = false;
            _flushed = false;
            _flushTime = 0f;
            _done = false;

            if (_display != null)
                _display.text = _headerLine + "\n<color=#7c7c8a>思考中</color>";

            _routine = StartCoroutine(AnimateRoutine());
        }

        /// <summary>流式数据注入：把新到达的片段追加到累积缓冲。</summary>
        public void Feed(string newText)
        {
            if (_done || _display == null) return;
            if (string.IsNullOrEmpty(newText)) return;
            _pending.Append(newText);
            _hasInput = true;
        }

        /// <summary>告知流结束。Flush 后若 pending 仍有字符，会继续打直到打完后触发 OnDone。</summary>
        public void Flush()
        {
            if (_done || _display == null) return;
            _flushed = true;
            _flushTime = Time.realtimeSinceStartup;
        }

        /// <summary>立刻完成——新回合打断时调用。</summary>
        public void FinishNow()
        {
            if (_done) return;
            _done = true;
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }

            if (_display != null)
            {
                _display.text = _hasInput
                    ? _headerLine + "\n" + _pending.ToString()
                    : _headerLine + "\n<color=#7c7c8a>(已中断)</color>";
            }

            var d = OnDone;
            OnDone = null;
            d?.Invoke(this);
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
            // 主动驱动：每 tick 自己往前推一格，不依赖流推送
            while (true)
            {
                int targetLen = _pending.Length;

                if (_shownCount < targetLen)
                {
                    _shownCount++;
                    if (_display != null)
                        _display.text = _headerLine + "\n" + _pending.ToString(0, _shownCount);
                    yield return new WaitForSeconds(CharInterval);
                    continue;
                }

                // 已追上 pending：等 Flush 信号
                if (_flushed)
                {
                    // Flush 后给一小段 grace 期：避免刚 Flush 就有字符没到就提前结束
                    while (_flushed
                           && Time.realtimeSinceStartup - _flushTime < FlushGraceSeconds
                           && _shownCount >= _pending.Length)
                    {
                        yield return new WaitForSeconds(CharInterval);
                    }
                    if (_shownCount >= _pending.Length)
                    {
                        if (_display != null)
                            _display.text = _headerLine + "\n" + _pending.ToString();
                        _done = true;
                        var d = OnDone;
                        OnDone = null;
                        d?.Invoke(this);
                        yield break;
                    }
                    continue;
                }

                // 既没有新字符也未 Flush → 静止等待
                yield return null;
            }
        }
    }
}
