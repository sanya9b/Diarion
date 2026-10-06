using System.Collections.Generic;

namespace Diarion.Models.Ai;

public enum AiModelKind
{
    /// <summary>Sentence encoder. Powers search, themes and digests.</summary>
    Embedding,

    /// <summary>Text generator. Powers the grounded chat only.</summary>
    Generation,
}

/// <summary>
/// What a device can be asked to run. Derived from RAM, architecture and free storage — never from
/// a model name, marketing tier, or Android version.
/// </summary>
public enum DeviceTier
{
    /// <summary>Under 4 GB or 32-bit: embeddings only.</summary>
    Low = 0,

    /// <summary>4–6 GB: the smallest generative model.</summary>
    Mid = 1,

    /// <summary>6 GB and up with enough cores: everything in the catalogue.</summary>
    High = 2,
}

/// <summary>One downloadable file, pinned by content.</summary>
/// <param name="RemotePath">Path inside the HuggingFace repository.</param>
/// <param name="LocalName">Filename on disk, which the runtime looks for by convention.</param>
/// <param name="Sha256">
/// Expected digest, verified after download. For LFS files this is the object id HuggingFace
/// publishes, so it can be checked against the source without downloading first.
/// </param>
public sealed record AiModelFile(string RemotePath, string LocalName, long SizeBytes, string Sha256);

/// <summary>
/// A model the user can install. Everything needed to fetch it, judge whether this device should,
/// and stamp the rows it produces.
/// </summary>
public sealed record AiModelDescriptor
{
    public required string Id { get; init; }

    public required AiModelKind Kind { get; init; }

    /// <summary>Shown in settings. Not localized — a model name is a proper noun.</summary>
    public required string DisplayName { get; init; }

    /// <summary>HuggingFace repository, e.g. <c>sentence-transformers/model-name</c>.</summary>
    public required string RepoId { get; init; }

    /// <summary>
    /// Commit the files are pinned to. A branch name would let the bytes change under a released
    /// app version; a commit cannot.
    /// </summary>
    public required string RevisionSha { get; init; }

    public required IReadOnlyList<AiModelFile> Files { get; init; }

    /// <summary>Free-storage guard and the number shown to the user before they commit to a download.</summary>
    public long TotalSizeBytes => Files.Sum(f => f.SizeBytes);

    public required int RequiredRamMb { get; init; }

    public required DeviceTier MinTier { get; init; }

    public required string LicenseSpdx { get; init; }

    /// <summary>Vector width. Zero for generative models.</summary>
    public int Dimensions { get; init; }

    /// <summary>Sequence length the graph was exported with.</summary>
    public int MaxTokens { get; init; }

    public string Quantization { get; init; } = string.Empty;

    /// <summary>
    /// Direct download URL for one of this model's files. HuggingFace serves LFS objects from
    /// <c>/resolve/</c>, and pinning the commit makes the response immutable.
    /// </summary>
    public string BuildFileUrl(AiModelFile file) =>
        $"https://huggingface.co/{RepoId}/resolve/{RevisionSha}/{file.RemotePath}";
}
