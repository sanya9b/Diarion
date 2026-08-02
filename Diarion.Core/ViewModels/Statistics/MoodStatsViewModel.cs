using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.ViewModels;

namespace Diarion.ViewModels.Statistics;

public partial class MoodStatsViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;
    private readonly ICorrelationService _correlationService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowHourlyMoodHint))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private string _topEmotionText = string.Empty;

    /// <summary>Share of the most frequent emotion (e.g. "42%"), for the KPI tile.</summary>
    [ObservableProperty]
    private string _topEmotionShareText = string.Empty;

    /// <summary>Total number of logged emotion entries in the period, for the KPI tile.</summary>
    [ObservableProperty]
    private string _entriesCountText = "0";

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<EmotionChartItem> _emotionChartData = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCorrelations))]
    private System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem> _correlations = new();

    public bool HasCorrelations => Correlations.Count > 0;

    /// <summary>
    /// How close the data is to its first insight, shown only while there is none. Correlations need
    /// fourteen paired days, and until then the card was simply absent — during exactly the fortnight
    /// in which people decide whether an app is worth keeping.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInsightProgress))]
    private string _insightProgressText = string.Empty;

    public bool HasInsightProgress => !string.IsNullOrEmpty(InsightProgressText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoodTrend))]
    private System.Collections.ObjectModel.ObservableCollection<MoodTrendPoint> _moodTrend = new();

    /// <summary>True when at least two days have logged mood, so a trend line is meaningful.</summary>
    public bool HasMoodTrend => MoodTrend.Count(p => p.HasData) >= 2;

    /// <summary>Average valence per hour of day, always 17 slots (7..23).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHourlyMood))]
    [NotifyPropertyChangedFor(nameof(ShowHourlyMoodHint))]
    private System.Collections.ObjectModel.ObservableCollection<MoodHourPoint> _hourlyMoodProfile = new();

    public bool HasHourlyMood => HourlyMoodProfile.Any(p => p.HasData);

    /// <summary>Mood was logged, but only ever with the day-level picker — prompt instead of an empty chart.</summary>
    public bool ShowHourlyMoodHint => !IsEmpty && !HasHourlyMood;

    /// <summary>Daily valence for the KPI-tile sparkline; null marks a day with no logged mood.</summary>
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<double?> _moodSparkline = new();

    /// <summary>Per-day cells for the Year-in-Pixels heatmap.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoodCalendar))]
    private System.Collections.ObjectModel.ObservableCollection<MoodHeatDay> _moodCalendar = new();

    /// <summary>Show the heatmap only for windows of at least ~a month with some logged mood.</summary>
    public bool HasMoodCalendar => MoodCalendar.Count >= 28 && MoodCalendar.Any(d => d.HasData);

    public MoodStatsViewModel(IStatisticsService statisticsService, ICorrelationService correlationService)
    {
        _statisticsService = statisticsService;
        _correlationService = correlationService;
    }

    private async Task LoadCorrelationsAsync(int days)
    {
        var correlations = await _correlationService.GetMoodCorrelationsAsync(days);
        var items = new System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem>();

        // Only surface associations that are statistically significant (p < 0.05, i.e. >= 3 dots).
        foreach (var c in correlations.Where(c => c.Confidence >= 3))
        {
            var factorName = c.FactorKey switch
            {
                CorrelationService.Factors.SleepDuration => AppResources.FactorSleepDuration,
                CorrelationService.Factors.SleepQuality => AppResources.FactorSleepQuality,
                CorrelationService.Factors.CyclePeriodDay => AppResources.FactorCyclePeriodDay,
                CorrelationService.Factors.CycleSymptomLoad => AppResources.FactorCycleSymptoms,
                CorrelationService.Factors.HabitCompletion => AppResources.FactorHabitCompletion,
                CorrelationService.Factors.MealsLogged => AppResources.FactorMealsLogged,
                CorrelationService.Factors.TaskCompletion => AppResources.FactorTaskCompletion,
                CorrelationService.Factors.DailySpend => AppResources.FactorDailySpend,
                _ => c.FactorKey
            };
            var arrow = c.Coefficient >= 0 ? "↑" : "↓";

            items.Add(new MoodCorrelationItem
            {
                Description = $"{arrow}  {factorName}",
                Dots = new string('●', c.Confidence) + new string('○', 5 - c.Confidence)
            });
        }

        Correlations = items;

        // Nothing to show yet is the normal state for the first fortnight, and an empty space says
        // nothing about why. Ask how far along the data is and say so instead.
        if (items.Count == 0)
        {
            var readiness = await _correlationService.GetReadinessAsync(days);
            InsightProgressText = string.Format(
                AppResources.StatsInsightProgressFormat,
                Math.Min(readiness.PairedDays, readiness.RequiredDays),
                readiness.RequiredDays);
        }
        else
        {
            InsightProgressText = string.Empty;
        }
    }

    public async Task LoadDataAsync(int days)
    {
        IsBusy = true;
        try
        {
            var moodStats = await _statisticsService.GetMoodStatisticsAsync(days);
            
            var totalEmotions = moodStats.EmotionCounts.Values.Sum();
            if (totalEmotions == 0)
            {
                IsEmpty = true;
                EmotionChartData.Clear();
                Correlations = new System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem>();
                InsightProgressText = string.Format(
                    AppResources.StatsInsightProgressFormat, 0, CorrelationService.MinSampleSize);
                TopEmotionText = AppResources.EmotionNone;
                TopEmotionShareText = string.Empty;
                EntriesCountText = "0";
                MoodTrend = new System.Collections.ObjectModel.ObservableCollection<MoodTrendPoint>();
                HourlyMoodProfile = new System.Collections.ObjectModel.ObservableCollection<MoodHourPoint>();
                MoodSparkline = new System.Collections.ObjectModel.ObservableCollection<double?>();
                MoodCalendar = new System.Collections.ObjectModel.ObservableCollection<MoodHeatDay>();
                return;
            }

            IsEmpty = false;
            EntriesCountText = totalEmotions.ToString(System.Globalization.CultureInfo.CurrentCulture);
            TopEmotionText = moodStats.TopEmotion.ToLocalizedName();

            var newEmotionData = new System.Collections.ObjectModel.ObservableCollection<EmotionChartItem>();
            foreach (var kvp in moodStats.EmotionCounts.OrderByDescending(x => x.Value))
            {
                if (kvp.Value > 0)
                {
                    var colorHex = kvp.Key.ToColorHex();

                    newEmotionData.Add(new EmotionChartItem
                    {
                        Name = kvp.Key.ToLocalizedName(),
                        Percentage = (double)kvp.Value / totalEmotions,
                        Color = Microsoft.Maui.Graphics.Color.FromArgb(colorHex)
                    });
                }
            }
            EmotionChartData = newEmotionData;

            // A share is only a "most common mood" if one mood actually leads. With three emotions
            // tied at 33% the tile named whichever happened to sort first and the donut printed 33%
            // with nothing to attach it to — both asserting a winner that the data does not have.
            var hasSingleLeader = newEmotionData.Count == 1
                || (newEmotionData.Count > 1 && newEmotionData[0].Percentage > newEmotionData[1].Percentage);

            if (hasSingleLeader)
            {
                TopEmotionShareText = newEmotionData[0].Percentage
                    .ToString("P0", System.Globalization.CultureInfo.CurrentCulture);
            }
            else
            {
                TopEmotionText = AppResources.StatsNoLeadingEmotion;
                TopEmotionShareText = string.Empty;
            }

            MoodTrend = new System.Collections.ObjectModel.ObservableCollection<MoodTrendPoint>(moodStats.DailyTrend);
            HourlyMoodProfile = new System.Collections.ObjectModel.ObservableCollection<MoodHourPoint>(moodStats.HourlyProfile);
            MoodSparkline = new System.Collections.ObjectModel.ObservableCollection<double?>(
                moodStats.DailyTrend.Select(p => p.HasData ? (double?)p.Valence : null));
            MoodCalendar = new System.Collections.ObjectModel.ObservableCollection<MoodHeatDay>(
                moodStats.DailyTrend.Select(p => new MoodHeatDay
                {
                    Date = p.Date,
                    HasData = p.HasData,
                    ColorHex = p.DominantEmotion.ToColorHex()
                }));

            await LoadCorrelationsAsync(days);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
