namespace Diarion.Services.Store;

/// <summary>
/// Small key-value store for access state, backed by the Keychain on iOS so it survives a
/// reinstall. It never holds a transaction id or receipt (spec 15, R0.3) — only the conclusions
/// drawn from them. Synchronous on purpose: the state is restored before the first frame, the
/// same way the app lock reads its settings.
/// </summary>
public interface IEntitlementStore
{
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}
