using System.Linq;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class AiModelCatalogTests
{
    private static DeviceCapabilities Device(int ramMb, long freeBytes = 100L * 1024 * 1024 * 1024, int cores = 8, bool is64Bit = true) =>
        new(ramMb, freeBytes, cores, is64Bit);

    [Theory]
    [InlineData(2048, 8, true, DeviceTier.Low)]
    [InlineData(3999, 8, true, DeviceTier.Low)]
    [InlineData(4096, 4, true, DeviceTier.Mid)]
    [InlineData(6144, 4, true, DeviceTier.Mid)]  // enough memory, too few cores for the top tier
    [InlineData(6144, 6, true, DeviceTier.High)]
    [InlineData(8192, 8, true, DeviceTier.High)]
    [InlineData(8192, 8, false, DeviceTier.Low)] // 32-bit cannot map a large model however much RAM
    public void Tier_FollowsMemoryArchitectureAndCores(int ramMb, int cores, bool is64Bit, DeviceTier expected)
    {
        Device(ramMb, cores: cores, is64Bit: is64Bit).Tier.Should().Be(expected);
    }

    [Fact]
    public void Recommend_LowEndDevice_StillGetsTheEncoder()
    {
        // The whole point of the tiering is that search, themes and reports work everywhere; only
        // generation is gated.
        AiModelCatalog.Recommend(AiModelKind.Embedding, Device(2048))
            .Should().Be(AiModelCatalog.MiniLmEncoder);
    }

    [Fact]
    public void Recommend_NoRoomOnDisk_RecommendsNothing()
    {
        // Twice the model size, because filling a phone to install a search index is not a trade
        // the user would thank us for.
        var barelyEnoughForOneCopy = AiModelCatalog.MiniLmEncoder.TotalSizeBytes + 1;

        AiModelCatalog.Recommend(AiModelKind.Embedding, Device(8192, freeBytes: barelyEnoughForOneCopy))
            .Should().BeNull();
    }

    [Fact]
    public void Recommend_NoGenerativeModelYet_ReturnsNull()
    {
        AiModelCatalog.Recommend(AiModelKind.Generation, Device(8192)).Should().BeNull();
    }

    [Fact]
    public void FindById_RoundTripsEveryCatalogueEntry()
    {
        foreach (var model in AiModelCatalog.All)
        {
            AiModelCatalog.FindById(model.Id).Should().Be(model);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-model")]
    public void FindById_Unknown_IsNull(string? id)
    {
        AiModelCatalog.FindById(id).Should().BeNull();
    }

    [Fact]
    public void EveryEntry_PinsACommitAndADigestForEveryFile()
    {
        // A branch name would let the bytes change under a released app version, and a missing
        // digest would make the verification step a no-op.
        foreach (var model in AiModelCatalog.All)
        {
            model.RevisionSha.Should().MatchRegex("^[0-9a-f]{40}$", $"{model.Id} must pin a commit");
            model.Files.Should().NotBeEmpty();

            foreach (var file in model.Files)
            {
                file.Sha256.Should().MatchRegex("^[0-9a-f]{64}$", $"{model.Id}/{file.LocalName} needs a digest");
                file.SizeBytes.Should().BePositive();
            }
        }
    }

    [Fact]
    public void EncoderMatchesWhatTheRuntimeExpectsToLoad()
    {
        var encoder = AiModelCatalog.MiniLmEncoder;

        encoder.Dimensions.Should().Be(384);
        encoder.MaxTokens.Should().Be(512);
        encoder.Files.Select(f => f.LocalName)
            .Should().Contain(["model.onnx", "sentencepiece.bpe.model"]);
    }
}
