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
    /// How many guesses were put to the test and how many came through, e.g. "Checked: 8. Holding: 2".
    /// </summary>
    /// <remarks>
    /// Without the first number the list looks like everything the diary has to say, and every row in
    /// it looks inevitable. With it, the reader can see that six other candidates were tried and
    /// dropped — which is what makes the two that remain worth anything.
    /// </remarks>
    [ObservableProperty]
    private string _correlationSummaryText = string.Empty;

    /// <summary>
    /// The same test report for the day's energy, in a card of its own. Its hypotheses are corrected as
    /// a separate family (see <see cref="MoodAxis"/>), so the two cards never dilute each other.
    /// </summary>
    /// <remarks>
    /// Only two states, not three: the "how far along" and "nothing held" cards belong to the mood pass,
    /// and energy rests on exactly the same days, so a second progress card would repeat the first word
    /// for word. When nothing about energy holds, the card is simply absent.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnergyCorrelations))]
    private System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem> _energyCorrelations = new();

    public bool HasEnergyCorrelations => EnergyCorrelations.Count > 0;

    [ObservableProperty]
    private string _energyCorrelationSummaryText = string.Empty;

    /// <summary>
    /// The headline of the card that stands in for the list: either how close the data is to its
    /// first comparison, or that comparisons were made and none held.
    /// </summary>
    /// <remarks>
    /// Correlations need fourteen paired days, and until then the card was simply absent — during
    /// exactly the fortnight in which people decide whether an app is worth keeping.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInsightProgress))]
    private string _insightProgressText = string.Empty;

    /// <summary>
    /// The line under that headline. Bound rather than fixed in the page, because the two states it
    /// stands for want opposite things said: one promises links later, the other explains why there
    /// are none and does not promise.
    /// </summary>
    [ObservableProperty]
    private string _insightProgressHint = string.Empty;

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

    /// <summary>
    /// Turns the period's correlations into the card, in whichever of its three states applies.
    /// </summary>
    /// <remarks>
    /// The card reports a test, not a discovery. Every factor with enough paired days is a guess that
    /// was checked; most of them fail, and the two numbers that matter are how many were tried and how
    /// many came through. Before this there were only two states, and the empty one always blamed the
    /// diary: with fourteen days logged and nothing significant it printed "14 of 14 days logged"
    /// under a promise that links appear once there is enough to compare. There was enough. Nothing
    /// held — which is a result, and now says so.
    /// </remarks>
    private async Task LoadCorrelationsAsync(StatsRange range)
    {
        var correlations = await _correlationService.GetMoodCorrelationsAsync(range);
        var items = HoldingRows(correlations);

        Correlations = items;

        if (items.Count > 0)
        {
            CorrelationSummaryText = string.Format(
                AppResources.StatsHypothesesCheckedFormat, correlations.Count, items.Count);
            InsightProgressText = string.Empty;
            InsightProgressHint = string.Empty;
            return;
        }

        CorrelationSummaryText = string.Empty;

        if (correlations.Count > 0)
        {
            // Comparisons were made and every one of them failed. Counting days here would be a lie
            // about the reason, and the hint below would promise something already delivered.
            InsightProgressText = AppResources.StatsNoHypothesisHolds;
            InsightProgressHint = string.Format(
                AppResources.StatsNoHypothesisHoldsHintFormat, correlations.Count);
            return;
        }

        // Nothing could be compared yet — the normal state for the first fortnight, and an empty space
        // says nothing about why. Ask how far along the data is and say so instead.
        var readiness = await _correlationService.GetReadinessAsync(range);
        InsightProgressText = string.Format(
            AppResources.StatsInsightProgressFormat,
            Math.Min(readiness.PairedDays, readiness.RequiredDays),
            readiness.RequiredDays);
        InsightProgressHint = AppResources.StatsInsightProgressHint;
    }

    /// <summary>
    /// The energy pass. Same rows, same bar (three dots after adjustment), its own count of how many
    /// were tried — the number that makes the survivors worth anything is per family, not per screen.
    /// </summary>
    private async Task LoadEnergyCorrelationsAsync(StatsRange range)
    {
        var correlations = await _correlationService.GetEnergyCorrelationsAsync(range);
        var items = HoldingRows(correlations);

        EnergyCorrelations = items;
        EnergyCorrelationSummaryText = items.Count > 0
            ? string.Format(AppResources.StatsHypothesesCheckedFormat, correlations.Count, items.Count)
            : string.Empty;
    }

    /// <summary>
    /// Only the associations that survive the check (p &lt; 0.05 after adjustment, i.e. >= 3 dots),
    /// as rows. One builder for both axes so the two cards cannot drift apart in what a row says.
    /// </summary>
    private static System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem> HoldingRows(
        System.Collections.Generic.IReadOnlyList<MoodCorrelation> correlations)
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem>();

        foreach (var c in correlations.Where(c => c.Holds))
        {
            var arrow = c.Coefficient >= 0 ? "↑" : "↓";

            items.Add(new MoodCorrelationItem
            {
                Description = $"{arrow}  {FactorName(c.FactorKey)}",
                Dots = new string('●', c.Confidence) + new string('○', 5 - c.Confidence),
                Evidence = string.Format(AppResources.StatsCorrelationEvidenceFormat, c.SampleSize)
            });
        }

        return items;
    }

    /// <summary>
    /// The user-facing name of a factor. Structured factors are looked up; a theme cannot be,
    /// because the key carries the user's own sentence and the list is different every window.
    /// </summary>
    private static string FactorName(string key) => FactorNames.Display(key);

    public async Task LoadDataAsync(StatsRange range)
    {
        IsBusy = true;
        try
        {
            var moodStats = await _statisticsService.GetMoodStatisticsAsync(range);
            
            var totalEmotions = moodStats.EmotionCounts.Values.Sum();
            if (totalEmotions == 0)
            {
                IsEmpty = true;
                EmotionChartData.Clear();
                Correlations = new System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem>();
                CorrelationSummaryText = string.Empty;
                EnergyCorrelations = new System.Collections.ObjectModel.ObservableCollection<MoodCorrelationItem>();
                EnergyCorrelationSummaryText = string.Empty;
                InsightProgressText = string.Format(
                    AppResources.StatsInsightProgressFormat, 0, CorrelationService.MinSampleSize);
                InsightProgressHint = AppResources.StatsInsightProgressHint;
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

            // The donut and its legend read at quadrant grain: four accents, at most five segments,
            // however many emotions the picker offers. Twenty-nine slivers would be unreadable and the
            // palette has no colours for them. Emotions keep their own counts for the KPI tile and
            // the AI snapshot.
            var newEmotionData = new System.Collections.ObjectModel.ObservableCollection<EmotionChartItem>();
            var byQuadrant = moodStats.EmotionCounts
                .Where(kvp => kvp.Value > 0)
                .GroupBy(kvp => kvp.Key.ToQuadrant())
                .Select(g => (Quadrant: g.Key, Count: g.Sum(kvp => kvp.Value)))
                .OrderByDescending(g => g.Count)
                .ThenBy(g => (int)g.Quadrant);
            foreach (var (quadrant, count) in byQuadrant)
            {
                newEmotionData.Add(new EmotionChartItem
                {
                    Name = quadrant.ToLocalizedName(),
                    Percentage = (double)count / totalEmotions,
                    Color = Microsoft.Maui.Graphics.Color.FromArgb(quadrant.ToColorHex())
                });
            }
            EmotionChartData = newEmotionData;

            // A share is only a "most common mood" if one mood actually leads. With three emotions
            // tied at 33% the tile named whichever happened to sort first and the donut printed 33%
            // with nothing to attach it to — both asserting a winner that the data does not have.
            var ranked = moodStats.EmotionCounts
                .Where(kvp => kvp.Value > 0)
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => kvp.Value)
                .ToList();
            var hasSingleLeader = ranked.Count == 1 || (ranked.Count > 1 && ranked[0] > ranked[1]);

            if (hasSingleLeader)
            {
                TopEmotionShareText = ((double)ranked[0] / totalEmotions)
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
                    ColorHex = (p.DominantQuadrant != EmotionQuadrant.None
                        ? p.DominantQuadrant
                        : p.DominantEmotion.ToQuadrant()).ToColorHex()
                }));

            await LoadCorrelationsAsync(range);
            await LoadEnergyCorrelationsAsync(range);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
