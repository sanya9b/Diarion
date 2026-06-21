using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

public partial class ProductivityStatsViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private double _taskCompletionPercentage;

    [ObservableProperty]
    private string _taskCompletionText = string.Empty;

    [ObservableProperty]
    private string _taskStatsText = string.Empty;

    public ProductivityStatsViewModel(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    public async Task LoadDataAsync(int days)
    {
        IsBusy = true;
        try
        {
            var todoStats = await _statisticsService.GetTodoStatisticsAsync(days);
            
            if (todoStats.TotalCount == 0)
            {
                IsEmpty = true;
                TaskCompletionPercentage = 0;
                TaskCompletionText = "0%";
                TaskStatsText = "0 / 0";
                return;
            }

            IsEmpty = false;
            TaskCompletionPercentage = todoStats.CompletionPercentage;
            TaskCompletionText = $"{todoStats.CompletionPercentage:P0}";
            TaskStatsText = $"{todoStats.CompletedCount} / {todoStats.TotalCount}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
