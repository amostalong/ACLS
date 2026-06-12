using System;
using ACLS.Llm;

namespace ACLS.UI
{
    public enum ChatBlockKind { Static, Streaming }

    /// <summary>
    /// A single displayable unit pushed from ChatBridge to ChatPanelView.
    ///
    /// The block is the shared sync handle: ChatBridge mutates CurrentStreamText
    /// and StreamFlushed during streaming; TypewriterSlot polls them on its
    /// coroutine tick. Both writer and reader run on the main thread.
    /// </summary>
    public sealed class ChatBlock
    {
        public ChatRole Role;
        public string HeaderPrefix;     // "旁白", "你", "系统" — for the colored header
        public DateTime Timestamp;
        public string MetaSuffix;       // optional right-aligned trailer (e.g. token usage)

        public ChatBlockKind Kind;

        // For Static: the final text. Set at construction, never changes.
        public string StaticText;

        // For Streaming: written by ChatBridge, read by TypewriterSlot.
        public string CurrentStreamText;
        public bool StreamFlushed;

        public bool IsStreaming => Kind == ChatBlockKind.Streaming;

        public static ChatBlock Static(ChatRole role, string prefix, string text) =>
            new ChatBlock
            {
                Kind = ChatBlockKind.Static,
                Role = role,
                HeaderPrefix = prefix ?? "",
                Timestamp = DateTime.Now,
                StaticText = text ?? "",
            };

        public static ChatBlock Streaming(ChatRole role, string prefix) =>
            new ChatBlock
            {
                Kind = ChatBlockKind.Streaming,
                Role = role,
                HeaderPrefix = prefix ?? "",
                Timestamp = DateTime.Now,
                CurrentStreamText = "",
            };
    }
}
