using System;
using System.Globalization;
using System.Threading.Tasks;
using Diarion.Services.Store;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Spec 15, R2: who installed before the paywall. The keys are spelled out because what is
/// persisted — and what is not — is the behaviour under test.
/// </summary>
public class LegacyAccessDetectorTests
{
    private const string LegacyFreeKey = "access.legacyFree";
    private const string LegacyUntilKey = "access.legacyUntil";
    private static readonly DateTime Now = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryEntitlementStore _store = new();
    private readonly FakeStoreClient _client = new();
    private readonly FakeInstallEvidence _evidence = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private LegacyAccessDetector Create() => new(_store, _client, _evidence, _clock);

    private static AppTransactionInfo Transaction(string originalAppVersion, StoreEnvironment environment) =>
        new(originalAppVersion, environment, null);

    [Fact]
    public async Task A_persisted_answer_is_final_and_the_store_is_not_asked()
    {
        _store.Set(LegacyFreeKey, "1");
        var detector = Create();

        detector.IsKnownLegacy.Should().BeTrue();
        (await detector.EvaluateAsync()).Should().BeTrue();
        _client.AppTransactionCalls.Should().Be(0);
    }

    [Fact]
    public async Task Completed_onboarding_on_the_first_paid_launch_is_legacy_without_the_store()
    {
        _evidence.IsFirstLaunchOfPaidEra = true;
        _evidence.OnboardingCompletedBeforeThisLaunch = true;

        (await Create().EvaluateAsync()).Should().BeTrue();

        _store.Values.Should().Contain(LegacyFreeKey, "1");
        _client.AppTransactionCalls.Should().Be(0);
    }

    [Fact]
    public void Recognition_starts_the_free_months_from_that_very_launch()
    {
        _evidence.IsFirstLaunchOfPaidEra = true;
        _evidence.OnboardingCompletedBeforeThisLaunch = true;
        var detector = Create();

        detector.ResolveLocally().Should().BeTrue();

        detector.LegacyUntil.Should().Be(Now.AddDays(StoreCatalog.LegacyFreeDays));
        DateTime.Parse(_store.Values[LegacyUntilKey], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .Should().Be(Now.AddDays(StoreCatalog.LegacyFreeDays));
    }

    [Fact]
    public void The_date_is_fixed_once_and_does_not_move_with_the_clock()
    {
        _evidence.IsFirstLaunchOfPaidEra = true;
        _evidence.OnboardingCompletedBeforeThisLaunch = true;
        var detector = Create();
        detector.ResolveLocally();
        var first = detector.LegacyUntil;

        _clock.Advance(TimeSpan.FromDays(10));

        Create().LegacyUntil.Should().Be(first);
    }

    [Fact]
    public void A_user_recognised_by_a_build_that_promised_for_ever_gets_their_months_from_now()
    {
        // TestFlight builds 3–20 wrote only the flag; the first build that counts months starts them.
        _store.Set(LegacyFreeKey, "1");

        Create().LegacyUntil.Should().Be(Now.AddDays(StoreCatalog.LegacyFreeDays));
        _store.Values.Should().ContainKey(LegacyUntilKey);
    }

    [Fact]
    public void Nobody_else_has_a_date()
    {
        Create().LegacyUntil.Should().BeNull();
        _store.Values.Should().NotContainKey(LegacyUntilKey);
    }

    [Fact]
    public void The_local_rule_resolves_synchronously_before_the_first_frame()
    {
        _evidence.IsFirstLaunchOfPaidEra = true;
        _evidence.OnboardingCompletedBeforeThisLaunch = true;
        var detector = Create();

        detector.ResolveLocally().Should().BeTrue();

        detector.IsKnownLegacy.Should().BeTrue();
        _client.AppTransactionCalls.Should().Be(0);
    }

    [Fact]
    public void Resolving_locally_never_asks_the_store_even_when_it_cannot_decide()
    {
        _client.AppTransaction = Transaction("2", StoreEnvironment.Production);

        Create().ResolveLocally().Should().BeFalse();
        _client.AppTransactionCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_new_users_own_onboarding_does_not_count_on_their_second_launch()
    {
        _evidence.IsFirstLaunchOfPaidEra = false;
        _evidence.OnboardingCompletedBeforeThisLaunch = true;
        _client.AppTransaction = Transaction("3", StoreEnvironment.Production);

        (await Create().EvaluateAsync()).Should().BeFalse();
        _store.Values.Should().NotContainKey(LegacyFreeKey);
    }

    [Fact]
    public async Task A_fresh_paid_install_has_no_onboarding_to_show_and_is_not_legacy()
    {
        _evidence.IsFirstLaunchOfPaidEra = true;
        _evidence.OnboardingCompletedBeforeThisLaunch = false;
        _client.AppTransaction = Transaction("3", StoreEnvironment.Production);

        (await Create().EvaluateAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("2", StoreEnvironment.Production, true)]
    [InlineData("1", StoreEnvironment.Production, true)]
    [InlineData("3", StoreEnvironment.Production, false)]
    [InlineData("2", StoreEnvironment.Sandbox, false)]
    [InlineData("1.0", StoreEnvironment.Production, false)]
    [InlineData("1.0", StoreEnvironment.Sandbox, false)]
    [InlineData("", StoreEnvironment.Production, false)]
    public async Task The_store_rule_is_the_build_number_in_production_only(string originalAppVersion, StoreEnvironment environment, bool expected)
    {
        _client.AppTransaction = Transaction(originalAppVersion, environment);

        (await Create().EvaluateAsync()).Should().Be(expected);

        if (expected)
            _store.Values.Should().Contain(LegacyFreeKey, "1");
        else
            _store.Values.Should().NotContainKey(LegacyFreeKey);
    }

    [Fact]
    public async Task A_store_failure_is_asked_again_next_time()
    {
        _client.Failure = new StoreException("not signed in");
        var detector = Create();

        (await detector.EvaluateAsync()).Should().BeFalse();

        _client.Failure = null;
        _client.AppTransaction = Transaction("2", StoreEnvironment.Production);

        (await detector.EvaluateAsync()).Should().BeTrue();
        _client.AppTransactionCalls.Should().Be(2);
    }

    [Fact]
    public async Task A_missing_receipt_is_not_an_answer_either()
    {
        _client.AppTransaction = null;
        var detector = Create();

        (await detector.EvaluateAsync()).Should().BeFalse();
        (await detector.EvaluateAsync()).Should().BeFalse();

        _client.AppTransactionCalls.Should().Be(2);
    }

    [Fact]
    public async Task A_negative_answer_from_the_store_is_remembered()
    {
        _client.AppTransaction = Transaction("3", StoreEnvironment.Production);
        var detector = Create();

        (await detector.EvaluateAsync()).Should().BeFalse();
        (await detector.EvaluateAsync()).Should().BeFalse();

        _client.AppTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_positive_answer_is_remembered_across_detectors()
    {
        _client.AppTransaction = Transaction("2", StoreEnvironment.Production);
        await Create().EvaluateAsync();

        var later = Create();

        later.IsKnownLegacy.Should().BeTrue();
        (await later.EvaluateAsync()).Should().BeTrue();
        _client.AppTransactionCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_platform_without_a_store_is_not_asked()
    {
        _client.IsAvailable = false;

        (await Create().EvaluateAsync()).Should().BeFalse();
        _client.AppTransactionCalls.Should().Be(0);
    }
}
