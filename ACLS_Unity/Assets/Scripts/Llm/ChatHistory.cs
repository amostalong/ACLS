using System.Collections.Generic;

namespace ACLS.Llm
{
    // In-memory conversation log. Step 1 keeps everything; Step 4 will summarize.
    public sealed class ChatHistory
    {
        private readonly List<ChatMessage> messages = new List<ChatMessage>();

        public IReadOnlyList<ChatMessage> All => messages;

        public void Add(ChatMessage m) => messages.Add(m);

        public IReadOnlyList<ChatMessage> Recent(int n)
        {
            if (n <= 0 || messages.Count == 0) return System.Array.Empty<ChatMessage>();
            int start = System.Math.Max(0, messages.Count - n);
            return messages.GetRange(start, messages.Count - start);
        }

        public void Clear() => messages.Clear();
    }
}
