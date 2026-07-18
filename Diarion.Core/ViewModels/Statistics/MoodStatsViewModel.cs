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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoodTrend))]
    private System.Collections.ObjectModel.ObservableCollection<MoodTrendPoint> _moodTrend = new();

    /// <summary>True when at least two days have logged mood, so a trend line is meaningful.</summary>
    public bool HasMoodTrend => MoodTrend.Count(p => p.HasData) >= 2;

    /// <summary>Daily valence for the KPI-tile sparkline; null marks a day with no logged mood.</summary>
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<double?> _moodSparkline = new();

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
                "SleepDuration" => AppResources.FactorSleepDuration,
                "SleepQuality" => AppResources.FactorSleepQuality,
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
                TopEmotionText = AppResources.EmotionNone;
                TopEmotionShareText = string.Empty;
                EntriesCountText = "0";
                MoodTrend = new System.Collections.ObjectModel.ObservableCollection<MoodTrendPoint>();
                MoodSparkline = new System.Collections.ObjectModel.ObservableCollection<double?>();
                return;
            }

            IsEmpty = false;
            EntriesCountText = totalEmotions.ToString(System.Globalization.CultureInfo.CurrentCulture);
            TopEmotionText = moodStats.TopEmotion switch
            {
                Emotion.Happy => AppResources.EmotionHappy,
                Emotion.Calm => AppResources.EmotionCalm,
                Emotion.Anxious => AppResources.EmotionAnxious,
                Emotion.Sad => AppResources.EmotionSad,
                Emotion.Angry => AppResources.EmotionAngry,
                _ => AppResources.EmotionNone
            };

            var newEmotionData = new System.Collections.ObjectModel.ObservableCollection<EmotionChartItem>();
            foreach (var kvp in moodStats.EmotionCounts.OrderByDescending(x => x.Value))
            {
                if (kvp.Value > 0)
                {
                    var colorHex = kvp.Key switch
                    {
                        Emotion.Happy => "#C26D53", // Coral
                        Emotion.Calm => "#8FA083",  // Sage
                        Emotion.Anxious => "#C9985A", // Amber
                        Emotion.Sad => "#929FA7",   // Ocean
                        Emotion.Angry => "#A87C8E", // Berry
                        _ => "#D0D3D4" // Dust
                    };
                    
                    var name = kvp.Key switch
                    {
                        Emotion.Happy => AppResources.EmotionHappy,
                        Emotion.Calm => AppResources.EmotionCalm,
                        Emotion.Anxious => AppResources.EmotionAnxious,
                        Emotion.Sad => AppResources.EmotionSad,
                        Emotion.Angry => AppResources.EmotionAngry,
                        _ => AppResources.EmotionNone
                    };

                    newEmotionData.Add(new EmotionChartItem
                    {
                        Name = name,
                        Percentage = (double)kvp.Value / totalEmotions,
                        Color = Microsoft.Maui.Graphics.Color.FromArgb(colorHex)
                    });
                }
            }
            EmotionChartData = newEmotionData;

            var topShare = newEmotionData.Count > 0 ? newEmotionData[0].Percentage : 0;
            TopEmotionShareText = topShare.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);

            MoodTrend = new System.Collections.ObjectModel.ObservableCollection<MoodTrendPoint>(moodStats.DailyTrend);
            MoodSparkline = new System.Collections.ObjectModel.ObservableCollection<double?>(
                moodStats.DailyTrend.Select(p => p.HasData ? (double?)p.Valence : null));

            await LoadCorrelationsAsync(days);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
