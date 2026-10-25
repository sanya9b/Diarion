using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels;

public partial class WishlistViewModel : BaseViewModel
{
    private readonly IWishlistService _wishlistService;

    public ObservableCollection<WishlistEntry> Entries { get; } = new();

    [ObservableProperty]
    private string _newWantText = string.Empty;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

    [ObservableProperty]
    private bool _isAddFormVisible;

    [ObservableProperty]
    private WishlistEntry? _selectedEntry;

    public bool IsEditing => SelectedEntry != null;

    private readonly IDialogService _dialogService;

    public WishlistViewModel(IWishlistService wishlistService, IDialogService dialogService)
    {
        _wishlistService = wishlistService;
        _dialogService = dialogService;
        Title = Diarion.Resources.Localization.AppResources.WishlistTitle ?? "Wishlist";
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _wishlistService.GetWishlistEntriesAsync();
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenAddForm(WishlistEntry? entry = null)
    {
        SelectedEntry = entry;
        if (entry != null)
        {
            NewWantText = entry.WantText;
            NewDate = entry.Date;
        }
        else
        {
            NewWantText = string.Empty;
            NewDate = DateTime.Today;
        }
        OnPropertyChanged(nameof(IsEditing));
        IsAddFormVisible = true;
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsAddFormVisible = false;
        SelectedEntry = null;
        OnPropertyChanged(nameof(IsEditing));
    }

    [RelayCommand]
    private async Task SaveEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWantText))
        {
            return;
        }

        var entry = SelectedEntry ?? new WishlistEntry();
        entry.WantText = NewWantText.Trim();
        entry.Date = NewDate.Date;

        if (!await WriteGate.TryAsync(() => _wishlistService.SaveWishlistEntryAsync(entry))) return;
        
        CloseForm();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleCompletedAsync(WishlistEntry entry)
    {
        if (entry == null) return;

        entry.IsCompleted = !entry.IsCompleted;
        if (!await WriteGate.TryAsync(() => _wishlistService.SaveWishlistEntryAsync(entry))) return;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(WishlistEntry entry)
    {
        if (entry == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle ?? "Delete",
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg ?? "Are you sure you want to delete this record?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes ?? "Yes",
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo ?? "No");

        if (confirm)
        {
            if (!await WriteGate.TryAsync(() => _wishlistService.DeleteWishlistEntryAsync(entry.Id))) return;
            Entries.Remove(entry);
        }
    }

    [RelayCommand]
    private async Task DeleteCurrentEntryAsync()
    {
        if (SelectedEntry != null)
        {
            await DeleteEntryAsync(SelectedEntry);
            CloseForm();
        }
    }
}
