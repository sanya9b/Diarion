using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class WishlistViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;

    public ObservableCollection<WishlistEntry> Entries { get; } = new();

    [ObservableProperty]
    private string _newWantText = string.Empty;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

    [ObservableProperty]
    private bool _isAddFormVisible;

    [ObservableProperty]
    private WishlistEntry? _selectedEntry;

    public WishlistViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = Diarion.Resources.Localization.AppResources.WishlistTitle ?? "Хочу, бажаю, отримаю";
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _diaryService.GetWishlistEntriesAsync();
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
        IsAddFormVisible = true;
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsAddFormVisible = false;
        SelectedEntry = null;
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

        await _diaryService.SaveWishlistEntryAsync(entry);
        
        CloseForm();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleCompletedAsync(WishlistEntry entry)
    {
        if (entry == null) return;

        entry.IsCompleted = !entry.IsCompleted;
        await _diaryService.SaveWishlistEntryAsync(entry);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(WishlistEntry entry)
    {
        if (entry == null) return;

        bool confirm = await Microsoft.Maui.Controls.Shell.Current.DisplayAlertAsync(
            Diarion.Resources.Localization.AppResources.DeleteConfirmTitle ?? "Видалити",
            Diarion.Resources.Localization.AppResources.DeleteConfirmMsg ?? "Ви впевнені, що хочете видалити цей запис?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes ?? "Так",
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo ?? "Ні");

        if (confirm)
        {
            await _diaryService.DeleteWishlistEntryAsync(entry.Id);
            Entries.Remove(entry);
        }
    }
}
