namespace Diarion.Services.Ai;

/// <summary>
/// The iOS side of <see cref="IModelTransferHost"/>: nothing to hold, and yet the answer to "does it
/// survive being minimized" is yes.
/// </summary>
/// <remarks>
/// Android has to ask for the process to be kept alive, and its foreground service is the asking.
/// Here there is nothing to ask for and nothing to show: the transfer belongs to a
/// system daemon (see <see cref="BackgroundSessionTransfer"/>), which carries on regardless of what
/// happens to this process, and iOS draws no progress of its own.
///
/// So this is a no-op host that is not the no-op host. The one property that differs from
/// <see cref="NullModelTransferHost"/> is the one the user reads: the model row may honestly invite
/// them to leave.
/// </remarks>
public sealed class SystemSessionTransferHost : IModelTransferHost
{
    public bool KeepsRunningInBackground => true;

    public IDisposable Begin(string modelId, string modelName) => NullModelTransferHost.Empty;

    public void Report(ModelDownloadProgress progress)
    {
        // Deliberately silent. A notification would be ours to schedule, ours to keep current while
        // suspended — which we are not — and ours to clear. The system already downloads without us;
        // it does not need us narrating.
    }
}
