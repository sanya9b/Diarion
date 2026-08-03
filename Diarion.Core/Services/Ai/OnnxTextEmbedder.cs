using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Diarion.Services.Ai;

/// <summary>
/// Runs the multilingual sentence encoder through ONNX Runtime on the CPU.
/// </summary>
/// <remarks>
/// Deliberately thin. Tokenization arithmetic, pooling, normalization and every ranking decision
/// live in Core where tests can reach them; what remains here is the native handle, the tensor
/// plumbing, and lazy loading. NNAPI is not used — it is deprecated as of Android 15 and Google
/// expects most devices to fall back to CPU anyway.
/// </remarks>
public sealed class OnnxTextEmbedder : ITextEmbedder, IDisposable
{
    private readonly IEmbeddingModelLocator _locator;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private InferenceSession? _session;
    private SentencePieceTokenizer? _tokenizer;
    private EmbeddingModelFiles? _files;
    private string[] _expectedInputs = [];

    public OnnxTextEmbedder(IEmbeddingModelLocator locator)
    {
        _locator = locator;
    }

    public string ModelId => _files?.ModelId ?? _locator.TryLocate()?.ModelId ?? string.Empty;

    public int Dimensions => _files?.Dimensions ?? _locator.TryLocate()?.Dimensions ?? 0;

    public bool IsAvailable
    {
        get
        {
            var files = _locator.TryLocate();
            return files is not null && File.Exists(files.OnnxPath) && File.Exists(files.TokenizerPath);
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await EmbedBatchAsync(new[] { text }, cancellationToken).ConfigureAwait(false);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var session = _session!;
        var tokenizer = _tokenizer!;
        var files = _files!;

        return await Task.Run(() =>
        {
            var results = new List<float[]>(texts.Count);

            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The tokenizer must not add its own markers: it would add SentencePiece's, and the
                // model expects fairseq's. XlmrIdMap owns that translation.
                var pieceIds = tokenizer.EncodeToIds(text ?? string.Empty, addBeginningOfSentence: false, addEndOfSentence: false);
                var (inputIds, attentionMask) = XlmrIdMap.BuildInput(pieceIds, files.MaxTokens);

                using var outputs = session.Run(BuildInputs(inputIds, attentionMask));

                var hidden = outputs.First().AsTensor<float>();
                var dimensions = hidden.Dimensions[^1];

                var pooled = EmbeddingMath.MeanPool(hidden.ToArray(), attentionMask, dimensions);
                EmbeddingMath.NormalizeInPlace(pooled);
                results.Add(pooled);
            }

            return (IReadOnlyList<float[]>)results;
        }, cancellationToken).ConfigureAwait(false);
    }

    private List<NamedOnnxValue> BuildInputs(int[] inputIds, int[] attentionMask)
    {
        var length = inputIds.Length;
        var inputs = new List<NamedOnnxValue>(_expectedInputs.Length);

        foreach (var name in _expectedInputs)
        {
            var values = name switch
            {
                "input_ids" => inputIds.Select(static v => (long)v).ToArray(),
                "attention_mask" => attentionMask.Select(static v => (long)v).ToArray(),
                // Single-segment input, so every token belongs to segment zero. Some XLM-R exports
                // declare this and some do not, which is why inputs are driven by the graph.
                "token_type_ids" => new long[length],
                _ => null,
            };

            if (values is not null)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, [1, length])));
            }
        }

        return inputs;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null)
            {
                return;
            }

            var files = _locator.TryLocate()
                ?? throw new InvalidOperationException("No embedding model is installed. Check IsAvailable before embedding.");

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                // One thread per big core at most: indexing runs while the user is using the app,
                // and starving the UI thread to embed faster is a bad trade.
                IntraOpNumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)),
            };

            var session = new InferenceSession(files.OnnxPath, options);

            await using var tokenizerStream = File.OpenRead(files.TokenizerPath);
            var tokenizer = SentencePieceTokenizer.Create(
                tokenizerStream,
                addBeginningOfSentence: false,
                addEndOfSentence: false);

            _expectedInputs = session.InputMetadata.Keys.ToArray();
            _tokenizer = tokenizer;
            _files = files;
            _session = session;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>
    /// Drops the native session. Called when the app sleeps: the graph is tens of megabytes of
    /// resident memory that nothing needs while the user is elsewhere.
    /// </summary>
    public void Unload()
    {
        _session?.Dispose();
        _session = null;
        _tokenizer = null;
        _files = null;
        _expectedInputs = [];
    }

    public void Dispose()
    {
        Unload();
        _loadGate.Dispose();
    }
}
