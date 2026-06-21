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
    private readonly IDiaryService _diaryService;

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

    [ObservableProperty]
    private string _newAmountText = string.Empty;

    [ObservableProperty]
    private string _newCategory = string.Empty;

    [ObservableProperty]
    private string _newNote = string.Empty;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

    public bool IsExpenseTypeSelected => NewTransactionType == TransactionType.Expense;
    public bool IsIncomeTypeSelected => NewTransactionType == TransactionType.Income;

    public FinanceViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = Diarion.Resources.Localization.AppResources.FinanceTitle ?? "Дохід/Витрати";
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var transactions = await _diaryService.GetFinanceTransactionsAsync();
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
    private void ToggleAddTransaction()
    {
        IsAddTransactionVisible = !IsAddTransactionVisible;
    }

    [RelayCommand]
    private void SetTransactionType(string typeStr)
    {
        if (Enum.TryParse<TransactionType>(typeStr, out var type))
        {
            NewTransactionType = type;
            OnPropertyChanged(nameof(IsExpenseTypeSelected));
            OnPropertyChanged(nameof(IsIncomeTypeSelected));
        }
    }

    [RelayCommand]
    private async Task SaveTransactionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAmountText) || !decimal.TryParse(NewAmountText.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            return;
        }

        var transaction = new FinanceTransaction
        {
            Type = NewTransactionType,
            Amount = amount,
            Category = NewCategory?.Trim() ?? string.Empty,
            Note = NewNote?.Trim() ?? string.Empty,
            Date = NewDate.Date
        };

        await _diaryService.SaveFinanceTransactionAsync(transaction);
        
        NewAmountText = string.Empty;
        NewCategory = string.Empty;
        NewNote = string.Empty;
        NewDate = DateTime.Today;
        IsAddTransactionVisible = false;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(FinanceTransaction transaction)
    {
        if (transaction == null) return;

        bool confirm = await Microsoft.Maui.Controls.Shell.Current.DisplayAlertAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle ?? "Видалити",
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg ?? "Ви впевнені, що хочете видалити цей запис?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes ?? "Так",
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo ?? "Ні");

        if (confirm)
        {
            await _diaryService.DeleteFinanceTransactionAsync(transaction.Id);
            await LoadAsync();
        }
    }
}
