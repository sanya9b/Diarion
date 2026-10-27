using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels.Finance;

/// <summary>
/// Monthly budgets: the progress widgets, the category detail opened by tapping one, and the budget
/// form. Budgets are global across accounts, so everything here reads the unfiltered ledger.
/// </summary>
public partial class FinanceBudgetsViewModel : ObservableObject
{
    private readonly FinanceLedger _ledger;
    private readonly IFinanceService _financeService;
    private readonly IDialogService _dialogService;
    private readonly Func<Task> _reloadAsync;

    public FinanceBudgetsViewModel(
        FinanceLedger ledger,
        IFinanceService financeService,
        IDialogService dialogService,
        Func<Task> reloadAsync)
    {
        _ledger = ledger;
        _financeService = financeService;
        _dialogService = dialogService;
        _reloadAsync = reloadAsync;
    }

    public ObservableCollection<BudgetItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _hasBudgets;

    /// <summary>Whether the budgets feature is enabled in settings.</summary>
    [ObservableProperty]
    private bool _showBudgets = true;

    // --- Category detail (tap a budget widget) ---

    public ObservableCollection<FinanceTransaction> CategoryTransactions { get; } = new();

    [ObservableProperty]
    private bool _isCategoryDetailVisible;

    [ObservableProperty]
    private string _categoryDetailTitle = string.Empty;

    [ObservableProperty]
    private string _categoryDetailSummary = string.Empty;

    [ObservableProperty]
    private bool _hasCategoryTransactions;

    [ObservableProperty]
    private BudgetItemViewModel? _selectedBudget;

    // --- Form ---

    [ObservableProperty]
    private bool _isBudgetFormVisible;

    [ObservableProperty]
    private string _newBudgetCategory = string.Empty;

    [ObservableProperty]
    private string _newBudgetLimitText = string.Empty;

    private Budget? _editingBudget;
    public bool IsEditingBudget => _editingBudget != null;

    /// <summary>Called by the owner once the ledger holds this load's transactions.</summary>
    internal async Task LoadAsync()
    {
        var budgets = await _financeService.GetBudgetsAsync();
        var progress = BudgetCalculator.Compute(budgets, _ledger.Transactions, DateTime.Today);

        Items.Clear();
        foreach (var p in progress)
        {
            Items.Add(new BudgetItemViewModel
            {
                Id = p.Budget.Id,
                Category = p.Budget.Category,
                AmountText = $"{_ledger.Money(p.Spent)} / {_ledger.Money(p.Limit)}",
                Progress = p.Progress,
                ProgressPercentText = p.Fraction.ToString("P0", System.Globalization.CultureInfo.CurrentCulture),
                IsOverspent = p.IsOverspent,
                RemainingText = p.IsOverspent
                    ? string.Format(Diarion.Resources.Localization.AppResources.BudgetOverspentFormat, _ledger.Money(Math.Abs(p.Remaining)))
                    : string.Format(Diarion.Resources.Localization.AppResources.BudgetRemainingFormat, _ledger.Money(p.Remaining))
            });
        }

        HasBudgets = Items.Count > 0;
    }

    [RelayCommand]
    private void ShowCategoryDetail(BudgetItemViewModel item)
    {
        if (item == null) return;

        var today = DateTime.Today;
        var expenses = _ledger.Transactions
            .Where(t => t.Type == TransactionType.Expense
                        && t.Date.Year == today.Year && t.Date.Month == today.Month
                        && string.Equals(t.Category ?? string.Empty, item.Category ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Date)
            .ToList();

        CategoryTransactions.Clear();
        foreach (var t in expenses)
        {
            CategoryTransactions.Add(t);
        }

        HasCategoryTransactions = CategoryTransactions.Count > 0;
        SelectedBudget = item;
        CategoryDetailTitle = item.Category;
        CategoryDetailSummary = item.AmountText;
        IsCategoryDetailVisible = true;
    }

    [RelayCommand]
    private void HideCategoryDetail()
    {
        IsCategoryDetailVisible = false;
        CategoryTransactions.Clear();
    }

    [RelayCommand]
    private void ShowBudgetForm()
    {
        _editingBudget = null;
        OnPropertyChanged(nameof(IsEditingBudget));
        NewBudgetCategory = string.Empty;
        NewBudgetLimitText = string.Empty;
        IsBudgetFormVisible = true;
    }

    [RelayCommand]
    private void HideBudgetForm()
    {
        IsBudgetFormVisible = false;
        _editingBudget = null;
        OnPropertyChanged(nameof(IsEditingBudget));
        NewBudgetCategory = string.Empty;
        NewBudgetLimitText = string.Empty;
    }

    [RelayCommand]
    private async Task EditBudgetAsync(BudgetItemViewModel item)
    {
        if (item == null) return;

        IsCategoryDetailVisible = false;

        var budget = (await _financeService.GetBudgetsAsync()).FirstOrDefault(x => x.Id == item.Id);
        if (budget == null) return;

        _editingBudget = budget;
        OnPropertyChanged(nameof(IsEditingBudget));
        NewBudgetCategory = budget.Category;
        NewBudgetLimitText = budget.MonthlyLimit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        IsBudgetFormVisible = true;
    }

    [RelayCommand]
    private async Task SaveBudgetAsync()
    {
        var category = (NewBudgetCategory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(category)) return;

        if (!FinanceLedger.TryParseAmount(NewBudgetLimitText, out var limit)) return;

        var budget = _editingBudget ?? new Budget();
        budget.Category = category;
        budget.MonthlyLimit = limit;

        if (!await WriteGate.TryAsync(() => _financeService.SaveBudgetAsync(budget))) return;

        HideBudgetForm();
        await _reloadAsync();
    }

    [RelayCommand]
    private async Task DeleteBudgetAsync(BudgetItemViewModel item)
    {
        if (item == null) return;

        IsCategoryDetailVisible = false;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle ?? "Delete",
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg ?? "Are you sure you want to delete this record?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes ?? "Yes",
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo ?? "No");

        if (!confirm) return;

        if (!await WriteGate.TryAsync(() => _financeService.DeleteBudgetAsync(item.Id))) return;
        await _reloadAsync();
    }
}
