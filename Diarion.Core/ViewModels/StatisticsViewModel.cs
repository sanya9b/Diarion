using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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
    Cycle
}

public partial class StatisticsTabItem : ObservableObject
{
    public StatisticsTabOption Option { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Emoji glyph shown before the tab label in the segmented selector.</summary>
    public string Icon { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class StatisticsViewModel : BaseViewModel
{
    private readonly Diarion.Services.IStatisticsService _statisticsService;
    private readonly Diarion.Services.IDiaryService _diaryService;
    private readonly Diarion.Services.IFinanceService _financeService;
    private readonly Diarion.Services.INavigationService _navigationService;

    [ObservableProperty]
    private int _currentStreak;

    public bool IsStreakVisible => CurrentStreak > 0;

    /// <summary>
    /// The run only survives because a missed day was forgiven. Worth saying: an unmarked number implies
    /// an unbroken run, and the quota is finite — the next miss ends it.
    /// </summary>
    [ObservableProperty]
    private bool _isStreakHeldByGrace;

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
    private TimeRangeItem? _selectedTimeRange;

    public ViewModels.Statistics.MoodStatsViewModel MoodStats { get; }

    /// <summary>Verbatim passages describing what the selected window was about. Empty when AI is off.</summary>
    public ObservableCollection<DigestExcerptItem> DigestExcerpts { get; } = [];

    [ObservableProperty]
    private bool _hasDigest;
    public ViewModels.Statistics.SleepStatsViewModel SleepStats { get; }
    public ViewModels.Statistics.ProductivityStatsViewModel ProductivityStats { get; }
    public ViewModels.Statistics.FinanceStatsViewModel FinanceStats { get; }
    public ViewModels.Statistics.HabitStatsViewModel HabitStats { get; }
    public ViewModels.Statistics.CycleStatsViewModel CycleStats { get; }

    public StatisticsViewModel(
        Diarion.Services.IStatisticsService statisticsService,
        Diarion.Services.IDiaryService diaryService,
        Diarion.Services.IFinanceService financeService,
        Diarion.Services.INavigationService navigationService,
        ViewModels.Statistics.MoodStatsViewModel moodStats,
        Services.Ai.IDigestService digestService,
        ViewModels.Statistics.SleepStatsViewModel sleepStats,
        ViewModels.Statistics.ProductivityStatsViewModel productivityStats,
        ViewModels.Statistics.FinanceStatsViewModel financeStats,
        ViewModels.Statistics.HabitStatsViewModel habitStats,
        ViewModels.Statistics.CycleStatsViewModel cycleStats)
    {
        _statisticsService = statisticsService;
        _diaryService = diaryService;
        _financeService = financeService;
        _navigationService = navigationService;
        MoodStats = moodStats;
        _digestService = digestService;
        SleepStats = sleepStats;
        ProductivityStats = productivityStats;
        FinanceStats = financeStats;
        HabitStats = habitStats;
        CycleStats = cycleStats;

        Title = AppResources.StatisticsTitle;
        InitializeTabs();
        InitializeTimeRanges();
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
                Icon = "🩸"
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
            new StatisticsTabItem { Option = StatisticsTabOption.General, DisplayName = AppResources.TabGeneral, Icon = "😊", IsSelected = true },
            new StatisticsTabItem { Option = StatisticsTabOption.Sleep, DisplayName = AppResources.TabSleep, Icon = "😴" },
            new StatisticsTabItem { Option = StatisticsTabOption.Productivity, DisplayName = AppResources.TabProductivity, Icon = "✅" },
            new StatisticsTabItem { Option = StatisticsTabOption.Finance, DisplayName = AppResources.FinanceTitle ?? "Finance", Icon = "💸" },
            new StatisticsTabItem { Option = StatisticsTabOption.Habits, DisplayName = AppResources.TabHabits, Icon = "🌱" }
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

        // Load data for the selected tab when switched
        _ = LoadStatisticsAsync();
    }

    private void InitializeTimeRanges()
    {
        TimeRanges = new System.Collections.ObjectModel.ObservableCollection<TimeRangeItem>
        {
            new TimeRangeItem { Option = TimeRangeOption.Week, DisplayName = AppResources.TimeRangeWeek, IsSelected = true },
            new TimeRangeItem { Option = TimeRangeOption.TwoWeeks, DisplayName = AppResources.TimeRangeTwoWeeks },
            new TimeRangeItem { Option = TimeRangeOption.Month, DisplayName = AppResources.TimeRangeMonth },
            new TimeRangeItem { Option = TimeRangeOption.ThreeMonths, DisplayName = AppResources.TimeRange3Months },
            new TimeRangeItem { Option = TimeRangeOption.SixMonths, DisplayName = AppResources.TimeRange6Months },
            new TimeRangeItem { Option = TimeRangeOption.Year, DisplayName = AppResources.TimeRangeYear }
        };
        SelectedTimeRange = TimeRanges[0];
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task SelectTimeRangeAsync(TimeRangeItem item)
    {
        if (item == null || item.IsSelected) return;

        foreach (var r in TimeRanges)
        {
            r.IsSelected = false;
        }
        item.IsSelected = true;
        SelectedTimeRange = item;

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
            OnPropertyChanged(nameof(IsStreakVisible));

            int days = (int)(SelectedTimeRange?.Option ?? TimeRangeOption.Week);
            
            if (IsGeneralTabVisible)
            {
                await MoodStats.LoadDataAsync(days);
                await LoadDigestAsync(days);
            }
            else if (IsSleepTabVisible)
            {
                await SleepStats.LoadDataAsync(days);
            }
            else if (IsProductivityTabVisible)
            {
                await ProductivityStats.LoadDataAsync(days);
            }
            else if (IsFinanceTabVisible)
            {
                await LoadFinanceAccountsAsync();
                await FinanceStats.LoadDataAsync(days, SelectedStatsAccountId);
            }
            else if (IsHabitsTabVisible)
            {
                await HabitStats.LoadDataAsync(days);
            }
            else if (IsCycleTabVisible)
            {
                await CycleStats.LoadDataAsync(days);
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

    private async Task LoadFinanceAccountsAsync()
    {
        if (StatsAccounts.Count > 0) return;

        var accounts = await _financeService.GetAccountsAsync(includeArchived: false);
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
        _accountDebouncer.Debounce(LoadStatisticsAsync);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectAllStatsAccounts()
    {
        SelectedStatsAccountId = null;
        UpdateStatsAccountSelection();
        _accountDebouncer.Debounce(LoadStatisticsAsync);
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
    private readonly Services.Ai.IDigestService _digestService;

    /// <summary>
    /// The digest quotes the user's own sentences rather than paraphrasing them. That is what lets
    /// weekly and monthly reports work on every device with no generative model at all.
    /// </summary>
    private async Task LoadDigestAsync(int days)
    {
        var end = DateTime.Today;
        var start = end.AddDays(-(days - 1));

        var digest = await _digestService.BuildAsync(start, end);

        DigestExcerpts.Clear();
        foreach (var excerpt in digest.Excerpts)
        {
            DigestExcerpts.Add(new DigestExcerptItem(excerpt));
        }

        HasDigest = digest.HasContent;
    }
}

/// <summary>A digest line with its date already formatted for display.</summary>
public sealed class DigestExcerptItem
{
    public DigestExcerptItem(Services.Ai.DigestExcerpt excerpt)
    {
        Text = excerpt.Text;
        DateLabel = excerpt.Date.ToString("d MMMM", System.Globalization.CultureInfo.CurrentCulture);
    }

    public string Text { get; }

    public string DateLabel { get; }
}