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
/// The account strip and the account form: which account the page is filtered to, and creating,
/// editing, archiving and deleting accounts. Owns the selection; the page owner is told through
/// <c>refresh</c> whenever it changes so the feed and the balances can follow.
/// </summary>
public partial class FinanceAccountsViewModel : ObservableObject
{
    private readonly FinanceLedger _ledger;
    private readonly IFinanceService _financeService;
    private readonly IDialogService _dialogService;
    private readonly Func<Task> _reloadAsync;
    private readonly Action _refresh;

    public FinanceAccountsViewModel(
        FinanceLedger ledger,
        IFinanceService financeService,
        IDialogService dialogService,
        Func<Task> reloadAsync,
        Action refresh)
    {
        _ledger = ledger;
        _financeService = financeService;
        _dialogService = dialogService;
        _reloadAsync = reloadAsync;
        _refresh = refresh;
    }

    /// <summary>Accounts for the selector strip and the add-transaction picker.</summary>
    public ObservableCollection<AccountItemViewModel> Items { get; } = new();

    /// <summary>Reveals archived accounts in the strip so they can be inspected or unarchived.</summary>
    [ObservableProperty]
    private bool _showArchivedAccounts;

    /// <summary>Selected account for the strip filter; null = All accounts. Lives on the ledger so the forms can read it.</summary>
    public Guid? SelectedAccountId
    {
        get => _ledger.SelectedAccountId;
        private set
        {
            if (_ledger.SelectedAccountId == value) return;
            _ledger.SelectedAccountId = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private bool _isAllAccountsSelected = true;

    [ObservableProperty]
    private string _selectedAccountName = string.Empty;

    public bool HasAccounts => _ledger.Accounts.Count > 0;

    internal IEnumerable<Account> VisibleAccounts => ShowArchivedAccounts ? _ledger.Accounts : _ledger.ActiveAccounts;

    // --- Account add/edit form ---

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

    // --- Called by the owner around a load ---

    /// <summary>Drops a stale selection if that account is gone or no longer shown in the strip.</summary>
    internal void DropStaleSelection()
    {
        if (SelectedAccountId != null && !VisibleAccounts.Any(a => a.Id == SelectedAccountId))
        {
            SelectedAccountId = null;
        }
    }

    /// <summary>Rebuilds the strip from the ledger and re-applies the selection highlight.</summary>
    internal void Rebuild()
    {
        Items.Clear();
        foreach (var a in VisibleAccounts)
        {
            Items.Add(new AccountItemViewModel
            {
                Id = a.Id,
                Name = AccountLocalization.ResolveName(a),
                Icon = a.Icon,
                ColorHex = a.ColorHex,
                IsArchived = a.IsArchived,
                BalanceText = _ledger.Money(AccountBalanceCalculator
                    .ComputeBalance(a, _ledger.Transactions, _ledger.Transfers))
            });
        }
        OnPropertyChanged(nameof(HasAccounts));
        UpdateSelectionState();
    }

    /// <summary>The add-transaction form shares the strip's items as its account picker.</summary>
    internal void UpdatePickerState(Guid? pickedAccountId)
    {
        foreach (var a in Items)
        {
            a.IsPickerSelected = a.Id == pickedAccountId;
        }
    }

    private void UpdateSelectionState()
    {
        IsAllAccountsSelected = SelectedAccountId == null;
        foreach (var a in Items)
        {
            a.IsSelected = a.Id == SelectedAccountId;
        }
        var selected = _ledger.Find(SelectedAccountId);
        SelectedAccountName = selected == null
            ? Diarion.Resources.Localization.AppResources.AllAccounts
            : AccountLocalization.ResolveName(selected);
    }

    private void ApplySelection(Guid? accountId)
    {
        SelectedAccountId = accountId;
        _refresh();
        UpdateSelectionState();
    }

    // --- Selection ---

    [RelayCommand]
    private void SelectAccount(AccountItemViewModel item)
    {
        if (item == null) return;
        ApplySelection(item.Id);
    }

    [RelayCommand]
    private void SelectAllAccounts() => ApplySelection(null);

    [RelayCommand]
    private void ToggleShowArchivedAccounts()
    {
        ShowArchivedAccounts = !ShowArchivedAccounts;

        if (!ShowArchivedAccounts && SelectedAccountId != null
            && !_ledger.ActiveAccounts.Any(a => a.Id == SelectedAccountId))
        {
            SelectedAccountId = null;
        }

        Rebuild();
        _refresh();
    }

    // --- Form ---

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
        var acc = _ledger.Find(SelectedAccountId);
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
        var hasTransactions = editingId != null && _ledger.Transactions.Any(t => t.AccountId == editingId);

        if (editingId != null && hasTransactions)
        {
            foreach (var a in _ledger.Accounts.Where(a => a.Id != editingId))
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

        // Unlike an amount, an opening balance may be zero or negative, and an unreadable one is
        // simply taken as zero — the account is still worth creating.
        decimal.TryParse((NewAccountInitialBalanceText ?? string.Empty).Replace(",", "."),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal initial);

        var account = _editingAccount ?? new Account();
        account.Name = name;
        // The user owns the name from here on, so stop resolving it from resources.
        account.ResourceKey = null;
        account.InitialBalance = initial;
        account.Icon = string.IsNullOrEmpty(NewAccountIcon) ? "💳" : NewAccountIcon;
        account.ColorHex = string.IsNullOrEmpty(NewAccountColorHex) ? "#8FA083" : NewAccountColorHex;

        if (!await WriteGate.TryAsync(() => _financeService.SaveAccountAsync(account))) return;

        IsAccountFormVisible = false;
        SetEditingAccount(null);
        await _reloadAsync();
    }

    [RelayCommand]
    private async Task ToggleArchiveAccountAsync()
    {
        if (_editingAccount == null) return;

        if (!_editingAccount.IsArchived && _ledger.ActiveAccounts.Count() <= 1)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.AccountFormTitle,
                Diarion.Resources.Localization.AppResources.ArchiveAccountLastError);
            return;
        }

        _editingAccount.IsArchived = !_editingAccount.IsArchived;
        if (!await WriteGate.TryAsync(() => _financeService.SaveAccountAsync(_editingAccount))) return;

        if (_editingAccount.IsArchived && SelectedAccountId == _editingAccount.Id)
        {
            SelectedAccountId = null;
        }

        IsAccountFormVisible = false;
        SetEditingAccount(null);
        await _reloadAsync();
    }

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        if (_editingAccount == null) return;

        if (_ledger.Accounts.Count <= 1)
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
            : _ledger.Accounts.First(a => a.Id != deletedId).Id;

        if (!await WriteGate.TryAsync(() => _financeService.DeleteAccountAsync(deletedId, reassignTo))) return;

        if (SelectedAccountId == deletedId) SelectedAccountId = null;

        IsAccountFormVisible = false;
        SetEditingAccount(null);
        await _reloadAsync();
    }
}
