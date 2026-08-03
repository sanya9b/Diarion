using System.Collections.Generic;
using System.Linq;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>
/// The models the app knows how to install.
/// </summary>
/// <remarks>
/// A compile-time constant, not a downloaded manifest. The app has to be able to describe what it
/// could run while offline, and a remote catalogue would make the settings screen depend on the
/// network to render. Sizes and digests come from HuggingFace's own file metadata, so they can be
/// checked against the source without downloading anything.
/// </remarks>
public static class AiModelCatalog
{
    public const string MiniLmEncoderId = "paraphrase-multilingual-MiniLM-L12-v2-int8";

    /// <summary>
    /// Multilingual sentence encoder, 118M parameters, 384 dimensions, Apache-2.0.
    /// </summary>
    /// <remarks>
    /// The <c>qint8_arm64</c>, <c>qint8_avx512</c> and <c>qint8_avx512_vnni</c> exports are
    /// byte-identical — same digest, same size — so despite the naming there is no per-architecture
    /// build to choose between, and one file serves Android, iOS and Windows alike.
    /// </remarks>
    public static readonly AiModelDescriptor MiniLmEncoder = new()
    {
        Id = MiniLmEncoderId,
        Kind = AiModelKind.Embedding,
        DisplayName = "Multilingual MiniLM L12 v2 (int8)",
        RepoId = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2",
        RevisionSha = "e8f8c211226b894fcb81acc59f3b34ba3efd5f42",
        Files =
        [
            new AiModelFile(
                RemotePath: "onnx/model_qint8_arm64.onnx",
                LocalName: "model.onnx",
                SizeBytes: 118_412_398,
                Sha256: "783fea82d71a58179b830a4dbd2d58447e640609e98eedf9ffa12622d375a672"),
            new AiModelFile(
                RemotePath: "sentencepiece.bpe.model",
                LocalName: "sentencepiece.bpe.model",
                SizeBytes: 5_069_051,
                Sha256: "cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"),
        ],
        RequiredRamMb = 512,
        MinTier = DeviceTier.Low,
        LicenseSpdx = "Apache-2.0",
        Dimensions = 384,
        MaxTokens = 512,
        Quantization = "int8",
    };

    public static IReadOnlyList<AiModelDescriptor> All { get; } = [MiniLmEncoder];

    public static IEnumerable<AiModelDescriptor> OfKind(AiModelKind kind) => All.Where(m => m.Kind == kind);

    public static AiModelDescriptor? FindById(string? id) => All.FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// The best model of a kind this device should be offered, or null when none fits. Storage is
    /// held to twice the model size: filling a phone to install a search index is not a trade the
    /// user would thank us for.
    /// </summary>
    public static AiModelDescriptor? Recommend(AiModelKind kind, DeviceCapabilities capabilities) =>
        OfKind(kind)
            .Where(m => m.MinTier <= capabilities.Tier)
            .Where(m => m.RequiredRamMb <= capabilities.TotalRamMb)
            .Where(m => m.TotalSizeBytes * 2 <= capabilities.AvailableStorageBytes)
            .OrderByDescending(m => m.RequiredRamMb)
            .FirstOrDefault();
}
