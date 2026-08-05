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
