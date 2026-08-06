using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <summary>
/// Turns text into a vector. The only interface in the AI module backed by a native handle, so it
/// stays deliberately small — everything that can be reasoned about lives on the Core side of it.
/// </summary>
public interface ITextEmbedder
{
    /// <summary>Identifier of the loaded model. Stamped on every row it produces.</summary>
    string ModelId { get; }

    /// <summary>Width of the vectors this model emits.</summary>
    int Dimensions { get; }

    /// <summary>
    /// False when no model is installed. Callers degrade rather than throw: the app has to stay
    /// usable when the download has not happened, or has been evicted.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Embeds one text. The result is L2-normalized.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds several texts, in the same order. Results are L2-normalized.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Frees the native model. The next call reloads it, so this is a memory decision rather than a
    /// lifecycle one: on app sleep, and on a tight device between retrieval and generation, where
    /// the encoder is finished for the turn and the generator is about to want every megabyte.
    /// </summary>
    void Unload();
}

/// <summary>
/// Stand-in used when AI is switched off or no model is installed. Registered by default so the
/// container always resolves, and so nothing downstream needs a null check.
/// </summary>
public sealed class NullTextEmbedder : ITextEmbedder
{
    public string ModelId => string.Empty;

    public int Dimensions => 0;

    public bool IsAvailable => false;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No embedding model is installed. Check IsAvailable before embedding.");

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No embedding model is installed. Check IsAvailable before embedding.");

    /// <summary>Nothing is loaded, so nothing to free.</summary>
    public void Unload()
    {
    }
}
