using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The single place where "may the AI read this diary" is decided. Every consumer delegates here,
/// so these are the tests that stand behind the promise the settings toggle makes.
/// </summary>
public class AiAvailabilityTests
{
    private readonly Mock<IProfileService> _profiles = new();

    private AiAvailability Build(bool encoderInstalled, bool generatorInstalled, bool aiEnabled)
    {
        _profiles.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { IsAiEnabled = aiEnabled });

        return new AiAvailability(
            encoderInstalled ? new StubAvailableEmbedder() : new NullTextEmbedder(),
            generatorInstalled ? new StubAvailableGenerator() : new NullTextGenerator(),
            _profiles.Object);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // consent withheld
    [InlineData(false, true, false)]   // nothing installed to run
    [InlineData(false, false, false)]
    public async Task CanEmbed_NeedsBothAnEncoderAndConsent(bool installed, bool enabled, bool expected)
    {
        (await Build(installed, generatorInstalled: true, enabled).CanEmbedAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task CanGenerate_WithoutAGenerativeModel_IsFalseEvenWhenEverythingElseIsReady()
    {
        var availability = Build(encoderInstalled: true, generatorInstalled: false, aiEnabled: true);

        (await availability.CanGenerateAsync()).Should().BeFalse();
        (await availability.CanEmbedAsync()).Should().BeTrue("search and themes still work without generation");
    }

    [Fact]
    public async Task CanGenerate_WithoutConsent_IsFalseThoughBothModelsAreInstalled()
    {
        var availability = Build(encoderInstalled: true, generatorInstalled: true, aiEnabled: false);

        (await availability.CanGenerateAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CanGenerate_EverythingReady_IsTrue()
    {
        var availability = Build(encoderInstalled: true, generatorInstalled: true, aiEnabled: true);

        (await availability.CanGenerateAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ConsentIsNotCachedAcrossCalls()
    {
        // The toggle is two taps away while a page is open. A value read once at construction would
        // keep the old answer until the app restarted.
        var availability = Build(encoderInstalled: true, generatorInstalled: true, aiEnabled: true);
        (await availability.CanEmbedAsync()).Should().BeTrue();

        _profiles.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { IsAiEnabled = false });

        (await availability.CanEmbedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NoEncoder_DoesNotEvenReadTheProfile()
    {
        // Ordering, not an optimisation detail: this runs on every search keystroke, and the
        // profile is a database read where the model check is a stat call.
        var availability = Build(encoderInstalled: false, generatorInstalled: false, aiEnabled: true);

        await availability.CanEmbedAsync();

        _profiles.Verify(p => p.GetUserProfileAsync(), Times.Never);
    }

    /// <summary>Installed but never invoked: availability reads the flag and nothing else.</summary>
    private sealed class StubAvailableEmbedder : ITextEmbedder
    {
        public bool IsAvailable => true;

        public string ModelId => "stub";

        public int Dimensions => 2;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Unload()
        {
        }
    }

    private sealed class StubAvailableGenerator : ITextGenerator
    {
        public bool IsAvailable => true;

        public string ModelId => "stub-generator";

        public IAsyncEnumerable<string> StreamAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
