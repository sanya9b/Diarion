using System;
using System.Collections.Generic;
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
/// The add/edit transaction form, with its category suggestions and account picker, plus deleting a
/// transaction from the feed. The picker reuses the account strip's items, so it leans on
/// <see cref="FinanceAccountsViewModel"/> to highlight the chosen one.
/// </summary>
public partial class FinanceTransactionFormViewModel : ObservableObject
{
    private readonly FinanceLedger _ledger;
    private readonly FinanceAccountsViewModel _accounts;
    private readonly IFinanceService _financeService;
    private readonly IDialogService _dialogService;
    private readonly Func<Task> _reloadAsync;

    public FinanceTransactionFormViewModel(
        FinanceLedger ledger,
        FinanceAccountsViewModel accounts,
        IFinanceService financeService,
        IDialogService dialogService,
        Func<Task> reloadAsync)
    {
        _ledger = ledger;
        _accounts = accounts;
        _financeService = financeService;
        _dialogService = dialogService;
        _reloadAsync = reloadAsync;
    }

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

    partial void OnNewCategoryChanged(string value) => UpdateSuggestions(value);

    [ObservableProperty]
    private string _newNote = string.Empty;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

    /// <summary>Owning account chosen in the form.</summary>
    [ObservableProperty]
    private Guid? _newAccountId;

    public bool IsExpenseTypeSelected => NewTransactionType == TransactionType.Expense;
    public bool IsIncomeTypeSelected => NewTransactionType == TransactionType.Income;

    private List<string> _allCategories = new();
    public ObservableCollection<string> SuggestedCategories { get; } = new();

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

    [RelayCommand]
    private void SelectNewAccount(AccountItemViewModel item)
    {
        if (item == null) return;
        NewAccountId = item.Id;
        _accounts.UpdatePickerState(NewAccountId);
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
        NewAccountId = _ledger.SelectedAccountId ?? _ledger.DefaultAccountId;
        _accounts.UpdatePickerState(NewAccountId);
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
        NewAccountId = transaction.AccountId ?? _ledger.DefaultAccountId;
        _accounts.UpdatePickerState(NewAccountId);

        OnPropertyChanged(nameof(IsExpenseTypeSelected));
        OnPropertyChanged(nameof(IsIncomeTypeSelected));

        IsAddTransactionVisible = true;
        await LoadCategoriesForCurrentTypeAsync();
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
        if (!FinanceLedger.TryParseAmount(NewAmountText, out var amount)) return;

        FinanceTransaction transaction;
        if (_editingTransaction != null)
        {
            transaction = _editingTransaction;
            transaction.Type = NewTransactionType;
            transaction.Amount = amount;
            transaction.Category = NewCategory?.Trim() ?? string.Empty;
            transaction.Note = NewNote?.Trim() ?? string.Empty;
            transaction.Date = NewDate.Date;
            transaction.AccountId = NewAccountId ?? transaction.AccountId ?? _ledger.DefaultAccountId;
        }
        else
        {
            transaction = new FinanceTransaction
            {
                Type = NewTransactionType,
                Amount = amount,
                Category = NewCategory?.Trim() ?? string.Empty,
                Note = NewNote?.Trim() ?? string.Empty,
                Date = NewDate.Date,
                AccountId = NewAccountId ?? _ledger.DefaultAccountId
            };
        }

        if (!await WriteGate.TryAsync(() => _financeService.SaveFinanceTransactionAsync(transaction))) return;

        ResetForm();
        IsAddTransactionVisible = false;

        await _reloadAsync();
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
            if (!await WriteGate.TryAsync(() => _financeService.DeleteFinanceTransactionAsync(transaction.Id))) return;
            await _reloadAsync();
        }
    }
}
