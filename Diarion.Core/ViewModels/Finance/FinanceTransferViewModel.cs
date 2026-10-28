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

/// <summary>The transfer-between-accounts form and the deletion of a transfer from the feed.</summary>
public partial class FinanceTransferViewModel : ObservableObject
{
    private readonly FinanceLedger _ledger;
    private readonly IFinanceService _financeService;
    private readonly IDialogService _dialogService;
    private readonly Func<Task> _reloadAsync;

    public FinanceTransferViewModel(
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

    /// <summary>
    /// A transfer needs two active accounts. With one, the form opened onto a "to" picker with nobody
    /// to pick and a validation error as the only way out; the entry point hides instead.
    /// </summary>
    [ObservableProperty]
    private bool _canTransfer;

    /// <summary>Called by the owner once the ledger holds this load's accounts.</summary>
    internal void Refresh() => CanTransfer = _ledger.ActiveAccounts.Count() >= 2;

    [ObservableProperty]
    private bool _isTransferFormVisible;

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

    [RelayCommand]
    private void ShowTransferForm()
    {
        NewTransferAmountText = string.Empty;
        NewTransferNote = string.Empty;
        NewTransferDate = DateTime.Today;
        TransferError = string.Empty;
        HasTransferError = false;

        var active = _ledger.ActiveAccounts.ToList();
        TransferFromAccountId = _ledger.SelectedAccountId ?? active.FirstOrDefault()?.Id;
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

        foreach (var a in _ledger.ActiveAccounts)
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

        if (!FinanceLedger.TryParseAmount(NewTransferAmountText, out var amount))
        {
            TransferError = Diarion.Resources.Localization.AppResources.TransferAmountError;
            HasTransferError = true;
            return;
        }

        if (!await WriteGate.TryAsync(() => _financeService.SaveTransferAsync(new Transfer
        {
            FromAccountId = TransferFromAccountId.Value,
            ToAccountId = TransferToAccountId.Value,
            Amount = amount,
            Note = NewTransferNote?.Trim() ?? string.Empty,
            Date = NewTransferDate.Date
        }))) return;

        IsTransferFormVisible = false;
        await _reloadAsync();
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

        if (!await WriteGate.TryAsync(() => _financeService.DeleteTransferAsync(item.Id))) return;
        await _reloadAsync();
    }
}
