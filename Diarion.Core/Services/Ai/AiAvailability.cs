using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services;

namespace Diarion.Services.Ai;

/// <summary>
/// Which local models the app offers, per kind. Nothing is deleted: the pages, the services and
/// their tests all stay, and these flags decide what the container hands out and what settings show.
/// </summary>
/// <remarks>
/// The generative model earned its retirement. A 981 MB download bought answers slow enough to be
/// read as a hang, and its place is taken by periodic reports built through an API, which the user
/// turns on themselves. The encoder did not: it is 123 MB, it runs in the background, and the themes
/// in statistics and the mood factors are built on it.
///
/// So the two are no longer one switch. Flip <see cref="GenerationOffered"/> back to <c>true</c> and
/// chat, its quick-menu tile and the Qwen3 row in settings return exactly as they were.
/// </remarks>
public static class OnDeviceAi
{
    /// <summary>The encoder: themes, digests and mood factors run on it.</summary>
    public static bool EmbeddingsOffered { get; } = true;

    /// <summary>Chat and everything that writes sentences. Off for everyone since 2026-08-10.</summary>
    public static bool GenerationOffered { get; } = false;

    /// <summary>Anything local at all — this is what decides whether settings has an AI tab.</summary>
    public static bool IsOffered => EmbeddingsOffered || GenerationOffered;

    /// <summary>Whether a model of this kind is worth showing in settings or downloading.</summary>
    public static bool Offers(AiModelKind kind) => kind switch
    {
        AiModelKind.Embedding => EmbeddingsOffered,
        AiModelKind.Generation => GenerationOffered,
        _ => false
    };
}

/// <summary>
/// Whether the AI stack may touch the diary at all, and with what.
/// </summary>
/// <remarks>
/// One definition, injected everywhere, because the alternative was tried and it failed quietly:
/// the consent toggle was honoured by the indexer alone, so turning AI off stopped the index
/// growing while chat, search, themes and digests carried on reading the index already on disk.
/// The switch said off and the models kept reading.
///
/// Consent and capability are separate facts and neither substitutes for the other. An installed
/// model is not permission, and permission without a model is not a feature.
/// </remarks>
public interface IAiAvailability
{
    /// <summary>Embeddings may be written or read: an encoder is installed and AI is turned on.</summary>
    Task<bool> CanEmbedAsync();

    /// <summary>Answers may be generated: everything above, plus an installed generative model.</summary>
    Task<bool> CanGenerateAsync();
}

public class AiAvailability : IAiAvailability
{
    private readonly ITextEmbedder _embedder;
    private readonly ITextGenerator _generator;
    private readonly IProfileService _profileService;

    public AiAvailability(ITextEmbedder embedder, ITextGenerator generator, IProfileService profileService)
    {
        _embedder = embedder;
        _generator = generator;
        _profileService = profileService;
    }

    public async Task<bool> CanEmbedAsync()
    {
        // The file check first: it is a stat call, where the profile is a database read, and this
        // runs on every search keystroke.
        if (!_embedder.IsAvailable)
        {
            return false;
        }

        var profile = await _profileService.GetUserProfileAsync().ConfigureAwait(false);
        return profile.IsAiEnabled;
    }

    public async Task<bool> CanGenerateAsync() =>
        _generator.IsAvailable && await CanEmbedAsync().ConfigureAwait(false);
}

/// <summary>
/// Says no to everything, for <see cref="OnDeviceAi.IsOffered"/> being false.
/// </summary>
/// <remarks>
/// The stack is retired at the gate and nowhere else. Because every consumer asks this one
/// question, switching the implementation stops the indexer, drops semantic search back to
/// lexical, empties the digest and the theme clusters, and hides chat — without a single
/// <c>if</c> appearing in any of them.
/// </remarks>
public class DisabledAiAvailability : IAiAvailability
{
    public Task<bool> CanEmbedAsync() => Task.FromResult(false);

    public Task<bool> CanGenerateAsync() => Task.FromResult(false);
}

/// <summary>
/// Embeddings answered as usual, generation refused — the shape of the stack while
/// <see cref="OnDeviceAi.GenerationOffered"/> is false.
/// </summary>
/// <remarks>
/// A wrapper rather than an <c>if</c> inside <see cref="AiAvailability"/>, for the same reason
/// <see cref="DisabledAiAvailability"/> is one: what the app offers is decided at the gate, and the
/// class that weighs an installed model against consent stays a pure answer to a pure question.
/// The refusal is unconditional on purpose — an installed Qwen3 left over from an older build must
/// not be able to talk it round.
/// </remarks>
public class EmbeddingsOnlyAiAvailability : IAiAvailability
{
    private readonly IAiAvailability _inner;

    public EmbeddingsOnlyAiAvailability(IAiAvailability inner) => _inner = inner;

    public Task<bool> CanEmbedAsync() => _inner.CanEmbedAsync();

    public Task<bool> CanGenerateAsync() => Task.FromResult(false);
}
