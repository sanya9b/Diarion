using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Helpers;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Models.Ai;
using Diarion.Services.InsightPath;
using Diarion.Services.Store;
using Diarion.ViewModels.Statistics;

namespace Diarion.ViewModels;

/// <summary>
/// The home-screen card for the path to the first hypothesis (spec 17, R1.5). Its own view model,
/// like <see cref="AccessBannerViewModel"/>: the card is about the diary as a whole, not about the
/// day on screen, and <c>MainViewModel</c> has no business growing a statistics concern.
/// </summary>
/// <remarks>
/// Refreshes on the three messages a paired day can arrive through (diary, tasks, cycle), on an
/// access change, and whenever the page asks. The refreshes are debounced: a save announces itself
/// once per entity, and one readiness pass per burst is plenty.
/// </remarks>
public partial class InsightPathViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

    private readonly IInsightPathService _path;
    private readonly INavigationService _navigation;
    private readonly IDispatcherService _dispatcher;
    private readonly IEntitlementService _entitlements;
    private readonly AsyncDebouncer _refresh = new(RefreshDelay);

    // -1 until the first state lands, so the first load never counts as a milestone being reached.
    private int _lastMilestone = -1;

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

    /// <summary>What to record so today counts: the nearest factor, or the cheapest pair before any has paired.</summary>
    [ObservableProperty]
    private string _hintText = string.Empty;

    [ObservableProperty]
    private bool _hasMilestone;

    [ObservableProperty]
    private string _milestoneTitle = string.Empty;

    [ObservableProperty]
    private string _milestoneBody = string.Empty;

    [ObservableProperty]
    private bool _hasHypothesis;

    /// <summary>The first hypothesis in the same words the statistics screen uses, so the two agree.</summary>
    [ObservableProperty]
    private string _hypothesisText = string.Empty;

    /// <summary>One sentence for screen readers (R2.5).</summary>
    [ObservableProperty]
    private string _accessibleSummary = string.Empty;

    public int ReachedMilestone { get; private set; }

    /// <summary>
    /// A milestone was reached while the card was alive — the one moment the card celebrates (R2.4).
    /// Not raised for the state the card loads with: a milestone reached last week is not news.
    /// </summary>
    public event Action<int>? MilestoneReached;

    /// <summary>Immediate refresh, for the page appearing.</summary>
    public Task RefreshAsync() => ApplyAsync();

    /// <summary>Runs a pending debounced refresh now. For tests and for anyone who cannot wait 300 ms.</summary>
    public Task FlushAsync() => _refresh.FlushAsync();

    private void ScheduleRefresh() => _refresh.Debounce(ApplyAsync);

    private void OnAccessChanged(AccessState _) => ScheduleRefresh();

    private async Task ApplyAsync()
    {
        var state = await _path.GetAsync();
        _dispatcher.InvokeOnMainThread(() => Apply(state));
    }

    private void Apply(InsightPathState state)
    {
        RequiredDays = state.RequiredDays;
        PairedDays = Math.Min(state.PairedDays, state.RequiredDays);
        ProgressText = string.Format(AppResources.InsightPathProgressFormat, PairedDays, RequiredDays);
        HintText = state.BestFactorKey is null
            ? AppResources.InsightPathHintNoFactor
            : string.Format(AppResources.InsightPathHintFormat, FactorNames.DisplayLower(state.BestFactorKey));

        ReachedMilestone = state.ReachedMilestone;
        HasMilestone = state.ReachedMilestone > 0;
        (MilestoneTitle, MilestoneBody) = MilestoneText(state.ReachedMilestone);

        HasHypothesis = state.FirstHypothesis is not null;
        HypothesisText = state.FirstHypothesis is { } h
            ? $"{(h.Coefficient >= 0 ? "↑" : "↓")}  {FactorNames.Display(h.FactorKey)} · {string.Format(AppResources.StatsCorrelationEvidenceFormat, h.SampleSize)}"
            : string.Empty;

        AccessibleSummary = string.Format(AppResources.InsightPathAccessibleFormat, PairedDays, RequiredDays, HintText);
        IsVisible = state.IsVisible;

        if (IsVisible && _lastMilestone >= 0 && state.ReachedMilestone > _lastMilestone)
            MilestoneReached?.Invoke(state.ReachedMilestone);
        _lastMilestone = state.ReachedMilestone;
    }

    private static (string Title, string Body) MilestoneText(int milestone) => milestone switch
    {
        1 => (AppResources.InsightPathMilestone1Title, AppResources.InsightPathMilestone1Body),
        3 => (AppResources.InsightPathMilestone3Title, AppResources.InsightPathMilestone3Body),
        7 => (AppResources.InsightPathMilestone7Title, AppResources.InsightPathMilestone7Body),
        14 => (AppResources.InsightPathMilestone14Title, AppResources.InsightPathMilestone14Body),
        _ => (string.Empty, string.Empty)
    };

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

    [RelayCommand]
    private async Task HideAsync()
    {
        await _path.HideUntilNextMilestoneAsync();
        await ApplyAsync();
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _entitlements.Changed -= OnAccessChanged;
    }
}
