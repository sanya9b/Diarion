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

    public const string Qwen3GeneratorId = "qwen3-1.7b-int4";

    /// <summary>
    /// Qwen3-1.7B, converted to ORT-GenAI INT4 and republished, because no such build existed.
    /// Apache-2.0, 100+ languages including Ukrainian.
    /// </summary>
    /// <remarks>
    /// Chosen over Qwen3-0.6B on measurement, not size. On a Ukrainian retrieval-QA set 0.6B scored
    /// 1/4 and answered "when did I sleep best" with the worst night in the set, correctly cited;
    /// this one scores 4/4 once the prompt carries a worked example. It is High tier because 1.1 GB
    /// resident is not something a 4 GB phone should be asked to hold while the user is typing.
    ///
    /// Built with builder 0.15.x — 0.14.1 segfaults on this model — and verified to load under the
    /// 0.14.1 runtime the app pins. Building happens on a desktop and never touches the manifest,
    /// so the version that protects the permission list does not constrain the model.
    /// </remarks>
    public static readonly AiModelDescriptor Qwen3Generator = new()
    {
        Id = Qwen3GeneratorId,
        Kind = AiModelKind.Generation,
        DisplayName = "Qwen3 1.7B (int4)",
        RepoId = "Hug0007/qwen3-1.7b-int4-onnx-genai",
        RevisionSha = "fd1e1e4bc66ad91531525d645ebbbbe14a186a92",
        Files =
        [
            // ORT-GenAI loads the folder, so every file has to land beside the others under its
            // original name — genai_config.json names them.
            new AiModelFile("genai_config.json", "genai_config.json", 1_571,
                "cb89e5aa137cbeaea7ca3148f4255afae6f9190688a038902084600492a6d998"),
            new AiModelFile("model.onnx", "model.onnx", 299_439,
                "3f60cbbe99f7c8f1cd6ce54f7108b15b80eb5c4efc90bf0b92e4f16074442a20"),
            new AiModelFile("model.onnx.data", "model.onnx.data", 1_096_810_496,
                "3cb60c8bf33f4e3fb0d5ef02acc92af0120e1f2b57359518bab82aadcdda5a22"),
            new AiModelFile("tokenizer.json", "tokenizer.json", 11_422_650,
                "be75606093db2094d7cd20f3c2f385c212750648bd6ea4fb2bf507a6a4c55506"),
            new AiModelFile("tokenizer_config.json", "tokenizer_config.json", 722,
                "70dbff0c676db3313a55d21970d115b24a9bbc3f8bf7682c7d73cc948c7f7344"),
            // Not optional: without the chat template the model continues the prompt instead of
            // answering it.
            new AiModelFile("chat_template.jinja", "chat_template.jinja", 4_256,
                "51fa65c79bb57f058dc7ef8734884bd325fe9d45bb03a61bfef59785d3bc2da9"),
        ],
        RequiredRamMb = 6144,
        MinTier = DeviceTier.High,
        LicenseSpdx = "Apache-2.0",
        Quantization = "int4",
    };

    public static IReadOnlyList<AiModelDescriptor> All { get; } = [MiniLmEncoder, Qwen3Generator];

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
