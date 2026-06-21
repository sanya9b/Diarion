using System;
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
    Productivity
}

public partial class StatisticsTabItem : ObservableObject
{
    public StatisticsTabOption Option { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class StatisticsViewModel : BaseViewModel
{
    private readonly Diarion.Services.IStatisticsService _statisticsService;

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
    private TimeRangeItem? _selectedTimeRange;

    public ViewModels.Statistics.MoodStatsViewModel MoodStats { get; }
    public ViewModels.Statistics.SleepStatsViewModel SleepStats { get; }
    public ViewModels.Statistics.ProductivityStatsViewModel ProductivityStats { get; }

    public StatisticsViewModel(
        Diarion.Services.IStatisticsService statisticsService,
        ViewModels.Statistics.MoodStatsViewModel moodStats,
        ViewModels.Statistics.SleepStatsViewModel sleepStats,
        ViewModels.Statistics.ProductivityStatsViewModel productivityStats)
    {
        _statisticsService = statisticsService;
        MoodStats = moodStats;
        SleepStats = sleepStats;
        ProductivityStats = productivityStats;
        
        Title = AppResources.StatisticsTitle;
        InitializeTabs();
        InitializeTimeRanges();
    }

    private void InitializeTabs()
    {
        Tabs = new System.Collections.ObjectModel.ObservableCollection<StatisticsTabItem>
        {
            new StatisticsTabItem { Option = StatisticsTabOption.General, DisplayName = "Огляд", IsSelected = true },
            new StatisticsTabItem { Option = StatisticsTabOption.Sleep, DisplayName = "Сон" },
            new StatisticsTabItem { Option = StatisticsTabOption.Productivity, DisplayName = "Продуктивність" }
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
            int days = (int)(SelectedTimeRange?.Option ?? TimeRangeOption.Week);
            
            if (IsGeneralTabVisible)
            {
                await MoodStats.LoadDataAsync(days);
            }
            else if (IsSleepTabVisible)
            {
                await SleepStats.LoadDataAsync(days);
            }
            else if (IsProductivityTabVisible)
            {
                await ProductivityStats.LoadDataAsync(days);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void OpenMenu()
    {
        Microsoft.Maui.Controls.Shell.Current.FlyoutIsPresented = true;
    }
}
