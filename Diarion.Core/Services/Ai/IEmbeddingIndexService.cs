using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>
/// Keeps <c>ai_embeddings</c> in step with the diary and notes.
/// </summary>
/// <remarks>
/// One task and one cancellation token — not a job framework. The work queue is derived on every
/// pass by comparing source hashes against stored ones, so there is no cursor to persist, no state
/// to corrupt, and an interrupted run resumes simply by running again.
/// </remarks>
public interface IEmbeddingIndexService
{
    AiIndexProgress Progress { get; }

    event EventHandler<AiIndexProgress>? ProgressChanged;

    /// <summary>
    /// Starts indexing in the background if it is not already running. Returns immediately.
    /// </summary>
    void Start();

    /// <summary>
    /// Cancels indexing and waits for the current batch to finish. Must be awaited before
    /// <c>DatabaseContext.Reopen()</c> or <c>DropAllData()</c>, which invalidate the collection
    /// the loop is writing to.
    /// </summary>
    Task StopAsync();

    /// <summary>Runs one full pass and returns when the index is current. Used by tests and by the
    /// "rebuild index" button, where the caller wants to know when it is done.</summary>
    Task<AiIndexProgress> RunOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-embeds a single document after an edit, or removes its rows if it went empty.</summary>
    Task ReindexSourceAsync(string sourceKind, string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Drops the whole index. Used when AI is switched off or the model changes.</summary>
    Task ClearAsync();
}
