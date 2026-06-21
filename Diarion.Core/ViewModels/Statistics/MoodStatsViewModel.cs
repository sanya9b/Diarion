using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

public partial class MoodStatsViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private string _topEmotionText = string.Empty;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<EmotionChartItem> _emotionChartData = new();

    public MoodStatsViewModel(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
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
                TopEmotionText = AppResources.EmotionNone;
                return;
            }

            IsEmpty = false;
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
        }
        finally
        {
            IsBusy = false;
        }
    }
}
