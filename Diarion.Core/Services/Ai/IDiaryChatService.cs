using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <summary>Why the assistant declined to answer. The UI says which; none of them is a failure.</summary>
public enum ChatRefusalReason
{
    None,

    /// <summary>No generative model installed.</summary>
    Unavailable,

    /// <summary>Nothing in the diary came close enough to the question.</summary>
    NothingRelevant,

    /// <summary>The model answered without citing anything it was given.</summary>
    Ungrounded,
}

/// <param name="Delta">Text produced since the previous update. Empty on the final message.</param>
/// <param name="IsComplete">True on the last message, when <paramref name="Answer"/> is filled in.</param>
public sealed record ChatDelta(string Delta, bool IsComplete = false, ChatResult? Answer = null);

/// <param name="Refusal">Why there is no answer, or <see cref="ChatRefusalReason.None"/>.</param>
public sealed record ChatResult(string Text, IReadOnlyList<ChatCitation> Citations, ChatRefusalReason Refusal)
{
    public bool IsRefusal => Refusal != ChatRefusalReason.None;
}

/// <summary>
/// Answers questions about the diary, using only the diary.
/// </summary>
/// <remarks>
/// Two gates, neither of them a prompt instruction. Retrieval too weak and the model is never
/// invoked; an answer that cites nothing is discarded. A small model inventing something about the
/// user's own life in their own diary is the worst thing this feature could do, so the guarantee is
/// made in code where it can be tested.
/// </remarks>
public interface IDiaryChatService
{
    bool IsAvailable { get; }

    IAsyncEnumerable<ChatDelta> AskAsync(string question, CancellationToken cancellationToken = default);
}
