using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Diarion.Services.Ai;

/// <summary>
/// Runs the generative model through ONNX Runtime GenAI on the CPU.
/// </summary>
/// <remarks>
/// Thin by the same rule as the embedder: the native handles and the decode loop live here, every
/// decision about what to send and whether to trust what comes back lives in Core beside it.
/// </remarks>
public sealed class OnnxGenAiTextGenerator : ITextGenerator, IDisposable
{
    /// <summary>
    /// Sampling settings. Greedy decoding on a small model loops; a mild repetition penalty is the
    /// cheapest thing that stops it, and determinism is worth more here than variety — the same
    /// question about the same diary should not answer differently twice.
    /// </summary>
    private const float RepetitionPenalty = 1.1f;

    private readonly IGenerativeModelLocator _locator;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private Model? _model;
    private Tokenizer? _tokenizer;
    private string _modelId = string.Empty;

    public OnnxGenAiTextGenerator(IGenerativeModelLocator locator)
    {
        _locator = locator;
    }

    public string ModelId => _modelId.Length > 0 ? _modelId : _locator.TryLocate()?.ModelId ?? string.Empty;

    public bool IsAvailable
    {
        get
        {
            var located = _locator.TryLocate();
            return located is not null && File.Exists(Path.Combine(located.Directory, "genai_config.json"));
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        int maxTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var model = _model!;
        var tokenizer = _tokenizer!;

        // The chat template is not optional. Without it an instruct model behaves like a base
        // completion model and continues the prompt instead of answering it — measured, and it cost
        // a whole evaluation run before it was noticed.
        var templated = ApplyChatTemplate(tokenizer, prompt);
        using var sequences = tokenizer.Encode(templated);

        using var parameters = new GeneratorParams(model);
        parameters.SetSearchOption("max_length", sequences[0].Length + maxTokens);
        parameters.SetSearchOption("do_sample", false);
        parameters.SetSearchOption("repetition_penalty", RepetitionPenalty);

        using var generator = new Generator(model, parameters);
        generator.AppendTokenSequences(sequences);

        using var stream = tokenizer.CreateStream();

        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Decoding is synchronous and CPU-bound; yielding keeps the UI thread free between
            // tokens so the answer visibly streams instead of arriving in one lump.
            await Task.Run(generator.GenerateNextToken, cancellationToken).ConfigureAwait(false);

            var sequence = generator.GetSequence(0);
            var piece = stream.Decode(sequence[^1]);

            if (!string.IsNullOrEmpty(piece))
            {
                yield return piece;
            }
        }
    }

    private static string ApplyChatTemplate(Tokenizer tokenizer, string prompt)
    {
        var messages = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { role = "user", content = prompt },
        });

        try
        {
            return tokenizer.ApplyChatTemplate(null, messages, null, true);
        }
        catch (Exception)
        {
            // A model without a template in its config is not a reason to fail the feature; the raw
            // prompt is worse but still answers sometimes.
            return prompt;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_model is not null)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_model is not null)
            {
                return;
            }

            var located = _locator.TryLocate()
                ?? throw new InvalidOperationException("No generative model is installed. Check IsAvailable first.");

            // Loading is seconds of disk and CPU; off the UI thread.
            await Task.Run(() =>
            {
                using var config = new Config(located.Directory);
                var model = new Model(config);
                _tokenizer = new Tokenizer(model);
                _model = model;
                _modelId = located.ModelId;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>Frees the model. Called on app sleep — this is the largest allocation in the app.</summary>
    public void Unload()
    {
        _tokenizer?.Dispose();
        _tokenizer = null;
        _model?.Dispose();
        _model = null;
        _modelId = string.Empty;
    }

    public void Dispose()
    {
        Unload();
        _loadGate.Dispose();
    }
}

/// <summary>Where the generative model lives, if one is installed.</summary>
public interface IGenerativeModelLocator
{
    GenerativeModelFiles? TryLocate();
}

/// <param name="Directory">Folder holding <c>genai_config.json</c> and the graph beside it.</param>
public sealed record GenerativeModelFiles(string ModelId, string Directory);
