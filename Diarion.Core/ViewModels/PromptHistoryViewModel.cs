using System;
using System.Collections.Generic;
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

/// <summary>One past answer: the question as it reads today, what was written, and the day it belongs to.</summary>
public partial class PromptAnswerItemViewModel : ObservableObject
{
    public Guid EntryId { get; init; }
    public DateTime Date { get; init; }
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;

    public string DateText => Date.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>An entry whose prompt reference no longer resolves — deleted, or from before the library.</summary>
    public bool HasQuestion => !string.IsNullOrWhiteSpace(Question);
}

public partial class PromptHistoryViewModel : BaseViewModel
{
    private readonly IDiaryService _diaryService;
    private readonly IGuidedPromptService _promptService;
    private readonly INavigationService _navigationService;

    /// <summary>Everything loaded, unfiltered; <see cref="Answers"/> is the view over it.</summary>
    private List<PromptAnswerItemViewModel> _all = new();

    public ObservableCollection<PromptAnswerItemViewModel> Answers { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasAnswers))]
    private int _answerCount;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public bool HasAnswers => AnswerCount > 0;
    public bool IsEmpty => AnswerCount == 0;

    /// <summary>Distinguishes "nothing written yet" from "nothing matches the search".</summary>
    [ObservableProperty]
    private bool _hasNoMatches;

    public PromptHistoryViewModel(
        IDiaryService diaryService,
        IGuidedPromptService promptService,
        INavigationService navigationService)
    {
        _diaryService = diaryService;
        _promptService = promptService;
        _navigationService = navigationService;
        Title = AppResources.PromptHistoryTitle;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var answers = await _diaryService.GetPromptAnswersAsync();

            // The whole library, including soft-deleted rows, so an answer to a question the user later
            // removed still shows the question rather than an orphaned paragraph.
            var library = await _promptService.GetLibraryAsync();

            _all = answers
                .Select(a => new PromptAnswerItemViewModel
                {
                    EntryId = a.EntryId,
                    Date = a.Date,
                    Question = PromptLocalization.ResolveText(library.Find(a.PromptReference)),
                    Answer = a.Answer.Trim()
                })
                .ToList();

            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = (SearchQuery ?? string.Empty).Trim();

        var matches = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(a =>
                    a.Answer.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || a.Question.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        Answers.Clear();
        foreach (var item in matches)
        {
            Answers.Add(item);
        }

        AnswerCount = _all.Count;
        HasNoMatches = _all.Count > 0 && matches.Count == 0;
    }

    [RelayCommand]
    private async Task OpenDayAsync(PromptAnswerItemViewModel? item)
    {
        if (item == null) return;

        await _navigationService.NavigateToAsync($"DiaryDetail?Id={item.EntryId}");
    }

    [RelayCommand]
    private async Task CloseAsync() => await _navigationService.NavigateBackAsync();
}
