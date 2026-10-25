using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Icons;
using Diarion.Resources.Localization;

namespace Diarion.ViewModels;

public enum TimeRangeOption
{
    Week = 7,
    TwoWeeks = 14,
    Month = 30,
    ThreeMonths = 90,
    SixMonths = 180,
    Year = 365
}

public partial class TimeRangeItem : ObservableObject
{
    public TimeRangeOption Option { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public enum StatisticsTabOption
{
    General,
    Sleep,
    Productivity,
    Finance,
    Habits,
    Cycle,
    /// <summary>The period against the one before it. First in the strip, last in the enum: nothing orders by this.</summary>
    Review
}

public partial class StatisticsTabItem : ObservableObject
{
    public StatisticsTabOption Option { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Line icon drawn before the tab label in the segmented selector (see <see cref="AppIcons"/>).</summary>
    public Microsoft.Maui.Controls.Shapes.Geometry? IconGeometry { get; set; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class StatisticsViewModel : BaseViewModel
{
    private readonly Diarion.Services.IStatisticsService _statisticsService;
    private readonly Diarion.Services.IDiaryService _diaryService;
    private readonly Diarion.Services.IFinanceService _financeService;
    private readonly Diarion.Services.INavigationService _navigationService;
    private readonly Diarion.Services.IDispatcherService _dispatcher;

    [ObservableProperty]
    private int _currentStreak;

    public bool IsStreakVisible => CurrentStreak > 0;

    /// <summary>The season's sign in the pill, as on the home tile; read when the streak is counted.</summary>
    [ObservableProperty]
    private Season _streakSeason = SeasonCalendar.Of(DateTime.Today);

    public Microsoft.Maui.Controls.Shapes.Geometry StreakIcon => AppIcons.ForSeason(StreakSeason);

    partial void OnStreakSeasonChanged(Season value) => OnPropertyChanged(nameof(StreakIcon));

    /// <summary>
    /// The run only survives because a missed day was forgiven. Worth saying: an unmarked number implies
    /// an unbroken run, and the quota is finite — the next miss ends it.
    /// </summary>
    [ObservableProperty]
    private bool _isStreakHeldByGrace;

    /// <summary>
    /// The preset chips. They are shortcuts, not modes: tapping one writes its window into
    /// <see cref="RangeStart"/> / <see cref="RangeEnd"/>, and those two dates are what the screen reads.
    /// </summary>
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<TimeRangeItem> _timeRanges = new();

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<StatisticsTabItem> _tabs = new();

    [ObservableProperty]
    private StatisticsTabItem? _selectedTab;

    [ObservableProperty]
    private bool _isGeneralTabVisible;

    [ObservableProperty]
    private bool _isSleepTabVisible;

    [ObservableProperty]
    private bool _isProductivityTabVisible;

    [ObservableProperty]
    private bool _isFinanceTabVisible;

    [ObservableProperty]
    private bool _isHabitsTabVisible;

    [ObservableProperty]
    private bool _isCycleTabVisible;

    [ObservableProperty]
    private bool _isReviewTabVisible;

    // --- The period, as two dates ---

    /// <summary>
    /// First day of the window, inclusive. Bound two-way to a date picker that sits ahead of the preset
    /// chips: picking a date is not a separate "custom mode", it is simply the general case that the
    /// chips are shorthand for.
    /// </summary>
    [ObservableProperty]
    private DateTime _rangeStart;

    /// <summary>Last day of the window, inclusive.</summary>
    [ObservableProperty]
    private DateTime _rangeEnd;

    /// <summary>Tomorrow has no data, so the pickers refuse to go there.</summary>
    public DateTime MaxSelectableDate { get; } = DateTime.Today;

    public DateTime MinSelectableDate { get; } = DateTime.Today.AddYears(-10);

    /// <summary>What every tab on this screen is asked for.</summary>
    public StatsRange CurrentRange => new StatsRange(RangeStart, RangeEnd).Normalized();

    /// <summary>
    /// True when the two dates do not spell out any preset — the state the date chip highlights instead.
    /// The screen opens in it, because month-to-date is not one of the offered chips.
    /// </summary>
    [ObservableProperty]
    private bool _isCustomRangeSelected;

    /// <summary>
    /// A preset writes both dates, and each write would otherwise start its own reload — with the
    /// half-applied window in between. Set while the pair is being replaced as a unit.
    /// </summary>
    private bool _isApplyingPreset;

    /// <summary>
    /// A date picker fires on every change, and the finance query reads twice the window, so unguarded
    /// reloads would overlap and leave whichever was slowest on screen. Same reasoning as the account
    /// chips below, and deliberately the same delay.
    /// </summary>
    private readonly Diarion.Helpers.AsyncDebouncer _rangeDebouncer = new(TimeSpan.FromMilliseconds(150));

    /// <summary>
    /// False until the constructor is done. Selecting a tab loads it, and the constructor selects the
    /// first one — into a view model the page has not bound yet, a moment before OnAppearing asks for
    /// the same load again. Two runs of the heaviest screen in the app, overlapping, writing the same
    /// collections: the first one's results land in a page that is already showing the second one's.
    /// </summary>
    private bool _isInitialized;

    public ViewModels.Statistics.MoodStatsViewModel MoodStats { get; }

    /// <summary>The days of the window on which something was written. The activity card's squares.</summary>
    [ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<DateTime> _activityDates = Array.Empty<DateTime>();

    /// <summary>«18 з 31» — written days out of the window's days.</summary>
    [ObservableProperty]
    private string _activityCountText = string.Empty;

    /// <summary>The card shows once the window has been read, written days or not.</summary>
    [ObservableProperty]
    private bool _hasActivityDays;
    public ViewModels.Statistics.SleepStatsViewModel SleepStats { get; }
    public ViewModels.Statistics.ProductivityStatsViewModel ProductivityStats { get; }
    public ViewModels.Statistics.FinanceStatsViewModel FinanceStats { get; }
    public ViewModels.Statistics.HabitStatsViewModel HabitStats { get; }
    public ViewModels.Statistics.CycleStatsViewModel CycleStats { get; }

    /// <summary>
    /// The window against the window before it — the tab the screen opens on. What a person wants
    /// first from a statistics screen is "what changed", and every other tab answers "what is".
    /// </summary>
    public ViewModels.Statistics.PeriodReviewViewModel Review { get; }

    /// <summary>The first-visit coach marks over this screen. Idle until the page starts it.</summary>
    public TourViewModel Tour { get; }

    public StatisticsViewModel(
        Diarion.Services.IStatisticsService statisticsService,
        Diarion.Services.IDiaryService diaryService,
        Diarion.Services.IFinanceService financeService,
        Diarion.Services.INavigationService navigationService,
        Diarion.Services.IDispatcherService dispatcher,
        ViewModels.Statistics.MoodStatsViewModel moodStats,
        ViewModels.Statistics.SleepStatsViewModel sleepStats,
        ViewModels.Statistics.ProductivityStatsViewModel productivityStats,
        ViewModels.Statistics.FinanceStatsViewModel financeStats,
        ViewModels.Statistics.HabitStatsViewModel habitStats,
        ViewModels.Statistics.CycleStatsViewModel cycleStats,
        ViewModels.Statistics.PeriodReviewViewModel review,
        TourViewModel tour)
    {
        _statisticsService = statisticsService;
        _diaryService = diaryService;
        _financeService = financeService;
        _navigationService = navigationService;
        _dispatcher = dispatcher;
        // Lets another screen open this one on a given window.
        WeakReferenceMessenger.Default.Register<StatisticsRangeRequestedMessage>(
            this, (r, m) => ((StatisticsViewModel)r).ApplyRange(m.Range));
        MoodStats = moodStats;
        SleepStats = sleepStats;
        ProductivityStats = productivityStats;
        FinanceStats = financeStats;
        HabitStats = habitStats;
        CycleStats = cycleStats;
        Review = review;
        Tour = tour;

        Title = AppResources.StatisticsTitle;
        // Period first: InitializeTabs selects a tab, which starts a load, and a load with the dates
        // still at default(DateTime) would query the year 1.
        InitializeTimeRanges();
        InitializeTabs();
        // From here on a tab change means the user changed it, and that does need a load. The first
        // one is OnAppearing's to make.
        _isInitialized = true;
    }

    /// <summary>
    /// Adds or removes the cycle tab to match the profile. Called on every appearance rather than once in
    /// the constructor, because the gate turns on and off from the settings screen while the app runs.
    /// </summary>
    public async Task RefreshCycleTabAvailabilityAsync()
    {
        bool available = await CycleStats.IsAvailableAsync();
        var existing = Tabs.FirstOrDefault(t => t.Option == StatisticsTabOption.Cycle);

        if (available && existing == null)
        {
            Tabs.Add(new StatisticsTabItem
            {
                Option = StatisticsTabOption.Cycle,
                DisplayName = AppResources.TabCycle,
                IconGeometry = AppIcons.Drop
            });
        }
        else if (!available && existing != null)
        {
            Tabs.Remove(existing);
            // Standing on a tab that just disappeared would leave the page blank.
            if (existing.IsSelected) SelectTab(Tabs[0]);
        }
    }

    private void InitializeTabs()
    {
        Tabs = new System.Collections.ObjectModel.ObservableCollection<StatisticsTabItem>
        {
            new StatisticsTabItem { Option = StatisticsTabOption.Review, DisplayName = AppResources.TabReview, IconGeometry = AppIcons.Bars, IsSelected = true },
            new StatisticsTabItem { Option = StatisticsTabOption.General, DisplayName = AppResources.TabGeneral, IconGeometry = AppIcons.Smile },
            new StatisticsTabItem { Option = StatisticsTabOption.Sleep, DisplayName = AppResources.TabSleep, IconGeometry = AppIcons.Moon },
            new StatisticsTabItem { Option = StatisticsTabOption.Productivity, DisplayName = AppResources.TabProductivity, IconGeometry = AppIcons.CheckCircle },
            new StatisticsTabItem { Option = StatisticsTabOption.Finance, DisplayName = AppResources.FinanceTitle ?? "Finance", IconGeometry = AppIcons.Banknote },
            new StatisticsTabItem { Option = StatisticsTabOption.Habits, DisplayName = AppResources.TabHabits, IconGeometry = AppIcons.Sprout }
        };
        SelectTab(Tabs[0]);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void SelectTab(StatisticsTabItem item)
    {
        if (item == null) return;

        foreach (var t in Tabs)
        {
            t.IsSelected = false;
        }
        item.IsSelected = true;
        SelectedTab = item;

        IsGeneralTabVisible = item.Option == StatisticsTabOption.General;
        IsSleepTabVisible = item.Option == StatisticsTabOption.Sleep;
        IsProductivityTabVisible = item.Option == StatisticsTabOption.Productivity;
        IsFinanceTabVisible = item.Option == StatisticsTabOption.Finance;
        IsHabitsTabVisible = item.Option == StatisticsTabOption.Habits;
        IsCycleTabVisible = item.Option == StatisticsTabOption.Cycle;
        IsReviewTabVisible = item.Option == StatisticsTabOption.Review;

        // Load data for the selected tab when switched
        if (_isInitialized)
        {
            _ = LoadStatisticsAsync();
        }
    }

    private void InitializeTimeRanges()
    {
        TimeRanges = new System.Collections.ObjectModel.ObservableCollection<TimeRangeItem>
        {
            new TimeRangeItem { Option = TimeRangeOption.Week, DisplayName = AppResources.TimeRangeWeek },
            new TimeRangeItem { Option = TimeRangeOption.TwoWeeks, DisplayName = AppResources.TimeRangeTwoWeeks },
            new TimeRangeItem { Option = TimeRangeOption.Month, DisplayName = AppResources.TimeRangeMonth },
            new TimeRangeItem { Option = TimeRangeOption.ThreeMonths, DisplayName = AppResources.TimeRange3Months },
            new TimeRangeItem { Option = TimeRangeOption.SixMonths, DisplayName = AppResources.TimeRange6Months },
            new TimeRangeItem { Option = TimeRangeOption.Year, DisplayName = AppResources.TimeRangeYear }
        };

        // The screen opens on the current month so far, which is no chip — so none of them lights up,
        // and the date pair does instead.
        ApplyRange(StatsRange.MonthToDate());
    }

    /// <summary>
    /// Replaces both dates as one edit. Each write raises its own change notification, and letting them
    /// through separately would queue a reload for the half-applied window in between.
    /// </summary>
    private void ApplyRange(StatsRange range)
    {
        _isApplyingPreset = true;
        try
        {
            RangeStart = range.Start;
            RangeEnd = range.End;
        }
        finally
        {
            _isApplyingPreset = false;
        }

        SyncPresetSelection();
    }

    /// <summary>
    /// Lights the one chip whose window the two dates spell out exactly, and nothing otherwise. Derived
    /// rather than remembered, so the strip cannot disagree with the dates it sits next to.
    /// </summary>
    private void SyncPresetSelection()
    {
        var range = CurrentRange;
        var matched = false;

        foreach (var r in TimeRanges)
        {
            r.IsSelected = !matched && range == StatsRange.LastDays((int)r.Option);
            matched |= r.IsSelected;
        }

        IsCustomRangeSelected = !matched;
    }

    /// <summary>
    /// A debounced reload resumes on a thread-pool thread, and every tab's load replaces bound
    /// collections. WinUI drops those writes without raising anything, so the screen keeps the previous
    /// window's charts while the KPI text next to them updates — the two disagree and nothing says why.
    /// Hand the load back to the UI thread, and keep awaiting it so the debouncer's "at most one run"
    /// still means something.
    /// </summary>
    private Task ReloadOnMainThreadAsync()
    {
        var completion = new TaskCompletionSource();

        _dispatcher.InvokeOnMainThread(async () =>
        {
            try
            {
                await LoadStatisticsAsync();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    /// <summary>Runs a date edit that is still waiting out its delay, instead of waiting for it.</summary>
    public Task FlushPendingReloadAsync() => _rangeDebouncer.FlushAsync();

    /// <summary>Reloads once the dates stop moving, unless a preset is mid-write.</summary>
    private void OnRangeEdited()
    {
        if (_isApplyingPreset) return;

        SyncPresetSelection();
        _rangeDebouncer.Debounce(ReloadOnMainThreadAsync);
    }

    // The two ends push each other rather than raising an error: there is nowhere in a single-line strip
    // to put one, and an inverted window would reach the services as "you logged nothing at all".
    partial void OnRangeStartChanged(DateTime value)
    {
        if (value.Date > RangeEnd.Date) RangeEnd = value.Date;
        OnRangeEdited();
    }

    partial void OnRangeEndChanged(DateTime value)
    {
        if (value.Date < RangeStart.Date) RangeStart = value.Date;
        OnRangeEdited();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task SelectTimeRangeAsync(TimeRangeItem item)
    {
        if (item == null || item.IsSelected) return;

        ApplyRange(StatsRange.LastDays((int)item.Option));
        await LoadStatisticsAsync();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task LoadStatisticsAsync()
    {
        IsBusy = true;
        
        try
        {
            var streak = await _diaryService.GetCurrentStreakAsync();
            CurrentStreak = streak.Length;
            IsStreakHeldByGrace = streak.HeldByGrace;
            StreakSeason = SeasonCalendar.Of(DateTime.Today);
            OnPropertyChanged(nameof(IsStreakVisible));

            var range = CurrentRange;

            if (IsReviewTabVisible)
            {
                await Review.LoadDataAsync(range);
            }
            else if (IsGeneralTabVisible)
            {
                await LoadActivityAsync(range);
                await MoodStats.LoadDataAsync(range);
            }
            else if (IsSleepTabVisible)
            {
                await SleepStats.LoadDataAsync(range);
            }
            else if (IsProductivityTabVisible)
            {
                await ProductivityStats.LoadDataAsync(range);
            }
            else if (IsFinanceTabVisible)
            {
                await LoadFinanceAccountsAsync();
                await FinanceStats.LoadDataAsync(range, SelectedStatsAccountId);
            }
            else if (IsHabitsTabVisible)
            {
                await HabitStats.LoadDataAsync(range);
            }
            else if (IsCycleTabVisible)
            {
                await CycleStats.LoadDataAsync(range);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Account scope for the finance tab ---

    /// <summary>Null means every account, matching the finance page's own strip.</summary>
    [ObservableProperty]
    private Guid? _selectedStatsAccountId;

    [ObservableProperty]
    private bool _isAllStatsAccountsSelected = true;

    public System.Collections.ObjectModel.ObservableCollection<AccountItemViewModel> StatsAccounts { get; } = new();

    public bool HasStatsAccounts => StatsAccounts.Count > 1;

    /// <summary>
    /// Tapping through account chips must not stack reloads. SelectTab already fires a load without
    /// awaiting it, and the finance query now reads twice the window, so overlapping runs would finish
    /// out of order and leave whichever happened to be slowest on screen.
    /// </summary>
    private readonly Diarion.Helpers.AsyncDebouncer _accountDebouncer = new(TimeSpan.FromMilliseconds(150));

    /// <summary>Set once the strip is filled, so it is filled exactly once.</summary>
    private bool _statsAccountsLoaded;

    private async Task LoadFinanceAccountsAsync()
    {
        if (_statsAccountsLoaded) return;

        var accounts = await _financeService.GetAccountsAsync(includeArchived: false);

        // Checked again on the far side of the await, which is where the strip was being filled twice:
        // two loads that overlap both find it empty on the way in, and both add every account. The
        // flag is only raised here, so a failed read leaves the next appearance free to retry.
        if (_statsAccountsLoaded) return;
        _statsAccountsLoaded = true;

        foreach (var account in accounts)
        {
            StatsAccounts.Add(new AccountItemViewModel
            {
                Id = account.Id,
                Name = Diarion.Services.AccountLocalization.ResolveName(account),
                Icon = account.Icon,
                ColorHex = account.ColorHex
            });
        }

        OnPropertyChanged(nameof(HasStatsAccounts));
        UpdateStatsAccountSelection();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectStatsAccount(AccountItemViewModel? item)
    {
        SelectedStatsAccountId = item?.Id;
        UpdateStatsAccountSelection();
        _accountDebouncer.Debounce(ReloadOnMainThreadAsync);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectAllStatsAccounts()
    {
        SelectedStatsAccountId = null;
        UpdateStatsAccountSelection();
        _accountDebouncer.Debounce(ReloadOnMainThreadAsync);
    }

    private void UpdateStatsAccountSelection()
    {
        IsAllStatsAccountsSelected = SelectedStatsAccountId == null;
        foreach (var account in StatsAccounts)
        {
            account.IsSelected = account.Id == SelectedStatsAccountId;
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void OpenMenu()
    {
        _ = _navigationService.OpenFlyoutAsync();
    }
    /// <summary>
    /// Which days of the window were written on — by the same rule the streak counts, so the tile at the
    /// top of the home screen and this card agree. A new list each time: the squares redraw when their
    /// source is replaced.
    /// </summary>
    private async Task LoadActivityAsync(StatsRange range)
    {
        var written = await _diaryService.GetContentDatesAsync(range.Start, range.End) ?? Array.Empty<DateTime>();
        var days = (int)(range.End.Date - range.Start.Date).TotalDays + 1;

        ActivityDates = written.ToList();
        ActivityCountText = string.Format(AppResources.StatsActivityCountFormat, written.Count, days);
        HasActivityDays = true;
    }
}
