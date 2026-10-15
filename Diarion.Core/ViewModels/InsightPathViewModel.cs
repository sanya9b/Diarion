using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Helpers;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Models.Ai;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.InsightPath;
using Diarion.Services.Store;
using Diarion.ViewModels.Statistics;

namespace Diarion.ViewModels;

/// <summary>
/// The strip under the date on the home screen: the path to the first hypothesis (spec 17, R1.5,
/// trimmed on 2026-10-15 to the strip and a count). Its own view model, like
/// <see cref="AccessBannerViewModel"/>: the path is about the diary as a whole, not about the day
/// on screen, and <c>MainViewModel</c> has no business growing a statistics concern.
/// </summary>
/// <remarks>
/// Refreshes on the three messages a paired day can arrive through (diary, tasks, cycle), on an
/// access change, and whenever the page asks. The refreshes are debounced: a save announces itself
/// once per entity, and one readiness pass per burst is plenty. The strip says nothing in words
/// until the last milestone; then it shows the first hypothesis once, for the rest of this
/// session, and asks the service to keep it off the screen from the next launch on.
/// </remarks>
public partial class InsightPathViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

    private readonly IInsightPathService _path;
    private readonly INavigationService _navigation;
    private readonly IDispatcherService _dispatcher;
    private readonly IEntitlementService _entitlements;
    private readonly AsyncDebouncer _refresh = new(RefreshDelay);

    // Set the first time the hypothesis row is shown. From then on this instance keeps the row
    // visible even though the service already reports the path as finished.
    private bool _arrivedShown;

    public InsightPathViewModel(
        IInsightPathService path,
        INavigationService navigation,
        IDispatcherService dispatcher,
        IEntitlementService entitlements)
    {
        _path = path;
        _navigation = navigation;
        _dispatcher = dispatcher;
        _entitlements = entitlements;

        WeakReferenceMessenger.Default.Register<DocumentChangedMessage>(this, (r, m) =>
        {
            // Notes are documents too, but they never pair with a mood.
            if (m.SourceKind == EmbeddingSourceKind.Diary)
                ((InsightPathViewModel)r).ScheduleRefresh();
        });
        WeakReferenceMessenger.Default.Register<TodoChangedMessage>(this, (r, _) => ((InsightPathViewModel)r).ScheduleRefresh());
        WeakReferenceMessenger.Default.Register<CycleLogChangedMessage>(this, (r, _) => ((InsightPathViewModel)r).ScheduleRefresh());
        _entitlements.Changed += OnAccessChanged;
    }

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private int _pairedDays;

    [ObservableProperty]
    private int _requiredDays = InsightPathMilestones.Last;

    /// <summary>«9 з 14».</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _hasHypothesis;

    /// <summary>The first hypothesis in the same words the statistics screen uses, so the two agree.</summary>
    [ObservableProperty]
    private string _hypothesisText = string.Empty;

    /// <summary>One sentence for screen readers (R2.5).</summary>
    [ObservableProperty]
    private string _accessibleSummary = string.Empty;

    /// <summary>Immediate refresh, for the page appearing.</summary>
    public Task RefreshAsync() => ApplyAsync();

    /// <summary>Runs a pending debounced refresh now. For tests and for anyone who cannot wait 300 ms.</summary>
    public Task FlushAsync() => _refresh.FlushAsync();

    private void ScheduleRefresh() => _refresh.Debounce(ApplyAsync);

    private void OnAccessChanged(AccessState _) => ScheduleRefresh();

    private async Task ApplyAsync()
    {
        var state = await _path.GetAsync();
        var arrivedNow = state.IsVisible && state.FirstHypothesis is not null && !_arrivedShown;
        if (arrivedNow)
        {
            // Shown once: ask for the path to stay off the screen from the next launch, while this
            // instance keeps the row for the session (see Apply).
            _arrivedShown = true;
            await _path.HideUntilNextMilestoneAsync();
        }

        _dispatcher.InvokeOnMainThread(() => Apply(state));
    }

    private void Apply(InsightPathState state)
    {
        RequiredDays = state.RequiredDays;
        PairedDays = Math.Min(state.PairedDays, state.RequiredDays);
        ProgressText = string.Format(AppResources.InsightPathProgressFormat, PairedDays, RequiredDays);

        HasHypothesis = state.FirstHypothesis is not null;
        HypothesisText = state.FirstHypothesis is { } h
            ? $"{(h.Coefficient >= 0 ? "↑" : "↓")}  {FactorNames.Display(h.FactorKey)} · {string.Format(AppResources.StatsCorrelationEvidenceFormat, h.SampleSize)}"
            : string.Empty;

        AccessibleSummary = string.Format(AppResources.InsightPathAccessibleFormat, PairedDays, RequiredDays);
        IsVisible = state.IsVisible || (_arrivedShown && state.FirstHypothesis is not null);
    }

    /// <summary>
    /// Opens the statistics screen on the same window the path counted (R1.6). The page first, the
    /// window second: the statistics view model registers for the message when its page is created,
    /// so a message sent before navigation could reach nobody.
    /// </summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        await _navigation.NavigateToAsync("//StatisticsPage");
        WeakReferenceMessenger.Default.Send(
            new StatisticsRangeRequestedMessage(StatsRange.LastDays(InsightPathService.WindowDays)));
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _entitlements.Changed -= OnAccessChanged;
    }
}
