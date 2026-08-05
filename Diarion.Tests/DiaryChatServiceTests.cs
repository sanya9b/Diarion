using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class DiaryChatServiceTests : IDisposable
{
    private const string Model = "stub";

    private readonly DatabaseContext _dbContext;
    private readonly LiteDbVectorStore _store;
    private readonly StubEmbedder _embedder = new();
    private readonly StubGenerator _generator = new();
    private readonly FakeAiAvailability _availability = new();
    private readonly DiaryChatService _service;

    public DiaryChatServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _store = new LiteDbVectorStore(_dbContext);
        _service = new DiaryChatService(_store, _embedder, _generator, _availability);
    }

    public void Dispose() => _dbContext.Dispose();

    private int _ordinal;

    private void Indexed(string text, float[] vector, int day = 3)
    {
        EmbeddingMath.NormalizeInPlace(vector);
        var id = $"entry-{_ordinal}";
        _store.UpsertBatch([new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(EmbeddingSourceKind.Diary, id, _ordinal++),
            SourceKind = EmbeddingSourceKind.Diary,
            SourceId = id,
            SourceDate = new DateTime(2026, 6, day),
            Text = text,
            ModelId = Model,
            Dim = vector.Length,
            Vector = EmbeddingMath.ToBytes(vector),
        }]);
    }

    private async Task<ChatResult> AskAsync(string question)
    {
        ChatResult? result = null;
        await foreach (var delta in _service.AskAsync(question))
        {
            if (delta.IsComplete)
            {
                result = delta.Answer;
            }
        }

        result.Should().NotBeNull("every conversation must end with a completion message");
        return result!;
    }

    [Fact]
    public async Task Ask_NoGenerativeModel_RefusesAsUnavailable()
    {
        _availability.CanGenerate = false;

        (await _service.IsAvailableAsync()).Should().BeFalse();

        ChatResult? result = null;
        await foreach (var delta in _service.AskAsync("щось"))
        {
            result = delta.Answer;
        }

        result!.Refusal.Should().Be(ChatRefusalReason.Unavailable);
    }

    [Fact]
    public async Task Ask_AiSwitchedOff_RefusesEvenThoughTheIndexIsStillThere()
    {
        // The index survives the toggle — deleting it on every switch-off would mean an hour of
        // re-embedding to switch back on. So the toggle has to be honoured on the way out, here,
        // or the diary stays readable by a feature the user turned off.
        _embedder.Map("кава", [1f, 0f]);
        Indexed("вранці пив каву на балконі, було тихо", [1f, 0f]);
        Indexed("ще про каву та ранок", [1f, 0f]);

        _availability.CanEmbed = false;

        ChatResult? result = null;
        await foreach (var delta in _service.AskAsync("кава"))
        {
            result = delta.Answer;
        }

        result!.Refusal.Should().Be(ChatRefusalReason.Unavailable);
        _generator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Ask_NothingRelevant_RefusesWithoutInvokingTheModel()
    {
        // The gate that matters most: a model that is never called cannot invent anything.
        _embedder.Map("тривога", [1f, 0f]);
        Indexed("зовсім про інше", [0f, 1f]);

        var result = await AskAsync("тривога");

        result.Refusal.Should().Be(ChatRefusalReason.NothingRelevant);
        _generator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Ask_OnlyOneRelevantPassage_StillRefuses()
    {
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава", [1f, 0f]);
        Indexed("зовсім про інше", [0f, 1f]);

        var result = await AskAsync("кава");

        result.Refusal.Should().Be(ChatRefusalReason.NothingRelevant);
        _generator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Ask_GroundedAnswer_IsReturnedWithItsSources()
    {
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава була смачна", [1f, 0f], day: 3);
        Indexed("знову пив каву зранку", [1f, 0.05f], day: 4);
        _generator.Respond("Ви пили каву вранці [1].");

        var result = await AskAsync("кава");

        result.IsRefusal.Should().BeFalse();
        result.Text.Should().Be("Ви пили каву вранці [1].");
        result.Citations.Should().ContainSingle();
        result.Citations[0].SourceDate.Should().Be(new DateTime(2026, 6, 3));
    }

    [Fact]
    public async Task Ask_ModelAnswersWithoutCiting_IsDowngradedToARefusal()
    {
        // A fluent answer with no citation came from the model's weights, not the diary. This is
        // the difference between a promise and a guarantee.
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава", [1f, 0f]);
        Indexed("знову кава", [1f, 0.05f]);
        _generator.Respond("Ви завжди починали ранок із кави, і це вас заспокоювало.");

        var result = await AskAsync("кава");

        result.Refusal.Should().Be(ChatRefusalReason.Ungrounded);
        result.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task Ask_ModelInventsASourceNumber_IsDowngraded()
    {
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава", [1f, 0f]);
        Indexed("знову кава", [1f, 0.05f]);
        _generator.Respond("Про це ви писали [9].");

        (await AskAsync("кава")).Refusal.Should().Be(ChatRefusalReason.Ungrounded);
    }

    [Fact]
    public async Task Ask_StreamsBeforeCompleting()
    {
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава", [1f, 0f]);
        Indexed("знову кава", [1f, 0.05f]);
        _generator.Respond("Так [1].");

        var deltas = new List<string>();
        await foreach (var delta in _service.AskAsync("кава"))
        {
            if (!delta.IsComplete)
            {
                deltas.Add(delta.Delta);
            }
        }

        // On a phone CPU an answer takes long enough that watching it arrive is the difference
        // between "thinking" and "frozen".
        deltas.Should().NotBeEmpty();
        string.Concat(deltas).Should().Be("Так [1].");
    }

    [Fact]
    public async Task Ask_PromptCarriesTheRetrievedPassages()
    {
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава була смачна", [1f, 0f], day: 7);
        Indexed("знову пив каву зранку", [1f, 0.05f], day: 8);
        _generator.Respond("Так [1].");

        await AskAsync("кава");

        _generator.LastPrompt.Should().Contain("ранкова кава була смачна");
        _generator.LastPrompt.Should().Contain("ПИТАННЯ: кава");

        // The reasoning switch has to reach the model, or the whole answer budget goes to a
        // monologue. It travels in the message because ORT-GenAI 0.14.1 cannot set the template flag.
        _generator.LastPrompt.Should().Contain("/no_think");
    }

    [Fact]
    public async Task Ask_AReasoningModelsMonologue_ReachesNeitherTheScreenNorTheCitations()
    {
        // Seen in the running app: the model deliberated in English over markers it did not commit
        // to, and all of it was shown and cited. The wiring is what these two assertions cover —
        // ReasoningFilterTests covers the filter itself.
        _embedder.Map("кава", [1f, 0f]);
        Indexed("ранкова кава була смачна", [1f, 0f], day: 7);
        Indexed("знову пив каву зранку", [1f, 0.05f], day: 8);
        _generator.Respond("<think> Records [1] and [2] both mention it, but [2] is closer. </think> Так, ви пили ранкову каву [1].");

        var streamed = new List<string>();
        ChatResult? result = null;
        await foreach (var delta in _service.AskAsync("кава"))
        {
            if (delta.IsComplete)
            {
                result = delta.Answer;
                continue;
            }

            streamed.Add(delta.Delta);
        }

        string.Concat(streamed).Should().NotContain("Records").And.NotContain("<think>");
        result!.Text.Should().Be("Так, ви пили ранкову каву [1].");
        result.Citations.Select(c => c.Marker).Should().Equal(1);
    }

    private sealed class StubEmbedder : ITextEmbedder
    {
        private readonly Dictionary<string, float[]> _vectors = new();

        public string ModelId => Model;

        public int Dimensions => 2;

        public bool IsAvailable => true;

        public void Map(string text, float[] vector)
        {
            EmbeddingMath.NormalizeInPlace(vector);
            _vectors[text] = vector;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(_vectors.TryGetValue(text, out var v) ? v : [0f, 1f]);

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0f, 1f }).ToList());
    }

    private sealed class StubGenerator : ITextGenerator
    {
        private string _response = string.Empty;

        public string ModelId => "stub-gen";

        public bool IsAvailable => true;

        public int CallCount { get; private set; }

        public string LastPrompt { get; private set; } = string.Empty;

        public void Respond(string text) => _response = text;

        public async IAsyncEnumerable<string> StreamAsync(
            string prompt,
            int maxTokens,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;

            // Word by word, the way a real decoder arrives.
            var parts = _response.Split(' ');
            for (var i = 0; i < parts.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i == 0 ? parts[i] : " " + parts[i];
            }
        }
    }
}
