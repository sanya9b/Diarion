using System.Globalization;

namespace Diarion.Services.Store;

public interface ILegacyAccessDetector
{
    /// <summary>Already decided and persisted; safe to read before the first frame.</summary>
    bool IsKnownLegacy { get; }

    /// <summary>
    /// The offline rule alone, synchronously: a 1.0 user who had finished onboarding is recognised
    /// before the first frame of the first paid launch, not a moment later when the store answers
    /// (which would flash the read-only banner at exactly the people promised it never applies).
    /// Persists a positive answer, like <see cref="EvaluateAsync"/>.
    /// </summary>
    bool ResolveLocally();

    /// <summary>
    /// Runs both rules from spec 15, R2.1. Idempotent: a positive answer is persisted and never
    /// re-derived; a negative answer from the store is persisted too; a missing answer (no
    /// network, not signed in) is not, so the next call asks again.
    /// </summary>
    Task<bool> EvaluateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides whether this user installed Diarion before it had a paywall. Two independent rules,
/// either one is enough; see the interface for how answers are remembered.
/// </summary>
public sealed class LegacyAccessDetector : ILegacyAccessDetector
{
    internal const string LegacyFreeKey = "access.legacyFree";
    internal const string StoreRuleResolvedKey = "access.legacyStoreResolved";
    private const string True = "1";

    private readonly IEntitlementStore _store;
    private readonly IStoreClient _storeClient;
    private readonly IInstallEvidence _evidence;

    public LegacyAccessDetector(IEntitlementStore store, IStoreClient storeClient, IInstallEvidence evidence)
    {
        _store = store;
        _storeClient = storeClient;
        _evidence = evidence;
    }

    public bool IsKnownLegacy => _store.Get(LegacyFreeKey) == True;

    public bool ResolveLocally()
    {
        if (IsKnownLegacy) return true;

        // Onboarding was finished by a build that had no paywall. Evaluated only on the first paid
        // launch, otherwise a new user's own onboarding would count on their second launch.
        if (_evidence.IsFirstLaunchOfPaidEra && _evidence.OnboardingCompletedBeforeThisLaunch)
        {
            _store.Set(LegacyFreeKey, True);
            return true;
        }

        return false;
    }

    public async Task<bool> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (ResolveLocally()) return true;

        if (_store.Get(StoreRuleResolvedKey) == True) return false;
        if (!_storeClient.IsAvailable) return false;

        AppTransactionInfo? transaction;
        try
        {
            transaction = await _storeClient.GetAppTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StoreException)
        {
            return false; // No answer is not "no": ask again next time (AC5).
        }

        if (transaction is null) return false;

        if (IsLegacyInstall(transaction))
        {
            _store.Set(LegacyFreeKey, True);
            return true;
        }

        _store.Set(StoreRuleResolvedKey, True);
        return false;
    }

    /// <summary>
    /// Production only: the sandbox always reports "1.0", which is not even an integer, and
    /// TestFlight would otherwise make every tester a legacy user (spec 15, R2.2).
    /// </summary>
    internal static bool IsLegacyInstall(AppTransactionInfo transaction) =>
        transaction.Environment == StoreEnvironment.Production
        && int.TryParse(transaction.OriginalAppVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var build)
        && build <= StoreCatalog.LastFreeBuild;
}
