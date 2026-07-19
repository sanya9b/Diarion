using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class FinanceViewModel : BaseViewModel
{
    private readonly IFinanceService _financeService;

    public ObservableCollection<FinanceTransaction> Transactions { get; } = new();
    public ObservableCollection<BudgetItemViewModel> Budgets { get; } = new();

    [ObservableProperty]
    private bool _hasBudgets;

    [ObservableProperty]
    private bool _isBudgetFormVisible;

    [ObservableProperty]
    private string _newBudgetCategory = string.Empty;

    [ObservableProperty]
    private string _newBudgetLimitText = string.Empty;

    private Budget? _editingBudget;
    public bool IsEditingBudget => _editingBudget != null;

    [ObservableProperty]
    private decimal _totalBalance;

    [ObservableProperty]
    private decimal _monthIncome;

    [ObservableProperty]
    private decimal _monthExpense;

    [ObservableProperty]
    private bool _isAddTransactionVisible;

    [ObservableProperty]
    private TransactionType _newTransactionType = TransactionType.Expense;

    private FinanceTransaction? _editingTransaction;

    public bool IsEditing => _editingTransaction != null;

    [ObservableProperty]
    private string _newAmountText = string.Empty;

    [ObservableProperty]
    private string _newCategory = string.Empty;

    partial void OnNewCategoryChanged(string value)
    {
        UpdateSuggestions(value);
    }

    [ObservableProperty]
    private string _newNote = string.Empty;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

    public bool IsExpenseTypeSelected => NewTransactionType == TransactionType.Expense;
    public bool IsIncomeTypeSelected => NewTransactionType == TransactionType.Income;

    private List<string> _allCategories = new();
    public ObservableCollection<string> SuggestedCategories { get; } = new();

    private readonly IDialogService _dialogService;

    public FinanceViewModel(IFinanceService financeService, IDialogService dialogService)
    {
        _financeService = financeService;
        _dialogService = dialogService;
        Title = Diarion.Resources.Localization.AppResources.FinanceTitle ?? "Income/Expenses";
    }

    private void UpdateSuggestions(string query)
    {
        SuggestedCategories.Clear();
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var c in _allCategories.Take(5))
            {
                SuggestedCategories.Add(c);
            }
            return;
        }

        var filtered = _allCategories
            .Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase) && !c.Equals(query, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        foreach (var c in filtered)
        {
            SuggestedCategories.Add(c);
        }
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        NewCategory = category;
        SuggestedCategories.Clear(); // Hide suggestions after selection
    }

    private void ResetForm()
    {
        _editingTransaction = null;
        OnPropertyChanged(nameof(IsEditing));
        NewAmountText = string.Empty;
        NewCategory = string.Empty;
        NewNote = string.Empty;
        NewDate = DateTime.Today;
        NewTransactionType = TransactionType.Expense;
        OnPropertyChanged(nameof(IsExpenseTypeSelected));
        OnPropertyChanged(nameof(IsIncomeTypeSelected));
    }

    [RelayCommand]
    private async Task EditTransactionAsync(FinanceTransaction transaction)
    {
        if (transaction == null) return;
        
        _editingTransaction = transaction;
        OnPropertyChanged(nameof(IsEditing));
        
        NewTransactionType = transaction.Type;
        NewAmountText = transaction.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        NewCategory = transaction.Category;
        NewNote = transaction.Note;
        NewDate = transaction.Date;
        
        OnPropertyChanged(nameof(IsExpenseTypeSelected));
        OnPropertyChanged(nameof(IsIncomeTypeSelected));

        IsAddTransactionVisible = true;
        await LoadCategoriesForCurrentTypeAsync();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var transactions = await _financeService.GetFinanceTransactionsAsync();
            Transactions.Clear();
            foreach (var t in transactions)
            {
                Transactions.Add(t);
            }

            CalculateBalances(transactions);
            await LoadBudgetsAsync(transactions);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadBudgetsAsync(System.Collections.Generic.List<FinanceTransaction> transactions)
    {
        var budgets = await _financeService.GetBudgetsAsync();
        var progress = BudgetCalculator.Compute(budgets, transactions, DateTime.Today);

        Budgets.Clear();
        foreach (var p in progress)
        {
            Budgets.Add(new BudgetItemViewModel
            {
                Id = p.Budget.Id,
                Category = p.Budget.Category,
                AmountText = $"{p.Spent:N2} / {p.Limit:N2}",
                Progress = p.Progress,
                ProgressPercentText = p.Fraction.ToString("P0", System.Globalization.CultureInfo.CurrentCulture),
                IsOverspent = p.IsOverspent,
                RemainingText = p.IsOverspent
                    ? string.Format(Diarion.Resources.Localization.AppResources.BudgetOverspentFormat, Math.Abs(p.Remaining).ToString("N2"))
                    : string.Format(Diarion.Resources.Localization.AppResources.BudgetRemainingFormat, p.Remaining.ToString("N2"))
            });
        }

        HasBudgets = Budgets.Count > 0;
    }

    private void CalculateBalances(System.Collections.Generic.List<FinanceTransaction> transactions)
    {
        TotalBalance = transactions.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount) -
                       transactions.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);

        var currentMonth = DateTime.Today.Month;
        var currentYear = DateTime.Today.Year;
        var thisMonthTransactions = transactions.Where(x => x.Date.Month == currentMonth && x.Date.Year == currentYear).ToList();

        MonthIncome = thisMonthTransactions.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
        MonthExpense = thisMonthTransactions.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
    }

    [RelayCommand]
    private async Task ToggleAddTransactionAsync()
    {
        IsAddTransactionVisible = !IsAddTransactionVisible;
        if (IsAddTransactionVisible)
        {
            if (!IsEditing) 
            {
                ResetForm();
            }
            await LoadCategoriesForCurrentTypeAsync();
        }
        else
        {
            ResetForm();
        }
    }

    private async Task LoadCategoriesForCurrentTypeAsync()
    {
        _allCategories = await _financeService.GetCategoriesAsync(NewTransactionType);
        UpdateSuggestions(NewCategory);
    }

    [RelayCommand]
    private async Task SetTransactionTypeAsync(string typeStr)
    {
        if (Enum.TryParse<TransactionType>(typeStr, out var type))
        {
            NewTransactionType = type;
            OnPropertyChanged(nameof(IsExpenseTypeSelected));
            OnPropertyChanged(nameof(IsIncomeTypeSelected));
            await LoadCategoriesForCurrentTypeAsync();
        }
    }

    [RelayCommand]
    private async Task SaveTransactionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAmountText) || !decimal.TryParse(NewAmountText.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            return;
        }

        FinanceTransaction transaction;
        if (_editingTransaction != null)
        {
            transaction = _editingTransaction;
            transaction.Type = NewTransactionType;
            transaction.Amount = amount;
            transaction.Category = NewCategory?.Trim() ?? string.Empty;
            transaction.Note = NewNote?.Trim() ?? string.Empty;
            transaction.Date = NewDate.Date;
        }
        else
        {
            transaction = new FinanceTransaction
            {
                Type = NewTransactionType,
                Amount = amount,
                Category = NewCategory?.Trim() ?? string.Empty,
                Note = NewNote?.Trim() ?? string.Empty,
                Date = NewDate.Date
            };
        }

        await _financeService.SaveFinanceTransactionAsync(transaction);
        
        ResetForm();
        IsAddTransactionVisible = false;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(FinanceTransaction transaction)
    {
        if (transaction == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle ?? "Delete",
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg ?? "Are you sure you want to delete this record?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes ?? "Yes",
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo ?? "No");

        if (confirm)
        {
            await _financeService.DeleteFinanceTransactionAsync(transaction.Id);
            await LoadAsync();
        }
    }

    // --- Budgets ---

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

        if (!decimal.TryParse((NewBudgetLimitText ?? string.Empty).Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal limit) || limit <= 0)
        {
            return;
        }

        var budget = _editingBudget ?? new Budget();
        budget.Category = category;
        budget.MonthlyLimit = limit;

        await _financeService.SaveBudgetAsync(budget);

        HideBudgetForm();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteBudgetAsync(BudgetItemViewModel item)
    {
        if (item == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle ?? "Delete",
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg ?? "Are you sure you want to delete this record?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes ?? "Yes",
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo ?? "No");

        if (!confirm) return;

        await _financeService.DeleteBudgetAsync(item.Id);
        await LoadAsync();
    }
}

/// <summary>A budget's computed progress row on the finance page.</summary>
public class BudgetItemViewModel
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string AmountText { get; set; } = string.Empty;      // "spent / limit"
    public string RemainingText { get; set; } = string.Empty;
    public string ProgressPercentText { get; set; } = string.Empty;
    public double Progress { get; set; }
    public bool IsOverspent { get; set; }
}
