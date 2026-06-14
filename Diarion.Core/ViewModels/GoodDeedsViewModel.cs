using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class GoodDeedsViewModel : BaseViewModel
{
    private const int TotalSlots = 12; // 12 slots for good deeds
    private readonly IDiaryService _diaryService;

    public ObservableCollection<GoodDeedSlotItemViewModel> DeedSlots { get; } = new();

    [ObservableProperty]
    private string _newDeedTitle = string.Empty;

    [ObservableProperty]
    private DateTime _newDeedDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddDeedFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsViewDeedFormVisible))]
    private GoodDeedSlotItemViewModel? _selectedSlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public GoodDeedsViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = AppResources.GoodDeedsTitle;
    }

    public bool IsAddDeedFormVisible => SelectedSlot != null;
    public bool IsViewDeedFormVisible => SelectedSlot?.IsFilled == true;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public DateTime MaxDeedDate => DateTime.Today;

    public async Task LoadAsync()
    {
        var selectedSlotNumber = SelectedSlot?.SlotNumber;
        IsBusy = true;

        try
        {
            var deeds = await _diaryService.GetGoodDeedsAsync();
            var deedsBySlot = deeds.ToDictionary(x => x.SlotNumber);

            DeedSlots.Clear();
            for (var slotNumber = 1; slotNumber <= TotalSlots; slotNumber++)
            {
                deedsBySlot.TryGetValue(slotNumber, out var deed);
                DeedSlots.Add(new GoodDeedSlotItemViewModel(slotNumber, deed));
            }

            SelectedSlot = selectedSlotNumber.HasValue
                ? DeedSlots.FirstOrDefault(x => x.SlotNumber == selectedSlotNumber)
                : null;

            UpdateSelectedStates();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectSlot(GoodDeedSlotItemViewModel? slot)
    {
        if (slot == null)
        {
            return;
        }

        ValidationMessage = string.Empty;
        SelectedSlot = ReferenceEquals(SelectedSlot, slot) ? null : slot;
        NewDeedTitle = slot.DeedTitle;
        NewDeedDate = slot.Date ?? DateTime.Today;
        UpdateSelectedStates();
    }

    [RelayCommand]
    private async Task SaveSelectedDeedAsync()
    {
        if (SelectedSlot == null)
        {
            return;
        }

        var normalizedTitle = (NewDeedTitle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            ValidationMessage = AppResources.GoodDeedsTitleRequiredMessage;
            return;
        }

        ValidationMessage = string.Empty;
        await _diaryService.SaveGoodDeedAsync(new GoodDeed
        {
            SlotNumber = SelectedSlot.SlotNumber,
            Title = normalizedTitle,
            Date = NewDeedDate.Date
        });

        SelectedSlot = null;
        NewDeedTitle = string.Empty;
        NewDeedDate = DateTime.Today;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedDeedAsync()
    {
        if (SelectedSlot == null || SelectedSlot.IsEmpty)
            return;

        await _diaryService.DeleteGoodDeedAsync(SelectedSlot.SlotNumber);
        
        SelectedSlot = null;
        await LoadAsync();
    }

    private void UpdateSelectedStates()
    {
        foreach (var slot in DeedSlots)
        {
            slot.IsSelected = SelectedSlot?.SlotNumber == slot.SlotNumber;
        }
    }
}

public partial class GoodDeedSlotItemViewModel : ObservableObject
{
    public GoodDeedSlotItemViewModel(int slotNumber, GoodDeed? deed)
    {
        SlotNumber = slotNumber;
        DeedTitle = deed?.Title ?? string.Empty;
        Date = deed?.Date.Date;
    }

    public int SlotNumber { get; }
    public string SlotNumberText => SlotNumber.ToString(CultureInfo.CurrentCulture);
    public string DeedTitle { get; }
    public DateTime? Date { get; }
    public string DateText => Date?.ToString("dd.MM", CultureInfo.CurrentCulture) ?? string.Empty;
    public bool IsFilled => !string.IsNullOrWhiteSpace(DeedTitle);
    public bool IsEmpty => !IsFilled;
    public string SlotCaption => string.Format(CultureInfo.CurrentCulture, AppResources.GoodDeedsSlotFormat, SlotNumberText);

    [ObservableProperty]
    private bool _isSelected;
}