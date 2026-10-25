namespace Diarion.Services.Store;

/// <summary>
/// The one call every data-changing service method makes first. Read paths, backup, export and
/// import never call it (spec 15, R0.1, R1.4).
/// </summary>
public interface IAccessGuard
{
    bool CanWrite { get; }

    /// <exception cref="ReadOnlyAccessException">The subscription has lapsed.</exception>
    void EnsureWritable();
}

public sealed class AccessGuard : IAccessGuard
{
    /// <summary>For services built without a subscription in play — tests and tools. DI always
    /// supplies the real guard; a service constructed by hand gets this one.</summary>
    public static IAccessGuard Unrestricted { get; } = new UnrestrictedAccessGuard();

    private readonly IEntitlementService _entitlements;

    public AccessGuard(IEntitlementService entitlements)
    {
        _entitlements = entitlements;
    }

    public bool CanWrite => _entitlements.Current.CanWrite;

    public void EnsureWritable()
    {
        if (!_entitlements.Current.CanWrite)
            throw new ReadOnlyAccessException();
    }

    private sealed class UnrestrictedAccessGuard : IAccessGuard
    {
        public bool CanWrite => true;

        public void EnsureWritable()
        {
        }
    }
}

/// <summary>
/// Raised by a write that reached a service while the diary is read-only. The UI's first line of
/// defence is <c>CanExecute</c>; this is the second, and the shell turns it into the paywall.
/// </summary>
public sealed class ReadOnlyAccessException : InvalidOperationException
{
    public ReadOnlyAccessException()
        : base("The subscription has lapsed; the diary is read-only until it is renewed.")
    {
    }
}
