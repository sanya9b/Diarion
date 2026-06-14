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

public partial class HappyMomentsViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;

    public ObservableCollection<HappyMomentSlotItemViewModel> MomentSlots { get; } = new();

    [ObservableProperty]
    private string _newMomentTitle = string.Empty;

    [ObservableProperty]
    private DateTime _newMomentDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddMomentFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsViewMomentFormVisible))]
    private HappyMomentSlotItemViewModel? _selectedEmptySlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public HappyMomentsViewModel(IDiaryService diaryService)
    {
        _diaryService = diaryService;
        Title = AppResources.HappyMomentsTitle;
    }

    public bool IsAddMomentFormVisible => SelectedEmptySlot != null;
    public bool IsViewMomentFormVisible => SelectedEmptySlot?.IsFilled == true;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public DateTime MaxMomentDate => DateTime.Today;

    public async Task LoadAsync()
    {
        var selectedSlotNumber = SelectedEmptySlot?.SlotNumber;
        IsBusy = true;

        try
        {
            var moments = await _diaryService.GetHappyMomentsAsync();

            MomentSlots.Clear();
            
            // Завжди порожній слот зверху
            int nextSlotNumber = moments.Any() ? moments.Max(x => x.SlotNumber) + 1 : 1;
            MomentSlots.Add(new HappyMomentSlotItemViewModel(nextSlotNumber, null));

            // Відмічені події спускаються вниз, сортовані за датою
            var orderedMoments = moments.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt);
            foreach (var moment in orderedMoments)
            {
                MomentSlots.Add(new HappyMomentSlotItemViewModel(moment.SlotNumber, moment));
            }

            SelectedEmptySlot = selectedSlotNumber.HasValue
                ? MomentSlots.FirstOrDefault(x => x.SlotNumber == selectedSlotNumber)
                : null;

            UpdateSelectedStates();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectSlot(HappyMomentSlotItemViewModel? slot)
    {
        if (slot == null)
        {
            return;
        }

        ValidationMessage = string.Empty;
        SelectedEmptySlot = ReferenceEquals(SelectedEmptySlot, slot) ? null : slot;
        NewMomentTitle = slot.MomentTitle;
        NewMomentDate = slot.Date ?? DateTime.Today;
        UpdateSelectedStates();
    }

    [RelayCommand]
    private async Task SaveSelectedMomentAsync()
    {
        if (SelectedEmptySlot == null)
        {
            return;
        }

        var normalizedTitle = (NewMomentTitle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            ValidationMessage = AppResources.HappyMomentsTitleRequiredMessage;
            return;
        }

        ValidationMessage = string.Empty;
        await _diaryService.SaveHappyMomentAsync(new HappyMoment
        {
            SlotNumber = SelectedEmptySlot.SlotNumber,
            Title = normalizedTitle,
            Date = NewMomentDate.Date
        });

        SelectedEmptySlot = null;
        NewMomentTitle = string.Empty;
        NewMomentDate = DateTime.Today;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedMomentAsync()
    {
        if (SelectedEmptySlot == null || SelectedEmptySlot.IsEmpty)
            return;

        await _diaryService.DeleteHappyMomentAsync(SelectedEmptySlot.SlotNumber);
        
        SelectedEmptySlot = null;
        await LoadAsync();
    }

    private void UpdateSelectedStates()
    {
        foreach (var slot in MomentSlots)
        {
            slot.IsSelected = slot.IsEmpty && SelectedEmptySlot?.SlotNumber == slot.SlotNumber;
        }
    }
}

public partial class HappyMomentSlotItemViewModel : ObservableObject
{
    public HappyMomentSlotItemViewModel(int slotNumber, HappyMoment? moment)
    {
        SlotNumber = slotNumber;
        MomentTitle = moment?.Title ?? string.Empty;
        Date = moment?.Date.Date;
    }

    public int SlotNumber { get; }
    public string SlotNumberText => SlotNumber.ToString(CultureInfo.CurrentCulture);
    public string MomentTitle { get; }
    public DateTime? Date { get; }
    public string DateText => Date?.ToString("dd.MM", CultureInfo.CurrentCulture) ?? string.Empty;
    public bool IsFilled => !string.IsNullOrWhiteSpace(MomentTitle);
    public bool IsEmpty => !IsFilled;
    public string SlotCaption => IsEmpty ? AppResources.HappyMomentsFormTitle : string.Format(CultureInfo.CurrentCulture, AppResources.HappyMomentsSlotFormat, SlotNumberText);

    [ObservableProperty]
    private bool _isSelected;
}