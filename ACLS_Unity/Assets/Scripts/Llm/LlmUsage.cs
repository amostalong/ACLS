using System;

namespace ACLS.Llm
{
    [Serializable]
    public struct LlmUsage
    {
        public int InputTokens;
        public int OutputTokens;

        public int Total => InputTokens + OutputTokens;
        public bool HasData => InputTokens > 0 || OutputTokens > 0;

        public static LlmUsage operator +(LlmUsage a, LlmUsage b) => new LlmUsage
        {
            InputTokens = a.InputTokens + b.InputTokens,
            OutputTokens = a.OutputTokens + b.OutputTokens,
        };
    }

    public sealed class LlmResponse
    {
        public string Content;
        public LlmUsage Usage;
    }
}
