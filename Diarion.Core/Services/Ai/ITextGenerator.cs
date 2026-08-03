using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <summary>
/// Produces text from a prompt. The second and last interface backed by a native handle.
/// </summary>
public interface ITextGenerator
{
    string ModelId { get; }

    /// <summary>False when no generative model is installed. Chat is hidden rather than failing.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Streams the answer token by token. Streaming is not decoration: on a phone CPU a two-hundred
    /// token answer takes long enough that watching it arrive is the difference between "thinking"
    /// and "frozen".
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default);
}

/// <summary>Stand-in when no generative model is installed, so the container always resolves.</summary>
public sealed class NullTextGenerator : ITextGenerator
{
    public string ModelId => string.Empty;

    public bool IsAvailable => false;

    public IAsyncEnumerable<string> StreamAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No generative model is installed. Check IsAvailable first.");
}
