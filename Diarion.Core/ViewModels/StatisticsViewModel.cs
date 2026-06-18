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

public partial class StatisticsViewModel : BaseViewModel
{
    private readonly Diarion.Services.IDiaryService _diaryService;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<TimeRangeItem> _timeRanges = new();

    [ObservableProperty]
    private TimeRangeItem? _selectedTimeRange;


    [ObservableProperty]
    private string _averageSleepDurationText = string.Empty;

    [ObservableProperty]
    private string _averageSleepQualityText = string.Empty;

    [ObservableProperty]
    private string _topEmotionText = string.Empty;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<Diarion.Models.EmotionChartItem> _emotionChartData = new();

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<Diarion.Models.SleepBarChartItem> _sleepChartData = new();

    [ObservableProperty]
    private double _taskCompletionPercentage;

    [ObservableProperty]
    private string _taskCompletionText = string.Empty;

    [ObservableProperty]
    private string _taskStatsText = string.Empty;

    public StatisticsViewModel(Diarion.Services.IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = AppResources.StatisticsTitle;
        InitializeTimeRanges();
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
            var sleepStats = await _diaryService.GetSleepStatisticsAsync(days);
            AverageSleepDurationText = $"{sleepStats.AverageSleepDuration.Hours}h {sleepStats.AverageSleepDuration.Minutes}m";
            AverageSleepQualityText = $"{sleepStats.AverageSleepQuality:F1} / 10";

            var sleepData = new System.Collections.ObjectModel.ObservableCollection<Diarion.Models.SleepBarChartItem>();
            
            // If less than 30 days, show daily. Else, group by week or month.
            if (days <= 14)
            {
                foreach (var pt in sleepStats.DailyData)
                {
                    sleepData.Add(new Diarion.Models.SleepBarChartItem 
                    { 
                        Label = pt.Date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture), 
                        Value = pt.Duration.TotalHours 
                    });
                }
            }
            else
            {
                // Group by Week or Month
                int groupSize = days <= 90 ? 7 : 30;
                for (int i = 0; i < sleepStats.DailyData.Count; i += groupSize)
                {
                    var group = sleepStats.DailyData.Skip(i).Take(groupSize).ToList();
                    var avg = group.Any(x => x.Duration.TotalHours > 0) 
                        ? group.Where(x => x.Duration.TotalHours > 0).Average(x => x.Duration.TotalHours) 
                        : 0;
                    
                    string label = groupSize == 7 
                        ? $"W{i/7 + 1}" 
                        : group.First().Date.ToString("MMM", System.Globalization.CultureInfo.CurrentCulture);
                        
                    sleepData.Add(new Diarion.Models.SleepBarChartItem 
                    { 
                        Label = label, 
                        Value = avg 
                    });
                }
            }
            SleepChartData = sleepData;

            var moodStats = await _diaryService.GetMoodStatisticsAsync(days);
            
            var totalEmotions = moodStats.EmotionCounts.Values.Sum();
            if (totalEmotions == 0) totalEmotions = 1; // Prevent div by 0

            TopEmotionText = moodStats.TopEmotion switch
            {
                Models.Emotion.Happy => AppResources.EmotionHappy,
                Models.Emotion.Calm => AppResources.EmotionCalm,
                Models.Emotion.Anxious => AppResources.EmotionAnxious,
                Models.Emotion.Sad => AppResources.EmotionSad,
                Models.Emotion.Angry => AppResources.EmotionAngry,
                _ => AppResources.EmotionNone
            };

            var topPercentage = moodStats.TopEmotion != Models.Emotion.None 
                ? (double)moodStats.EmotionCounts[moodStats.TopEmotion] / totalEmotions 
                : 0;

            var newEmotionData = new System.Collections.ObjectModel.ObservableCollection<Diarion.Models.EmotionChartItem>();
            foreach (var kvp in moodStats.EmotionCounts.OrderByDescending(x => x.Value))
            {
                if (kvp.Value > 0)
                {
                    var colorHex = kvp.Key switch
                    {
                        Models.Emotion.Happy => "#C26D53", // Coral
                        Models.Emotion.Calm => "#8FA083",  // Sage
                        Models.Emotion.Anxious => "#C9985A", // Amber
                        Models.Emotion.Sad => "#929FA7",   // Ocean
                        Models.Emotion.Angry => "#A87C8E", // Berry
                        _ => "#D0D3D4" // Dust
                    };
                    
                    var name = kvp.Key switch
                    {
                        Models.Emotion.Happy => AppResources.EmotionHappy,
                        Models.Emotion.Calm => AppResources.EmotionCalm,
                        Models.Emotion.Anxious => AppResources.EmotionAnxious,
                        Models.Emotion.Sad => AppResources.EmotionSad,
                        Models.Emotion.Angry => AppResources.EmotionAngry,
                        _ => AppResources.EmotionNone
                    };

                    newEmotionData.Add(new Diarion.Models.EmotionChartItem
                    {
                        Name = name,
                        Percentage = (double)kvp.Value / totalEmotions,
                        Color = Microsoft.Maui.Graphics.Color.FromArgb(colorHex)
                    });
                }
            }
            EmotionChartData = newEmotionData;

            var todoStats = await _diaryService.GetTodoStatisticsAsync(days);
            TaskCompletionPercentage = todoStats.CompletionPercentage;
            TaskCompletionText = $"{todoStats.CompletionPercentage:P0}";
            TaskStatsText = $"{todoStats.CompletedCount} / {todoStats.TotalCount}";
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
