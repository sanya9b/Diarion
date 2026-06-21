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
    private string _newWishText = string.Empty;

    [ObservableProperty]
    private string _newGetText = string.Empty;

    [ObservableProperty]
    private DateTime _newDate = DateTime.Today;

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
    private async Task SaveEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWantText) && 
            string.IsNullOrWhiteSpace(NewWishText) && 
            string.IsNullOrWhiteSpace(NewGetText))
        {
            return;
        }

        var entry = new WishlistEntry
        {
            WantText = NewWantText.Trim(),
            WishText = NewWishText.Trim(),
            GetText = NewGetText.Trim(),
            Date = NewDate.Date
        };

        await _diaryService.SaveWishlistEntryAsync(entry);
        
        NewWantText = string.Empty;
        NewWishText = string.Empty;
        NewGetText = string.Empty;
        NewDate = DateTime.Today;

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
