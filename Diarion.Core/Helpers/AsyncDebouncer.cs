using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Helpers;

/// <summary>
/// Debounces async actions: repeated <see cref="Debounce"/> calls within the delay window
/// coalesce into a single execution of the LAST scheduled action. <see cref="FlushAsync"/>
/// runs any still-pending action immediately.
/// <para>
/// The pending action is stored, so a flush executes exactly the action that was scheduled
/// (not a replacement passed by the caller). A scheduled fire and a flush never run
/// concurrently, and any pending action executes at most once.
/// </para>
/// </summary>
public class AsyncDebouncer
{
    private readonly TimeSpan _delay;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Func<Task>? _pendingAction;

    public AsyncDebouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public void Debounce(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CancellationToken token;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _pendingAction = action;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!token.IsCancellationRequested)
            {
                await RunIfClaimedAsync(action);
            }
        }, token);
    }

    /// <summary>
    /// Immediately runs the pending action (if any) and cancels the scheduled fire, so the
    /// action is not executed twice. Does nothing if nothing is pending.
    /// </summary>
    public async Task FlushAsync()
    {
        Func<Task>? action;
        lock (_lock)
        {
            _cts?.Cancel();
            action = _pendingAction;
        }

        await RunIfClaimedAsync(action);
    }

    // Guarantees a pending action runs at most once and never concurrently with another run
    // (whether triggered by the scheduled fire or by a flush).
    private async Task RunIfClaimedAsync(Func<Task>? action)
    {
        if (action == null)
        {
            return;
        }

        await _runGate.WaitAsync();
        try
        {
            lock (_lock)
            {
                // Another caller (flush vs. the scheduled fire) already claimed and ran this action.
                if (!ReferenceEquals(_pendingAction, action))
                {
                    return;
                }
                _pendingAction = null;
            }

            await action();
        }
        finally
        {
            _runGate.Release();
        }
    }
}
