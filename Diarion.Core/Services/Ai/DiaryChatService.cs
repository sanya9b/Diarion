using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

public class DiaryChatService : IDiaryChatService
{
    /// <summary>
    /// Passages pulled before diversification. Wider than what reaches the prompt so MMR has room
    /// to trade relevance for variety.
    /// </summary>
    private const int Candidates = 24;

    /// <summary>
    /// Long enough for a paragraph with citations, short enough that a phone CPU finishes. A small
    /// model given a larger budget does not answer better, it repeats itself for longer.
    /// </summary>
    private const int MaxAnswerTokens = 320;

    private readonly IVectorStore _store;
    private readonly ITextEmbedder _embedder;
    private readonly ITextGenerator _generator;
    private readonly IAiAvailability _availability;

    public DiaryChatService(
        IVectorStore store,
        ITextEmbedder embedder,
        ITextGenerator generator,
        IAiAvailability availability)
    {
        _store = store;
        _embedder = embedder;
        _generator = generator;
        _availability = availability;
    }

    public Task<bool> IsAvailableAsync() => _availability.CanGenerateAsync();

    public async IAsyncEnumerable<ChatDelta> AskAsync(
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await _availability.CanGenerateAsync().ConfigureAwait(false))
        {
            yield return Refusal(ChatRefusalReason.Unavailable);
            yield break;
        }

        var queryVector = await _embedder.EmbedAsync(question, cancellationToken).ConfigureAwait(false);
        var retrieved = _store.Search(
            queryVector,
            _embedder.ModelId,
            Candidates,
            SearchScope.All,
            PromptBuilder.MinRelevance);

        var prompt = PromptBuilder.Build(question, retrieved);
        if (!prompt.IsAnswerable)
        {
            // The model is never invoked. A small model told to say "I do not know" will sometimes
            // answer anyway; one that is not called cannot.
            yield return Refusal(ChatRefusalReason.NothingRelevant);
            yield break;
        }

        var full = new StringBuilder();
        var reasoning = new ReasoningFilter();

        await foreach (var token in _generator
                           .StreamAsync(prompt.Text, MaxAnswerTokens, cancellationToken)
                           .ConfigureAwait(false))
        {
            // Raw for the record, filtered for the screen: a reasoning model narrates its way to an
            // answer, and neither the user nor the citation parser should be reading that.
            full.Append(token);

            var visible = reasoning.Push(token);
            if (visible.Length > 0)
            {
                yield return new ChatDelta(visible);
            }
        }

        var tail = reasoning.Flush();
        if (tail.Length > 0)
        {
            yield return new ChatDelta(tail);
        }

        var parsed = CitationParser.Parse(ReasoningFilter.Strip(full.ToString()), prompt.Citations);

        yield return parsed.IsRefusal
            // Streamed text is discarded here on purpose: the UI shows the answer arriving and then
            // replaces it, which is honest, where keeping an uncited answer on screen would not be.
            ? Refusal(ChatRefusalReason.Ungrounded)
            : new ChatDelta(string.Empty, IsComplete: true, new ChatResult(parsed.Text, parsed.Used, ChatRefusalReason.None));
    }

    private static ChatDelta Refusal(ChatRefusalReason reason) =>
        new(string.Empty, IsComplete: true, new ChatResult(string.Empty, [], reason));
}
