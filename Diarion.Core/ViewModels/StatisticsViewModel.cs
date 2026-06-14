using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Resources.Localization;

namespace Diarion.ViewModels;

public partial class StatisticsViewModel : BaseViewModel
{
    private readonly Diarion.Services.IDiaryService _diaryService;

    [ObservableProperty]
    private string _averageSleepDurationText = string.Empty;

    [ObservableProperty]
    private string _averageSleepQualityText = string.Empty;

    [ObservableProperty]
    private string _topEmotionText = string.Empty;

    public StatisticsViewModel(Diarion.Services.IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = AppResources.StatisticsTitle;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task LoadStatisticsAsync()
    {
        IsBusy = true;
        
        try
        {
            var sleepStats = await _diaryService.GetSleepStatisticsAsync(7);
            AverageSleepDurationText = $"{sleepStats.AverageSleepDuration.Hours}h {sleepStats.AverageSleepDuration.Minutes}m";
            AverageSleepQualityText = $"{sleepStats.AverageSleepQuality:F1} / 10";

            var moodStats = await _diaryService.GetMoodStatisticsAsync(7);
            TopEmotionText = moodStats.TopEmotion switch
            {
                Models.Emotion.Happy => AppResources.EmotionHappy,
                Models.Emotion.Calm => AppResources.EmotionCalm,
                Models.Emotion.Anxious => AppResources.EmotionAnxious,
                Models.Emotion.Sad => AppResources.EmotionSad,
                Models.Emotion.Angry => AppResources.EmotionAngry,
                _ => AppResources.EmotionNone
            };
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
