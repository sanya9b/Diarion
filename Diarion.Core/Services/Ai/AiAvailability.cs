using System.Threading.Tasks;
using Diarion.Services;

namespace Diarion.Services.Ai;

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
/// <summary>
/// Whether the app offers on-device AI at all. Off for everyone since 2026-08-10.
/// </summary>
/// <remarks>
/// The models earned this. A 1.1 GB download bought search results a keyword query already found
/// and answers slow enough to be read as a hang, so the feature is retired rather than defended.
/// Its place is taken by periodic reports built through an API, which the user turns on themselves.
///
/// Nothing is deleted and nothing forks: this one flag decides both which <see cref="IAiAvailability"/>
/// the container hands out and whether the AI tab exists in settings. Flip it to <c>true</c> and
/// search, chat, digests, themes and the model downloader all come back as they were.
/// </remarks>
public static class OnDeviceAi
{
    public static bool IsOffered { get; } = false;
}

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
