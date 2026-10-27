using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels.Finance;

/// <summary>
/// Planned (recurring) transactions: the rule form, and confirming or skipping an occurrence that is
/// waiting in the feed. The occurrences themselves are rows of the owner's feed.
/// </summary>
public partial class FinancePlannedViewModel : ObservableObject
{
    private readonly FinanceLedger _ledger;
    private readonly IFinanceService _financeService;
    private readonly IDialogService _dialogService;
    private readonly Func<Task> _reloadAsync;

    public FinancePlannedViewModel(
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

    /// <summary>At least one occurrence is waiting to be confirmed or skipped.</summary>
    [ObservableProperty]
    private bool _hasPending;

    /// <summary>Feature toggle. Off, nothing posts and the entry point is hidden, but rules are kept.</summary>
    [ObservableProperty]
    private bool _showPlanned = true;

    [ObservableProperty]
    private bool _isRecurringFormVisible;

    [ObservableProperty]
    private string _newRecurringAmountText = string.Empty;

    [ObservableProperty]
    private string _newRecurringCategory = string.Empty;

    [ObservableProperty]
    private string _newRecurringNote = string.Empty;

    [ObservableProperty]
    private TransactionType _newRecurringType = TransactionType.Expense;

    public bool IsRecurringExpenseSelected => NewRecurringType == TransactionType.Expense;
    public bool IsRecurringIncomeSelected => NewRecurringType == TransactionType.Income;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecurringMonthly))]
    [NotifyPropertyChangedFor(nameof(IsRecurringWeekly))]
    [NotifyPropertyChangedFor(nameof(IsRecurringDaily))]
    private RecurrenceKind _newRecurringKind = RecurrenceKind.MonthlyByDay;

    public bool IsRecurringMonthly => NewRecurringKind == RecurrenceKind.MonthlyByDay;
    public bool IsRecurringWeekly => NewRecurringKind == RecurrenceKind.Weekly;
    public bool IsRecurringDaily => NewRecurringKind == RecurrenceKind.Daily;

    [ObservableProperty]
    private DateTime _newRecurringStartDate = DateTime.Today;

    [ObservableProperty]
    private bool _newRecurringAutoPost = true;

    [ObservableProperty]
    private string _recurringError = string.Empty;

    public bool HasRecurringError => !string.IsNullOrWhiteSpace(RecurringError);

    partial void OnRecurringErrorChanged(string value) => OnPropertyChanged(nameof(HasRecurringError));

    /// <summary>Called by the owner once the ledger holds this load's pending occurrences.</summary>
    internal void Refresh() => HasPending = _ledger.Pending.Count > 0;

    [RelayCommand]
    private void ShowRecurringForm()
    {
        RecurringError = string.Empty;
        NewRecurringAmountText = string.Empty;
        NewRecurringCategory = string.Empty;
        NewRecurringNote = string.Empty;
        NewRecurringType = TransactionType.Expense;
        NewRecurringKind = RecurrenceKind.MonthlyByDay;
        NewRecurringStartDate = DateTime.Today;
        NewRecurringAutoPost = true;
        OnPropertyChanged(nameof(IsRecurringExpenseSelected));
        OnPropertyChanged(nameof(IsRecurringIncomeSelected));
        IsRecurringFormVisible = true;
    }

    [RelayCommand]
    private void HideRecurringForm() => IsRecurringFormVisible = false;

    [RelayCommand]
    private void SetRecurringType(string typeText)
    {
        if (!Enum.TryParse<TransactionType>(typeText, out var type)) return;

        NewRecurringType = type;
        OnPropertyChanged(nameof(IsRecurringExpenseSelected));
        OnPropertyChanged(nameof(IsRecurringIncomeSelected));
    }

    [RelayCommand]
    private void SetRecurringKind(string kindText)
    {
        if (Enum.TryParse<RecurrenceKind>(kindText, out var kind))
        {
            NewRecurringKind = kind;
        }
    }

    [RelayCommand]
    private async Task SaveRecurringRuleAsync()
    {
        RecurringError = string.Empty;

        if (!FinanceLedger.TryParseAmount(NewRecurringAmountText, out var amount))
        {
            RecurringError = Diarion.Resources.Localization.AppResources.TransferAmountError;
            return;
        }

        var start = NewRecurringStartDate.Date;
        var rule = new RecurringTransaction
        {
            Type = NewRecurringType,
            // The rule form has no account picker of its own: it goes to the account the page is
            // filtered to, or the default one. (It used to read the add-transaction form's picker,
            // which the user may never have opened — the same answer in practice, reached by accident.)
            AccountId = _ledger.SelectedAccountId ?? _ledger.DefaultAccountId,
            Amount = amount,
            Category = (NewRecurringCategory ?? string.Empty).Trim(),
            Note = (NewRecurringNote ?? string.Empty).Trim(),
            AutoPost = NewRecurringAutoPost,
            Recurrence = new RecurrenceRule
            {
                Kind = NewRecurringKind,
                Anchor = start,
                DayOfMonth = start.Day,
                DaysOfWeek = new List<int> { (int)start.DayOfWeek }
            },
            // A start in the past is taken at its word and back-fills from there. Anything else — the
            // usual case of starting today — means "from now on": today counts as already dealt with, so
            // the first post is the next occurrence. Reading it the other way would guess that this
            // month's rent has not been entered yet, and guessing wrong writes money into the ledger.
            LastPostedThrough = start < DateTime.Today ? start.AddDays(-1) : DateTime.Today
        };

        if (!await WriteGate.TryAsync(() => _financeService.SaveRecurringTransactionAsync(rule))) return;

        IsRecurringFormVisible = false;
        await _reloadAsync();
    }

    [RelayCommand]
    private async Task DeleteRecurringRuleAsync(PlannedFeedItem item)
    {
        if (item == null) return;

        var confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle,
            Diarion.Resources.Localization.AppResources.DeletePlannedConfirm,
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);

        if (!confirm) return;

        if (!await WriteGate.TryAsync(() => _financeService.DeleteRecurringTransactionAsync(item.RuleId, deletePostedTransactions: false))) return;
        await _reloadAsync();
    }

    [RelayCommand]
    private async Task ConfirmPlannedAsync(PlannedFeedItem item)
    {
        if (item == null) return;

        if (!await WriteGate.TryAsync(() => _financeService.ConfirmOccurrenceAsync(item.RuleId, item.Date, _ledger.DefaultAccountId))) return;
        await _reloadAsync();
    }

    [RelayCommand]
    private async Task SkipPlannedAsync(PlannedFeedItem item)
    {
        if (item == null) return;

        if (!await WriteGate.TryAsync(() => _financeService.SkipOccurrenceAsync(item.RuleId, item.Date))) return;
        await _reloadAsync();
    }
}
