using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;

namespace Diarion.Services.Store;

/// <summary>
/// The view-model side of read-only mode. Every call a view model makes into a guarded service
/// method goes through <see cref="TryAsync"/>: a <see cref="ReadOnlyAccessException"/> thrown out
/// of a command would take the whole app down on iOS, so it is caught here, turned into one
/// <see cref="PaywallRequestedMessage"/> for the shell, and reported as <c>false</c> so the
/// caller stops before it shows a change that was never saved (spec 15, R5.2).
/// </summary>
public static class WriteGate
{
    public static async Task<bool> TryAsync(Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(true);
            return true;
        }
        catch (ReadOnlyAccessException)
        {
            WeakReferenceMessenger.Default.Send(new PaywallRequestedMessage(PaywallReason.WriteBlocked));
            return false;
        }
    }
}
