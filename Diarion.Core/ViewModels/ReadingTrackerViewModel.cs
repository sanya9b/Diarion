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

public partial class ReadingTrackerViewModel : BaseViewModel
{
    private const int TotalSlots = 12;
    private readonly IAuxiliaryService _auxiliaryService;

    public ObservableCollection<ReadingTrackerSlotItemViewModel> Slots { get; } = new();

    [ObservableProperty]
    private string _newBookTitle = string.Empty;

    [ObservableProperty]
    private DateTime _newCompletionDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddBookFormVisible))]
    [NotifyPropertyChangedFor(nameof(SelectedSlotTitle))]
    private ReadingTrackerSlotItemViewModel? _selectedSlot;

    public ReadingTrackerViewModel(IAuxiliaryService auxiliaryService)
    {
        _auxiliaryService = auxiliaryService;
        Title = AppResources.ReadingTrackerTitle;
    }

    public bool IsAddBookFormVisible => SelectedSlot != null;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);
    public DateTime MaxCompletionDate => DateTime.Today;
    public string SelectedSlotTitle => SelectedSlot?.SlotCaption ?? string.Empty;

    public async Task LoadAsync()
    {
        var selectedSlotNumber = SelectedSlot?.SlotNumber;
        IsBusy = true;

        try
        {
            var booksBySlot = (await _auxiliaryService.GetReadingTrackerBooksAsync())
                .ToDictionary(x => x.SlotNumber, x => x);

            Slots.Clear();
            for (var slotNumber = 1; slotNumber <= TotalSlots; slotNumber++)
            {
                booksBySlot.TryGetValue(slotNumber, out var book);
                Slots.Add(new ReadingTrackerSlotItemViewModel(slotNumber, book));
            }

            SelectedSlot = selectedSlotNumber.HasValue
                ? Slots.FirstOrDefault(x => x.SlotNumber == selectedSlotNumber)
                : null;

            UpdateSelectedStates();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectSlot(ReadingTrackerSlotItemViewModel? slot)
    {
        if (slot == null)
        {
            return;
        }

        SelectedSlot = ReferenceEquals(SelectedSlot, slot) ? null : slot;
        ValidationMessage = string.Empty;
        NewBookTitle = slot.BookTitle;
        NewCompletionDate = slot.CompletedOn ?? DateTime.Today;
        UpdateSelectedStates();
    }

    [RelayCommand]
    private async Task SaveBookAsync()
    {
        if (SelectedSlot == null)
        {
            return;
        }

        var normalizedTitle = (NewBookTitle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            ValidationMessage = AppResources.ReadingTrackerBookTitleRequiredMessage;
            return;
        }

        ValidationMessage = string.Empty;

        await _auxiliaryService.SaveReadingTrackerBookAsync(new ReadingTrackerBook
        {
            SlotNumber = SelectedSlot.SlotNumber,
            BookTitle = normalizedTitle,
            CompletedOn = NewCompletionDate.Date
        });

        SelectedSlot = null;
        NewBookTitle = string.Empty;
        NewCompletionDate = DateTime.Today;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteBookAsync()
    {
        if (SelectedSlot == null || SelectedSlot.IsEmpty)
            return;

        await _auxiliaryService.DeleteReadingTrackerBookAsync(SelectedSlot.SlotNumber);
        
        SelectedSlot = null;
        await LoadAsync();
    }

    private void UpdateSelectedStates()
    {
        foreach (var slot in Slots)
        {
            slot.IsSelected = SelectedSlot?.SlotNumber == slot.SlotNumber;
        }
    }
}

public partial class ReadingTrackerSlotItemViewModel : ObservableObject
{
    public ReadingTrackerSlotItemViewModel(int slotNumber, ReadingTrackerBook? book)
    {
        SlotNumber = slotNumber;
        BookTitle = book?.BookTitle ?? string.Empty;
        CompletedOn = book?.CompletedOn.Date;
    }

    public int SlotNumber { get; }
    public string SlotNumberText => SlotNumber.ToString(CultureInfo.CurrentCulture);
    public string BookTitle { get; }
    public DateTime? CompletedOn { get; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(BookTitle);
    public bool IsFilled => !IsEmpty;
    public string CompletedOnText => CompletedOn?.ToString("dd.MM", CultureInfo.CurrentCulture) ?? string.Empty;
    public string SlotCaption => string.Format(CultureInfo.CurrentCulture, AppResources.ReadingTrackerSlotFormat, SlotNumber);

    [ObservableProperty]
    private bool _isSelected;
}