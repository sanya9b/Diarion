using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

/// <summary>One symptom row: the localized label and how often it was logged.</summary>
public class CycleSymptomItemViewModel
{
    public string Label { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
}

/// <summary>
/// The cycle tab. Unlike every other tab this one ignores the selected period: the shortest range on offer
/// is a week, and a week cannot contain a cycle — the section would read as empty for anyone who had not
/// changed the range first. It describes the whole log instead, and says so on the card. That holds for a
/// hand-picked range too: an arbitrary fortnight is no better a container for a cycle than a preset one.
/// </summary>
public partial class CycleStatsViewModel : ObservableObject
{
    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private string _averageCycleText = "—";

    [ObservableProperty]
    private string _shortestCycleText = "—";

    [ObservableProperty]
    private string _longestCycleText = "—";

    [ObservableProperty]
    private string _averagePeriodText = "—";

    /// <summary>Names the base the averages stand on, the way the forecast card does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBasis))]
    private string _basisText = string.Empty;

    public bool HasBasis => !string.IsNullOrEmpty(BasisText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCycles))]
    private ObservableCollection<SleepBarChartItem> _cycleChartData = new();

    public bool HasCycles => CycleChartData.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSymptoms))]
    private ObservableCollection<CycleSymptomItemViewModel> _symptoms = new();

    public bool HasSymptoms => Symptoms.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalendar))]
    private ObservableCollection<DateTime> _markedDates = new();

    /// <summary>The heatmap needs a stretch of history to say anything; below that it is just noise.</summary>
    public bool HasCalendar => MarkedDates.Count >= 5;

    [ObservableProperty]
    private DateTime _calendarStart = DateTime.Today.AddDays(-364);

    [ObservableProperty]
    private DateTime _calendarEnd = DateTime.Today;

    public CycleStatsViewModel(ICycleLogService cycleLogService, IProfileService profileService)
    {
        _cycleLogService = cycleLogService;
        _profileService = profileService;
    }

    /// <summary>Whether the tab may be shown at all — same gate the rest of the feature honours.</summary>
    public async Task<bool> IsAvailableAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        return profile?.IsCycleTrackingActive == true;
    }

    public async Task LoadDataAsync(StatsRange range)
    {
        IsBusy = true;
        try
        {
            var marked = await _cycleLogService.GetMarkedDatesAsync();
            var logs = await _cycleLogService.GetLogsAsync();

            var history = CycleForecastCalculator.BuildHistory(marked);
            var stats = CycleStatisticsCalculator.Compute(history, logs, DateTime.Today);

            IsEmpty = stats.IsEmpty;
            if (IsEmpty)
            {
                CycleChartData = new ObservableCollection<SleepBarChartItem>();
                Symptoms = new ObservableCollection<CycleSymptomItemViewModel>();
                MarkedDates = new ObservableCollection<DateTime>();
                BasisText = string.Empty;
                return;
            }

            AverageCycleText = FormatDays(stats.AverageCycleLength);
            ShortestCycleText = FormatDays(stats.ShortestCycle);
            LongestCycleText = FormatDays(stats.LongestCycle);
            AveragePeriodText = stats.AveragePeriodLength.HasValue
                ? stats.AveragePeriodLength.Value.ToString("0.#", CultureInfo.CurrentCulture)
                : "—";

            BasisText = BuildBasis(stats);

            CycleChartData = new ObservableCollection<SleepBarChartItem>(
                stats.Cycles.Select(c => new SleepBarChartItem
                {
                    Label = c.Start.ToString("MMM", CultureInfo.CurrentCulture),
                    Value = c.Days
                }));

            Symptoms = new ObservableCollection<CycleSymptomItemViewModel>(
                stats.Symptoms.Select(s => new CycleSymptomItemViewModel
                {
                    Label = ResolveSymptom(s.Key),
                    CountText = s.Count.ToString(CultureInfo.CurrentCulture)
                }));

            MarkedDates = new ObservableCollection<DateTime>(stats.MarkedDates);
            CalendarEnd = DateTime.Today;
            CalendarStart = DateTime.Today.AddDays(-364);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatDays(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.CurrentCulture) : "—";

    private static string BuildBasis(CycleStatistics stats)
    {
        if (stats.RecordedCycleCount == 0)
        {
            return AppResources.CycleStatsNoCompleteCycle;
        }

        var basis = string.Format(
            CultureInfo.CurrentCulture,
            AppResources.CycleStatsBasisFormat,
            stats.RecordedCycleCount);

        if (stats.DiscardedCycleCount > 0)
        {
            basis += " " + string.Format(
                CultureInfo.CurrentCulture,
                AppResources.CycleStatsDiscardedFormat,
                stats.DiscardedCycleCount);
        }

        return basis;
    }

    private static string ResolveSymptom(string key) =>
        AppResources.ResourceManager.GetString(key, AppResources.Culture ?? CultureInfo.CurrentUICulture) ?? key;
}
