using TMPro;
using UnityEngine;

namespace ACLS.UI
{
    /// <summary>
    /// 打字机文本动画控制器。不自带 TMP，通过 Assign() 绑定外部 TMP 进行逐字显示。
    /// 流式数据通过 Feed/Flush 注入，打完触发 OnDone 通知调用方回收。
    /// </summary>
    public sealed class TypewriterSlot : MonoBehaviour
    {
        private const float CharInterval = 0.025f;
        private const float DotsInterval = 0.5f;

        private TextMeshProUGUI _display;
        private string _headerLine;
        private string _fullText = "";
        private int _shownCount;
        private bool _started;
        private bool _flushed;
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
            _fullText = "";
            _shownCount = 0;
            _started = false;
            _flushed = false;
            _done = false;

            if (_display != null)
                _display.text = _headerLine + "\n<color=#7c7c8a>思考中</color>";

            _routine = StartCoroutine(AnimateRoutine());
        }

        /// <summary>流式数据注入（完整最新文本）。</summary>
        public void Feed(string escapedText)
        {
            if (_done || _display == null) return;
            _fullText = escapedText ?? "";
            _started = true;
        }

        /// <summary>告知全部文本已到达。参数为最终文本，可省略（沿用上次 Feed 的值）。</summary>
        public void Flush(string escapedText)
        {
            if (_done || _display == null) return;
            _fullText = escapedText ?? "";
            _started = true;
            _flushed = true;
        }

        /// <summary>仅标记完成，不更新文本（已通过 Feed 拿到最终文本时用）。</summary>
        public void Flush()
        {
            if (_done || _display == null) return;
            _flushed = true;
        }

        /// <summary>立刻完成——新回合打断时调用。</summary>
        public void FinishNow()
        {
            if (_done) return;
            _done = true;
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }

            if (_display != null)
            {
                _display.text = _started
                    ? _headerLine + "\n" + _fullText
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
            while (!_started)
            {
                dots = (dots % 4) + 1;
                if (_display != null)
                    _display.text = _headerLine + "\n<color=#7c7c8a>思考中" + new string('\u00b7', dots) + "</color>";
                yield return new WaitForSeconds(DotsInterval);
            }

            // Phase 2 — 逐字显示
            while (true)
            {
                int targetLen = _fullText.Length;
                if (_shownCount >= targetLen)
                {
                    if (_flushed)
                    {
                        if (_display != null)
                            _display.text = _headerLine + "\n" + _fullText;
                        _done = true;
                        var d = OnDone;
                        OnDone = null;
                        d?.Invoke(this);
                        yield break;
                    }
                    yield return null;
                    continue;
                }

                _shownCount++;
                if (_display != null)
                    _display.text = _headerLine + "\n" + _fullText.Substring(0, _shownCount);
                yield return new WaitForSeconds(CharInterval);
            }
        }
    }
}
