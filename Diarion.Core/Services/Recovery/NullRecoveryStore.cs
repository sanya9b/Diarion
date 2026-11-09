using System;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Recovery;

/// <summary>Platforms without a cloud container yet (Android, Windows, Mac Catalyst): the feature is hidden.</summary>
public sealed class NullRecoveryStore : IRecoveryStore
{
    public bool IsSupported => false;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<RecoveryCopyInfo?> FindAsync(TimeSpan searchTimeout, CancellationToken cancellationToken = default)
        => Task.FromResult<RecoveryCopyInfo?>(null);

    public Task<string?> DownloadAsync(TimeSpan timeout, IProgress<double>? progress, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task WriteAsync(string sourcePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No cloud container on this platform.");

    public Task DeleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
