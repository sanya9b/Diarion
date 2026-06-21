using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

public partial class SleepStatsViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private string _averageSleepDurationText = string.Empty;

    [ObservableProperty]
    private string _averageSleepQualityText = string.Empty;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<SleepBarChartItem> _sleepChartData = new();

    public SleepStatsViewModel(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    public async Task LoadDataAsync(int days)
    {
        IsBusy = true;
        try
        {
            var sleepStats = await _statisticsService.GetSleepStatisticsAsync(days);
            
            // Check if there's any actual sleep data > 0
            if (!sleepStats.DailyData.Any(x => x.Duration.TotalHours > 0))
            {
                IsEmpty = true;
                AverageSleepDurationText = "0h 0m";
                AverageSleepQualityText = "0 / 10";
                SleepChartData.Clear();
                return;
            }

            IsEmpty = false;
            AverageSleepDurationText = $"{sleepStats.AverageSleepDuration.Hours}h {sleepStats.AverageSleepDuration.Minutes}m";
            AverageSleepQualityText = $"{sleepStats.AverageSleepQuality:F1} / 10";

            var sleepData = new System.Collections.ObjectModel.ObservableCollection<SleepBarChartItem>();
            
            // If less than 30 days, show daily. Else, group by week or month.
            if (days <= 14)
            {
                foreach (var pt in sleepStats.DailyData)
                {
                    sleepData.Add(new SleepBarChartItem 
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
                        
                    sleepData.Add(new SleepBarChartItem 
                    { 
                        Label = label, 
                        Value = avg 
                    });
                }
            }
            SleepChartData = sleepData;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
