using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Downloads the real encoder and runs it. Skipped unless <c>DIARION_AI_INTEGRATION=1</c>, because
/// it fetches ~123 MB and CI should not do that on every push.
/// </summary>
/// <remarks>
/// This is the only test that can catch the fairseq remap being wrong. Bad ids still produce
/// finite, correctly shaped, plausibly distributed vectors — the unit tests pin the mapping rule,
/// but only a real model can show that the rule is the one this checkpoint was trained with.
/// </remarks>
public class OnnxTextEmbedderIntegrationTests
{
    private const string EnableVariable = "DIARION_AI_INTEGRATION";

    private static string CacheRoot =>
        Environment.GetEnvironmentVariable("DIARION_AI_MODEL_CACHE")
        ?? Path.Combine(Path.GetTempPath(), "diarion-ai-models");

    [EnvironmentGatedFact(EnableVariable, "Downloads ~123 MB.")]
    public async Task TheRealEncoder_PlacesRelatedUkrainianWordsCloserThanUnrelatedOnes()
    {
        var embedder = await LoadAsync();

        var dog = await embedder.EmbedAsync("собака");
        var puppy = await embedder.EmbedAsync("цуценя");
        var bureaucracy = await embedder.EmbedAsync("бюрократія");

        var related = EmbeddingMath.DotNormalized(dog, puppy);
        var unrelated = EmbeddingMath.DotNormalized(dog, bureaucracy);

        related.Should().BeGreaterThan(unrelated,
            "a wrong fairseq remap still yields well-formed vectors, and this is what tells them apart");
    }

    [EnvironmentGatedFact(EnableVariable, "Downloads ~123 MB.")]
    public async Task TheRealEncoder_MatchesAcrossLanguages()
    {
        var embedder = await LoadAsync();

        var ukrainian = await embedder.EmbedAsync("я погано спав цієї ночі");
        var english = await embedder.EmbedAsync("I slept badly last night");
        var unrelated = await embedder.EmbedAsync("вартість оренди квартири");

        EmbeddingMath.DotNormalized(ukrainian, english)
            .Should().BeGreaterThan(EmbeddingMath.DotNormalized(ukrainian, unrelated));
    }

    [EnvironmentGatedFact(EnableVariable, "Downloads ~123 MB.")]
    public async Task TheRealEncoder_FindsAnEntryByMeaningRatherThanWords()
    {
        var embedder = await LoadAsync();

        // The point of the whole feature: no word is shared between query and entry.
        var query = await embedder.EmbedAsync("що я писав про роботу");
        var aboutWork = await embedder.EmbedAsync("сьогодні знову засидівся в офісі до ночі через дедлайн");
        var aboutSomethingElse = await embedder.EmbedAsync("купив полуницю на базарі, дуже смачна");

        EmbeddingMath.DotNormalized(query, aboutWork)
            .Should().BeGreaterThan(EmbeddingMath.DotNormalized(query, aboutSomethingElse));
    }

    [EnvironmentGatedFact(EnableVariable, "Downloads ~123 MB.")]
    public async Task TheRealEncoder_ProducesUnitVectorsOfTheAdvertisedWidth()
    {
        var embedder = await LoadAsync();

        var vector = await embedder.EmbedAsync("звичайний запис у щоденнику");

        vector.Should().HaveCount(AiModelCatalog.MiniLmEncoder.Dimensions);
        EmbeddingMath.DotNormalized(vector, vector).Should().BeApproximately(1f, 1e-4f);
    }

    [EnvironmentGatedFact(EnableVariable, "Downloads ~123 MB.")]
    public async Task TheCatalogueDigestsMatchWhatHuggingFaceServes()
    {
        // DownloadAsync verifies every digest and deletes anything that fails, so a successful
        // download is the assertion: the pinned commit, the paths and the hashes are all real.
        var downloaded = await DownloadAsync();

        downloaded.Should().BeTrue();
    }

    private static async Task<bool> DownloadAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        var service = new ModelDownloadService(http, new CachePathProvider());

        var model = AiModelCatalog.MiniLmEncoder;
        if (service.GetState(model) == ModelInstallState.Installed)
        {
            return true;
        }

        return await service.DownloadAsync(model);
    }

    private static async Task<OnnxTextEmbedder> LoadAsync()
    {
        (await DownloadAsync()).Should().BeTrue("the encoder has to be present before it can be run");

        return new OnnxTextEmbedder(new CacheLocator());
    }

    private sealed class CachePathProvider : IAiModelPathProvider
    {
        public string GetModelDirectory(string modelId) => Path.Combine(CacheRoot, modelId);
    }

    private sealed class CacheLocator : IEmbeddingModelLocator
    {
        public EmbeddingModelFiles? TryLocate()
        {
            var model = AiModelCatalog.MiniLmEncoder;
            var directory = Path.Combine(CacheRoot, model.Id);

            return new EmbeddingModelFiles(
                model.Id,
                Path.Combine(directory, "model.onnx"),
                Path.Combine(directory, "sentencepiece.bpe.model"),
                model.Dimensions,
                model.MaxTokens);
        }
    }
}
