using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

public partial class FinanceStatsViewModel : BaseViewModel
{
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isNotEmpty;

    [ObservableProperty]
    private decimal _totalIncome;

    [ObservableProperty]
    private decimal _totalExpense;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetBalanceText))]
    private decimal _netBalance;

    /// <summary>Net balance formatted with an explicit sign (e.g. "+1 149,50"), for the KPI tile.</summary>
    public string NetBalanceText => $"{(NetBalance >= 0 ? "+" : "-")}{System.Math.Abs(NetBalance):N2}";

    public ObservableCollection<CategoryStatItem> ExpenseByCategory { get; } = new();
    public ObservableCollection<CategoryStatItem> IncomeByCategory { get; } = new();

    public FinanceStatsViewModel(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    public async Task LoadDataAsync(int days)
    {
        var stats = await _statisticsService.GetFinanceStatisticsAsync(days);
        
        IsEmpty = stats.IsEmpty;
        IsNotEmpty = stats.IsNotEmpty;
        
        TotalIncome = stats.TotalIncome;
        TotalExpense = stats.TotalExpense;
        NetBalance = stats.TotalIncome - stats.TotalExpense;
        
        ExpenseByCategory.Clear();
        foreach (var item in stats.ExpenseByCategory)
        {
            ExpenseByCategory.Add(item);
        }

        IncomeByCategory.Clear();
        foreach (var item in stats.IncomeByCategory)
        {
            IncomeByCategory.Add(item);
        }
    }
}
