using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

[QueryProperty(nameof(PromptId), "PromptId")]
public partial class PromptEditorViewModel : BaseViewModel
{
    public const int MaxTextLength = 300;

    private readonly IGuidedPromptService _promptService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string? _promptId;

    [ObservableProperty]
    private string _textUk = string.Empty;

    [ObservableProperty]
    private string _textEn = string.Empty;

    [ObservableProperty]
    private PromptCategory _category = PromptCategory.OpenReflection;

    [ObservableProperty]
    private bool _isExisting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    private GuidedPrompt? _editing;

    public PromptEditorViewModel(
        IGuidedPromptService promptService,
        INavigationService navigationService,
        IDialogService dialogService)
    {
        _promptService = promptService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = AppResources.PromptEditorTitle;
    }

    partial void OnPromptIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && Guid.TryParse(value, out var id))
        {
            _ = LoadAsync(id);
        }
    }

    private async Task LoadAsync(Guid id)
    {
        var prompt = await _promptService.GetByIdAsync(id);
        if (prompt == null) return;

        _editing = prompt;
        IsExisting = true;

        TextUk = prompt.TextUk;
        TextEn = prompt.TextEn;
        Category = prompt.Category;
    }

    [RelayCommand]
    private void SetCategory(PromptCategory category) => Category = category;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationMessage = string.Empty;

        var uk = (TextUk ?? string.Empty).Trim();
        var en = (TextEn ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(uk) && string.IsNullOrWhiteSpace(en))
        {
            ValidationMessage = AppResources.PromptEditorTextRequired;
            return;
        }

        if (uk.Length > MaxTextLength || en.Length > MaxTextLength)
        {
            ValidationMessage = AppResources.PromptEditorTextTooLong;
            return;
        }

        if (IsExisting && _editing != null)
        {
            _editing.TextUk = uk;
            _editing.TextEn = en;
            _editing.Category = Category;
            // ResourceKey is deliberately preserved, unlike the habit editor: diary entries written
            // before prompts moved into the database reference built-ins by that key.
            await _promptService.UpdateAsync(_editing);
        }
        else
        {
            await _promptService.AddAsync(new GuidedPrompt
            {
                TextUk = uk,
                TextEn = en,
                Category = Category,
                CreatedAt = DateTime.Today
            });
        }

        WeakReferenceMessenger.Default.Send(new PromptLibraryChangedMessage());
        await _navigationService.NavigateBackAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!IsExisting || _editing == null)
        {
            await _navigationService.NavigateBackAsync();
            return;
        }

        bool confirm = await _dialogService.ShowConfirmationAsync(
            AppResources.DeletePromptConfirmTitle,
            string.Format(AppResources.DeletePromptConfirmMessage, PromptLocalization.ResolveText(_editing)),
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);

        if (!confirm) return;

        await _promptService.DeleteAsync(_editing.Id, DateTime.Today);
        WeakReferenceMessenger.Default.Send(new PromptLibraryChangedMessage());
        await _navigationService.NavigateBackAsync();
    }

    [RelayCommand]
    private async Task CancelAsync() => await _navigationService.NavigateBackAsync();
}
