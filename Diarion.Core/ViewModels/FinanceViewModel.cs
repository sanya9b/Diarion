using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels.Finance;

namespace Diarion.ViewModels;

/// <summary>
/// The finance page. Owns the load, the single feed the page renders and the balances; everything
/// else is a section with its own view model, handed the same <see cref="FinanceLedger"/> and a way
/// to ask for a reload after it writes. The sections never fetch for themselves, so a load is the one
/// place the page's picture of the ledger changes.
/// </summary>
public partial class FinanceViewModel : BaseViewModel
{
    private readonly IFinanceService _financeService;
    private readonly IProfileService _profileService;
    private readonly FinanceLedger _ledger = new();

    public FinanceViewModel(IFinanceService financeService, IDialogService dialogService, IProfileService profileService)
    {
        _financeService = financeService;
        _profileService = profileService;
        Title = Diarion.Resources.Localization.AppResources.FinanceTitle ?? "Income/Expenses";

        Accounts = new FinanceAccountsViewModel(_ledger, financeService, dialogService, LoadAsync, RefreshDerived);
        Transfers = new FinanceTransferViewModel(_ledger, financeService, dialogService, LoadAsync);
        Planned = new FinancePlannedViewModel(_ledger, financeService, dialogService, LoadAsync);
        Budgets = new FinanceBudgetsViewModel(_ledger, financeService, dialogService, LoadAsync);
        TransactionForm = new FinanceTransactionFormViewModel(_ledger, Accounts, financeService, dialogService, LoadAsync);
    }

    /// <summary>The account strip, the filter it holds and the account form.</summary>
    public FinanceAccountsViewModel Accounts { get; }

    /// <summary>The transfer-between-accounts form.</summary>
    public FinanceTransferViewModel Transfers { get; }

    /// <summary>Planned (recurring) transactions: the rule form and pending occurrences.</summary>
    public FinancePlannedViewModel Planned { get; }

    /// <summary>Monthly budgets, their category detail and the budget form.</summary>
    public FinanceBudgetsViewModel Budgets { get; }

    /// <summary>The add/edit transaction form.</summary>
    public FinanceTransactionFormViewModel TransactionForm { get; }

    /// <summary>
    /// The single account-filtered, date-sorted stream the page renders: transactions, transfers and
    /// occurrences awaiting confirmation, told apart by a template selector.
    /// </summary>
    public ObservableCollection<FinanceFeedItem> Feed { get; } = new();

    /// <summary>
    /// The feed has been built and holds nothing for the current filter. False until the first load so
    /// the empty-state line does not flash under the header while the ledger is still being read.
    /// </summary>
    [ObservableProperty]
    private bool _isFeedEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalBalanceText))]
    private decimal _totalBalance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthIncomeText))]
    private decimal _monthIncome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthExpenseText))]
    private decimal _monthExpense;

    /// <summary>
    /// The currency every amount on this page is rendered in. Mirrored onto the ledger so the sections
    /// format with the same code the balances do.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalBalanceText))]
    [NotifyPropertyChangedFor(nameof(MonthIncomeText))]
    [NotifyPropertyChangedFor(nameof(MonthExpenseText))]
    private string _currencyCode = MoneyFormatter.FallbackCode;

    partial void OnCurrencyCodeChanged(string value) => _ledger.CurrencyCode = value;

    public string TotalBalanceText => _ledger.Money(TotalBalance);

    // The sign lives here rather than in the XAML format string, because only here is it possible to
    // tell "nothing came in" from "money came in". A prefixed zero read as -0,00, which announces a
    // direction that did not happen.
    public string MonthIncomeText => Signed(MonthIncome, "+");
    public string MonthExpenseText => Signed(MonthExpense, "-");

    private string Signed(decimal amount, string sign)
        => amount == 0 ? _ledger.Money(0) : sign + _ledger.Money(amount);

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _ledger.Accounts = await _financeService.GetAccountsAsync(includeArchived: true);

            var profile = await _profileService.GetUserProfileAsync();
            Budgets.ShowBudgets = profile?.IsBudgetsEnabled ?? true;
            CurrencyCode = profile?.GetEffectiveCurrencyCode() ?? MoneyFormatter.FallbackCode;
            Planned.ShowPlanned = profile?.IsPlannedTransactionsEnabled ?? true;

            // Post what has come due before reading, so freshly materialized rows appear in this load
            // rather than only the next time the page is opened. Wrapped because this is the one thing
            // in LoadAsync that writes: every mutation command ends with a reload, so a planner failure
            // would otherwise blank the whole page after something as unrelated as saving a budget.
            _ledger.Pending = new List<PendingOccurrence>();
            if (Planned.ShowPlanned)
            {
                try
                {
                    _ledger.Pending = (await _financeService.ApplyDuePostingsAsync(DateTime.Today, _ledger.DefaultAccountId)).Pending;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Recurring posting failed: {ex.Message}");
                }
            }

            _ledger.Transactions = await _financeService.GetFinanceTransactionsAsync();
            _ledger.Transfers = await _financeService.GetTransfersAsync();

            Accounts.DropStaleSelection();
            Accounts.Rebuild();
            RefreshDerived();
            Planned.Refresh();

            await Budgets.LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Everything that follows the account filter: the feed and the three balance figures.</summary>
    private void RefreshDerived()
    {
        BuildFeed();
        CalculateBalances();
    }

    /// <summary>
    /// Rebuilds the one stream the page renders. The account filter applies to all three kinds:
    /// transactions by owner, transfers by either leg, pending occurrences by their rule's account.
    /// </summary>
    private void BuildFeed()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var selected = _ledger.SelectedAccountId;
        var items = new List<FinanceFeedItem>();

        var transactions = selected == null
            ? _ledger.Transactions
            : _ledger.Transactions.Where(t => t.AccountId == selected);

        foreach (var t in transactions)
        {
            items.Add(new TransactionFeedItem
            {
                Id = t.Id,
                Date = t.Date,
                CreatedAt = t.CreatedAt,
                Model = t,
                AmountText = _ledger.Money(t.Amount),
                IsFromPlan = t.RecurringTransactionId != null
            });
        }

        var transfers = selected == null
            ? _ledger.Transfers
            : _ledger.Transfers.Where(t => t.FromAccountId == selected || t.ToAccountId == selected);

        foreach (var t in transfers)
        {
            items.Add(new TransferFeedItem
            {
                Id = t.Id,
                // Truncated here rather than on the model: Transfer.Date is the one finance date that
                // isn't date-only, and reshaping it is a schema-adjacent change with no coverage here.
                Date = t.Date.Date,
                CreatedAt = t.CreatedAt,
                FromName = _ledger.ResolveAccountName(t.FromAccountId),
                ToName = _ledger.ResolveAccountName(t.ToAccountId),
                AmountText = _ledger.Money(t.Amount),
                DateText = t.Date.ToString("dd.MM.yyyy", culture),
                Note = t.Note
            });
        }

        var pending = selected == null
            ? _ledger.Pending
            : _ledger.Pending.Where(p => p.Rule.AccountId == selected);

        foreach (var p in pending)
        {
            items.Add(new PlannedFeedItem
            {
                Id = p.Rule.Id,
                RuleId = p.RuleId,
                Date = p.Date,
                CreatedAt = p.Rule.CreatedAt,
                Category = p.Rule.Category,
                AmountText = _ledger.Money(p.Rule.Amount),
                RecurrenceText = RecurrenceFormatter.Describe(p.Rule.Recurrence),
                DateText = p.Date.ToString("dd.MM.yyyy", culture),
                IsExpense = p.Rule.Type == TransactionType.Expense
            });
        }

        Feed.Clear();
        foreach (var item in items
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.SortRank)
            .ThenByDescending(i => i.CreatedAt))
        {
            Feed.Add(item);
        }

        IsFeedEmpty = Feed.Count == 0;
    }

    private void CalculateBalances()
    {
        var selected = _ledger.Find(_ledger.SelectedAccountId);

        TotalBalance = selected == null
            ? AccountBalanceCalculator.ComputeTotal(_ledger.Accounts, _ledger.Transactions)
            : AccountBalanceCalculator.ComputeBalance(selected, _ledger.Transactions, _ledger.Transfers);

        var today = DateTime.Today;
        var monthTx = _ledger.Transactions.Where(x => x.Date.Month == today.Month && x.Date.Year == today.Year);
        if (selected != null)
        {
            monthTx = monthTx.Where(x => x.AccountId == selected.Id);
        }
        var monthList = monthTx.ToList();

        MonthIncome = monthList.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
        MonthExpense = monthList.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
    }
}
