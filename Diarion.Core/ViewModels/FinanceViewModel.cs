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
    private readonly IProfileService _profileService;

    /// <summary>
    /// The single account-filtered, date-sorted stream the page renders: transactions, transfers and
    /// occurrences awaiting confirmation, told apart by a template selector.
    /// </summary>
    public ObservableCollection<FinanceFeedItem> Feed { get; } = new();

    public ObservableCollection<BudgetItemViewModel> Budgets { get; } = new();

    // --- Accounts / wallets ---
    /// <summary>All transactions unfiltered; <see cref="Feed"/> shows the account-filtered view.</summary>
    private List<FinanceTransaction> _allTransactions = new();
    private List<Account> _accounts = new();
    private List<Transfer> _transfers = new();
    private List<PendingOccurrence> _pending = new();

    /// <summary>Accounts for the selector strip and the add-transaction picker.</summary>
    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    /// <summary>
    /// Archived accounts stay in <see cref="_accounts"/> because their opening balance and transactions
    /// still count towards the total; they are only hidden from the strip.
    /// </summary>
    private IEnumerable<Account> ActiveAccounts => _accounts.Where(a => !a.IsArchived);

    private IEnumerable<Account> VisibleAccounts => ShowArchivedAccounts ? _accounts : ActiveAccounts;

    private Guid? DefaultAccountId => (ActiveAccounts.FirstOrDefault() ?? _accounts.FirstOrDefault())?.Id;

    /// <summary>Reveals archived accounts in the strip so they can be inspected or unarchived.</summary>
    [ObservableProperty]
    private bool _showArchivedAccounts;

    /// <summary>Selected account for the strip filter; null = All accounts.</summary>
    [ObservableProperty]
    private Guid? _selectedAccountId;

    [ObservableProperty]
    private bool _isAllAccountsSelected = true;

    [ObservableProperty]
    private string _selectedAccountName = string.Empty;

    public bool HasAccounts => _accounts.Count > 0;

    // Account add/edit form
    [ObservableProperty]
    private bool _isAccountFormVisible;

    [ObservableProperty]
    private string _newAccountName = string.Empty;

    [ObservableProperty]
    private string _newAccountInitialBalanceText = string.Empty;

    [ObservableProperty]
    private string _newAccountIcon = "💳";

    [ObservableProperty]
    private string _newAccountColorHex = "#8FA083";

    private Account? _editingAccount;
    public bool IsEditingAccount => _editingAccount != null;

    /// <summary>True when the edited account is archived, so the form offers "unarchive" instead.</summary>
    public bool IsEditingArchivedAccount => _editingAccount?.IsArchived == true;

    public ObservableCollection<SelectableOptionViewModel> AvailableIcons { get; } =
        new(new[] { "💳", "💵", "🏦", "💰", "🐷", "💼" }.Select(v => new SelectableOptionViewModel { Value = v }));

    public ObservableCollection<SelectableOptionViewModel> AvailableColors { get; } =
        new(new[] { "#8FA083", "#C26D53", "#929FA7", "#C9985A", "#3D405B", "#6D8B74" }.Select(v => new SelectableOptionViewModel { Value = v }));

    /// <summary>Where the deleted account's transactions go; shown in the form only when it has any.</summary>
    public ObservableCollection<AccountItemViewModel> ReassignTargets { get; } = new();

    [ObservableProperty]
    private Guid? _reassignToAccountId;

    [ObservableProperty]
    private bool _hasReassignTargets;

    /// <summary>Owning account chosen in the add-transaction form.</summary>
    [ObservableProperty]
    private Guid? _newAccountId;

    // --- Transfers between accounts ---
    [ObservableProperty]
    private bool _isTransferFormVisible;

    // --- Planned (recurring) transactions ---
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

    [ObservableProperty]
    private string _newTransferAmountText = string.Empty;

    [ObservableProperty]
    private string _newTransferNote = string.Empty;

    [ObservableProperty]
    private DateTime _newTransferDate = DateTime.Today;

    [ObservableProperty]
    private string _transferError = string.Empty;

    [ObservableProperty]
    private bool _hasTransferError;

    public ObservableCollection<AccountItemViewModel> TransferFromOptions { get; } = new();
    public ObservableCollection<AccountItemViewModel> TransferToOptions { get; } = new();

    [ObservableProperty]
    private Guid? _transferFromAccountId;

    [ObservableProperty]
    private Guid? _transferToAccountId;

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

    public FinanceViewModel(IFinanceService financeService, IDialogService dialogService, IProfileService profileService)
    {
        _financeService = financeService;
        _dialogService = dialogService;
        _profileService = profileService;
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
        NewAccountId = SelectedAccountId ?? DefaultAccountId;
        UpdateAccountPickerState();
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
        NewAccountId = transaction.AccountId ?? DefaultAccountId;
        UpdateAccountPickerState();

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
            _accounts = await _financeService.GetAccountsAsync(includeArchived: true);

            var profile = await _profileService.GetUserProfileAsync();
            ShowBudgets = profile?.IsBudgetsEnabled ?? true;
            ShowPlanned = profile?.IsPlannedTransactionsEnabled ?? true;

            // Post what has come due before reading, so freshly materialized rows appear in this load
            // rather than only the next time the page is opened. Wrapped because this is the one thing
            // in LoadAsync that writes: every mutation command ends with a reload, so a planner failure
            // would otherwise blank the whole page after something as unrelated as saving a budget.
            _pending = new List<PendingOccurrence>();
            if (ShowPlanned)
            {
                try
                {
                    _pending = (await _financeService.ApplyDuePostingsAsync(DateTime.Today, DefaultAccountId)).Pending;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Recurring posting failed: {ex.Message}");
                }
            }

            _allTransactions = await _financeService.GetFinanceTransactionsAsync();
            _transfers = await _financeService.GetTransfersAsync();

            // Drop a stale selection if that account is gone or no longer shown in the strip.
            if (SelectedAccountId != null && !VisibleAccounts.Any(a => a.Id == SelectedAccountId))
            {
                SelectedAccountId = null;
            }

            BuildAccountsCollection();
            BuildFeed();
            CalculateBalances();
            UpdateAccountSelectionState();

            await LoadBudgetsAsync(_allTransactions);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowCategoryDetail(BudgetItemViewModel item)
    {
        if (item == null) return;

        var today = DateTime.Today;
        // Budgets are global (across all accounts), so the detail uses the unfiltered set.
        var expenses = _allTransactions
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

    private void BuildAccountsCollection()
    {
        Accounts.Clear();
        foreach (var a in VisibleAccounts)
        {
            Accounts.Add(new AccountItemViewModel
            {
                Id = a.Id,
                Name = AccountLocalization.ResolveName(a),
                Icon = a.Icon,
                ColorHex = a.ColorHex,
                IsArchived = a.IsArchived,
                BalanceText = AccountBalanceCalculator
                    .ComputeBalance(a, _allTransactions, _transfers)
                    .ToString("N2", System.Globalization.CultureInfo.CurrentCulture)
            });
        }
        OnPropertyChanged(nameof(HasAccounts));
    }

    private string ResolveAccountName(Guid id) =>
        AccountLocalization.ResolveName(_accounts.FirstOrDefault(a => a.Id == id));

    /// <summary>
    /// Rebuilds the one stream the page renders. The account filter applies to all three kinds:
    /// transactions by owner, transfers by either leg, pending occurrences by their rule's account.
    /// </summary>
    private void BuildFeed()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var items = new List<FinanceFeedItem>();

        var transactions = SelectedAccountId == null
            ? _allTransactions
            : _allTransactions.Where(t => t.AccountId == SelectedAccountId);

        foreach (var t in transactions)
        {
            items.Add(new TransactionFeedItem
            {
                Id = t.Id,
                Date = t.Date,
                CreatedAt = t.CreatedAt,
                Model = t,
                IsFromPlan = t.RecurringTransactionId != null
            });
        }

        var transfers = SelectedAccountId == null
            ? _transfers
            : _transfers.Where(t => t.FromAccountId == SelectedAccountId || t.ToAccountId == SelectedAccountId);

        foreach (var t in transfers)
        {
            items.Add(new TransferFeedItem
            {
                Id = t.Id,
                // Truncated here rather than on the model: Transfer.Date is the one finance date that
                // isn't date-only, and reshaping it is a schema-adjacent change with no coverage here.
                Date = t.Date.Date,
                CreatedAt = t.CreatedAt,
                FromName = ResolveAccountName(t.FromAccountId),
                ToName = ResolveAccountName(t.ToAccountId),
                AmountText = t.Amount.ToString("N2", culture),
                DateText = t.Date.ToString("dd.MM.yyyy", culture),
                Note = t.Note
            });
        }

        var pending = SelectedAccountId == null
            ? _pending
            : _pending.Where(p => p.Rule.AccountId == SelectedAccountId);

        foreach (var p in pending)
        {
            items.Add(new PlannedFeedItem
            {
                Id = p.Rule.Id,
                RuleId = p.RuleId,
                Date = p.Date,
                CreatedAt = p.Rule.CreatedAt,
                Category = p.Rule.Category,
                AmountText = p.Rule.Amount.ToString("N2", culture),
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

        HasPending = _pending.Count > 0;
    }

    private void UpdateAccountSelectionState()
    {
        IsAllAccountsSelected = SelectedAccountId == null;
        foreach (var a in Accounts)
        {
            a.IsSelected = a.Id == SelectedAccountId;
        }
        var selected = SelectedAccountId == null ? null : _accounts.FirstOrDefault(a => a.Id == SelectedAccountId);
        SelectedAccountName = selected == null
            ? Diarion.Resources.Localization.AppResources.AllAccounts
            : AccountLocalization.ResolveName(selected);
    }

    private void UpdateAccountPickerState()
    {
        foreach (var a in Accounts)
        {
            a.IsPickerSelected = a.Id == NewAccountId;
        }
    }

    private void CalculateBalances()
    {
        var selected = SelectedAccountId == null ? null : _accounts.FirstOrDefault(a => a.Id == SelectedAccountId);

        TotalBalance = selected == null
            ? AccountBalanceCalculator.ComputeTotal(_accounts, _allTransactions)
            : AccountBalanceCalculator.ComputeBalance(selected, _allTransactions, _transfers);

        var today = DateTime.Today;
        var monthTx = _allTransactions.Where(x => x.Date.Month == today.Month && x.Date.Year == today.Year);
        if (selected != null)
        {
            monthTx = monthTx.Where(x => x.AccountId == selected.Id);
        }
        var monthList = monthTx.ToList();

        MonthIncome = monthList.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
        MonthExpense = monthList.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
    }

    // --- Account selection & management ---

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

        if (string.IsNullOrWhiteSpace(NewRecurringAmountText) ||
            !decimal.TryParse(NewRecurringAmountText.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            RecurringError = Diarion.Resources.Localization.AppResources.TransferAmountError;
            return;
        }

        var start = NewRecurringStartDate.Date;
        var rule = new RecurringTransaction
        {
            Type = NewRecurringType,
            AccountId = NewAccountId ?? DefaultAccountId,
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
            // A start in the past is taken at its word and back-fills from there; the usual case, today,
            // means "from now on" and posts nothing retroactively.
            LastPostedThrough = start > DateTime.Today ? DateTime.Today : start.AddDays(-1)
        };

        await _financeService.SaveRecurringTransactionAsync(rule);

        IsRecurringFormVisible = false;
        await LoadAsync();
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

        await _financeService.DeleteRecurringTransactionAsync(item.RuleId, deletePostedTransactions: false);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ConfirmPlannedAsync(PlannedFeedItem item)
    {
        if (item == null) return;

        await _financeService.ConfirmOccurrenceAsync(item.RuleId, item.Date, DefaultAccountId);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SkipPlannedAsync(PlannedFeedItem item)
    {
        if (item == null) return;

        await _financeService.SkipOccurrenceAsync(item.RuleId, item.Date);
        await LoadAsync();
    }

    [RelayCommand]
    private void SelectAccount(AccountItemViewModel item)
    {
        if (item == null) return;
        SelectedAccountId = item.Id;
        BuildFeed();
        CalculateBalances();
        UpdateAccountSelectionState();
    }

    [RelayCommand]
    private void SelectAllAccounts()
    {
        SelectedAccountId = null;
        BuildFeed();
        CalculateBalances();
        UpdateAccountSelectionState();
    }

    [RelayCommand]
    private void SelectNewAccount(AccountItemViewModel item)
    {
        if (item == null) return;
        NewAccountId = item.Id;
        UpdateAccountPickerState();
    }

    [RelayCommand]
    private void ShowAccountForm()
    {
        SetEditingAccount(null);
        NewAccountName = string.Empty;
        NewAccountInitialBalanceText = string.Empty;
        NewAccountIcon = AvailableIcons[0].Value;
        NewAccountColorHex = AvailableColors[0].Value;
        UpdateOptionSelectionState();
        BuildReassignTargets();
        IsAccountFormVisible = true;
    }

    [RelayCommand]
    private void EditSelectedAccount()
    {
        if (SelectedAccountId == null) return;
        var acc = _accounts.FirstOrDefault(a => a.Id == SelectedAccountId);
        if (acc == null) return;

        SetEditingAccount(acc);
        NewAccountName = AccountLocalization.ResolveName(acc);
        NewAccountInitialBalanceText = acc.InitialBalance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        NewAccountIcon = acc.Icon;
        NewAccountColorHex = acc.ColorHex;
        UpdateOptionSelectionState();
        BuildReassignTargets();
        IsAccountFormVisible = true;
    }

    [RelayCommand]
    private void HideAccountForm()
    {
        IsAccountFormVisible = false;
        SetEditingAccount(null);
    }

    private void SetEditingAccount(Account? account)
    {
        _editingAccount = account;
        OnPropertyChanged(nameof(IsEditingAccount));
        OnPropertyChanged(nameof(IsEditingArchivedAccount));
    }

    /// <summary>
    /// Offers every other account as the destination for the edited account's transactions. Only
    /// meaningful while editing — creating an account has nothing to reassign.
    /// </summary>
    private void BuildReassignTargets()
    {
        ReassignTargets.Clear();

        var editingId = _editingAccount?.Id;
        var hasTransactions = editingId != null && _allTransactions.Any(t => t.AccountId == editingId);

        if (editingId != null && hasTransactions)
        {
            foreach (var a in _accounts.Where(a => a.Id != editingId))
            {
                ReassignTargets.Add(new AccountItemViewModel
                {
                    Id = a.Id,
                    Name = AccountLocalization.ResolveName(a),
                    Icon = a.Icon,
                    ColorHex = a.ColorHex,
                    IsArchived = a.IsArchived
                });
            }
        }

        HasReassignTargets = ReassignTargets.Count > 0;
        ReassignToAccountId = ReassignTargets.FirstOrDefault()?.Id;
        UpdateReassignSelectionState();
    }

    private void UpdateReassignSelectionState()
    {
        foreach (var a in ReassignTargets)
        {
            a.IsPickerSelected = a.Id == ReassignToAccountId;
        }
    }

    [RelayCommand]
    private void SelectReassignTarget(AccountItemViewModel item)
    {
        if (item == null) return;
        ReassignToAccountId = item.Id;
        UpdateReassignSelectionState();
    }

    private void UpdateOptionSelectionState()
    {
        foreach (var o in AvailableIcons) o.IsSelected = o.Value == NewAccountIcon;
        foreach (var o in AvailableColors) o.IsSelected = o.Value == NewAccountColorHex;
    }

    [RelayCommand]
    private void SelectAccountIcon(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return;
        NewAccountIcon = icon;
        UpdateOptionSelectionState();
    }

    [RelayCommand]
    private void SelectAccountColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return;
        NewAccountColorHex = hex;
        UpdateOptionSelectionState();
    }

    [RelayCommand]
    private async Task SaveAccountAsync()
    {
        var name = (NewAccountName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        decimal.TryParse((NewAccountInitialBalanceText ?? string.Empty).Replace(",", "."),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal initial);

        var account = _editingAccount ?? new Account();
        account.Name = name;
        // The user owns the name from here on, so stop resolving it from resources.
        account.ResourceKey = null;
        account.InitialBalance = initial;
        account.Icon = string.IsNullOrEmpty(NewAccountIcon) ? "💳" : NewAccountIcon;
        account.ColorHex = string.IsNullOrEmpty(NewAccountColorHex) ? "#8FA083" : NewAccountColorHex;

        await _financeService.SaveAccountAsync(account);

        IsAccountFormVisible = false;
        SetEditingAccount(null);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleArchiveAccountAsync()
    {
        if (_editingAccount == null) return;

        if (!_editingAccount.IsArchived && ActiveAccounts.Count() <= 1)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AccountFormTitle,
                Diarion.Resources.Localization.AppResources.ArchiveAccountLastError);
            return;
        }

        _editingAccount.IsArchived = !_editingAccount.IsArchived;
        await _financeService.SaveAccountAsync(_editingAccount);

        if (_editingAccount.IsArchived && SelectedAccountId == _editingAccount.Id)
        {
            SelectedAccountId = null;
        }

        IsAccountFormVisible = false;
        SetEditingAccount(null);
        await LoadAsync();
    }

    [RelayCommand]
    private void ToggleShowArchivedAccounts()
    {
        ShowArchivedAccounts = !ShowArchivedAccounts;

        if (!ShowArchivedAccounts && SelectedAccountId != null
            && !ActiveAccounts.Any(a => a.Id == SelectedAccountId))
        {
            SelectedAccountId = null;
        }

        BuildAccountsCollection();
        BuildFeed();
        CalculateBalances();
        UpdateAccountSelectionState();
    }

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        if (_editingAccount == null) return;

        if (_accounts.Count <= 1)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.DeleteConfirmTitle,
                Diarion.Resources.Localization.AppResources.DeleteAccountLastError);
            return;
        }

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle,
            Diarion.Resources.Localization.AppResources.DeleteAccountConfirm,
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
        if (!confirm) return;

        var deletedId = _editingAccount.Id;
        var reassignTo = ReassignToAccountId is Guid chosen && chosen != deletedId
            ? chosen
            : _accounts.First(a => a.Id != deletedId).Id;

        await _financeService.DeleteAccountAsync(deletedId, reassignTo);

        if (SelectedAccountId == deletedId) SelectedAccountId = null;

        IsAccountFormVisible = false;
        SetEditingAccount(null);
        await LoadAsync();
    }

    // --- Transfers ---

    [RelayCommand]
    private void ShowTransferForm()
    {
        NewTransferAmountText = string.Empty;
        NewTransferNote = string.Empty;
        NewTransferDate = DateTime.Today;
        TransferError = string.Empty;
        HasTransferError = false;

        var active = ActiveAccounts.ToList();
        TransferFromAccountId = SelectedAccountId ?? active.FirstOrDefault()?.Id;
        TransferToAccountId = active.FirstOrDefault(a => a.Id != TransferFromAccountId)?.Id;

        BuildTransferOptions();
        IsTransferFormVisible = true;
    }

    [RelayCommand]
    private void HideTransferForm()
    {
        IsTransferFormVisible = false;
        HasTransferError = false;
        TransferError = string.Empty;
    }

    private void BuildTransferOptions()
    {
        TransferFromOptions.Clear();
        TransferToOptions.Clear();

        foreach (var a in ActiveAccounts)
        {
            var name = AccountLocalization.ResolveName(a);
            TransferFromOptions.Add(new AccountItemViewModel
            {
                Id = a.Id,
                Name = name,
                Icon = a.Icon,
                ColorHex = a.ColorHex,
                IsPickerSelected = a.Id == TransferFromAccountId
            });
            TransferToOptions.Add(new AccountItemViewModel
            {
                Id = a.Id,
                Name = name,
                Icon = a.Icon,
                ColorHex = a.ColorHex,
                IsPickerSelected = a.Id == TransferToAccountId
            });
        }
    }

    [RelayCommand]
    private void SelectTransferFrom(AccountItemViewModel item)
    {
        if (item == null) return;
        TransferFromAccountId = item.Id;
        foreach (var a in TransferFromOptions) a.IsPickerSelected = a.Id == TransferFromAccountId;
    }

    [RelayCommand]
    private void SelectTransferTo(AccountItemViewModel item)
    {
        if (item == null) return;
        TransferToAccountId = item.Id;
        foreach (var a in TransferToOptions) a.IsPickerSelected = a.Id == TransferToAccountId;
    }

    [RelayCommand]
    private async Task SaveTransferAsync()
    {
        HasTransferError = false;
        TransferError = string.Empty;

        if (TransferFromAccountId == null || TransferToAccountId == null
            || TransferFromAccountId == TransferToAccountId)
        {
            TransferError = Diarion.Resources.Localization.AppResources.TransferSameAccountError;
            HasTransferError = true;
            return;
        }

        if (!decimal.TryParse((NewTransferAmountText ?? string.Empty).Replace(",", "."),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture,
                out decimal amount) || amount <= 0)
        {
            TransferError = Diarion.Resources.Localization.AppResources.TransferAmountError;
            HasTransferError = true;
            return;
        }

        await _financeService.SaveTransferAsync(new Transfer
        {
            FromAccountId = TransferFromAccountId.Value,
            ToAccountId = TransferToAccountId.Value,
            Amount = amount,
            Note = NewTransferNote?.Trim() ?? string.Empty,
            Date = NewTransferDate.Date
        });

        IsTransferFormVisible = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteTransferAsync(TransferFeedItem item)
    {
        if (item == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle,
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg,
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
        if (!confirm) return;

        await _financeService.DeleteTransferAsync(item.Id);
        await LoadAsync();
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
            transaction.AccountId = NewAccountId ?? transaction.AccountId ?? DefaultAccountId;
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
                AccountId = NewAccountId ?? DefaultAccountId
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

        IsCategoryDetailVisible = false;

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

/// <summary>An account chip in the selector strip and the add-transaction picker.</summary>
public partial class AccountItemViewModel : ObservableObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public bool IsArchived { get; set; }

    [ObservableProperty]
    private string _balanceText = string.Empty;

    /// <summary>Selected in the account-filter strip.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Selected in the add-transaction account picker.</summary>
    [ObservableProperty]
    private bool _isPickerSelected;
}

/// <summary>
/// One choice in a swatch strip (account icon or colour). Carries its own selection flag because the
/// page highlights selection with DataTriggers, which need a bool on the bound item.
/// </summary>
public partial class SelectableOptionViewModel : ObservableObject
{
    public string Value { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

