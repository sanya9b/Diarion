using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Helpers;

public class AsyncDebouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public AsyncDebouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public void Debounce(Func<Task> action)
    {
        CancellationToken token;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay, token);
                if (!token.IsCancellationRequested)
                {
                    await action();
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    public async Task FlushAsync(Func<Task> action)
    {
        bool wasPending = false;
        lock (_lock)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                wasPending = true;
            }
        }

        if (wasPending)
        {
            await action();
        }
    }
}
