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

    public FinanceViewModel(IFinanceService financeService)
    {
        _financeService = financeService;
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
        }
        finally
        {
            IsBusy = false;
        }
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

        bool confirm = await Microsoft.Maui.Controls.Shell.Current.DisplayAlertAsync(
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
}
