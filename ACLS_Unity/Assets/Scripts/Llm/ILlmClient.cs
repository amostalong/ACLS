using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ACLS.Llm
{
    public interface ILlmClient
    {
        // Returns the assistant content + token usage. Caller parses JSON /
        // does post-processing. Implementations should throw on HTTP errors
        // with a message suitable for surfacing to the user.
        Task<LlmResponse> CompleteAsync(string systemPrompt,
                                        IReadOnlyList<ChatMessage> messages,
                                        CancellationToken ct);

        Task<LlmResponse> CompleteStreamAsync(string systemPrompt,
                                              IReadOnlyList<ChatMessage> messages,
                                              System.Action<string> onTextDelta,
                                              CancellationToken ct);
    }
}
