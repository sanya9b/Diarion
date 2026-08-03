using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Helpers;
using Diarion.Messages;
using Diarion.Models.Ai;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Ai;

namespace Diarion.ViewModels;

public partial class SearchViewModel : BaseViewModel
{
    private readonly ISemanticSearchService _search;
    private readonly INavigationService _navigation;
    private readonly AsyncDebouncer _debouncer = new(TimeSpan.FromMilliseconds(250));

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllScope))]
    [NotifyPropertyChangedFor(nameof(IsDiaryScope))]
    [NotifyPropertyChangedFor(nameof(IsNotesScope))]
    private SearchScope _scope = SearchScope.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private string _resultsSummary = string.Empty;

    [ObservableProperty]
    private bool _showEmptyState;

    public ObservableCollection<SearchResultItem> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    public bool IsAllScope => Scope == SearchScope.All;
    public bool IsDiaryScope => Scope == SearchScope.Diary;
    public bool IsNotesScope => Scope == SearchScope.Notes;

    /// <summary>
    /// Shown when no encoder is installed. The results are still real, just lexical — saying so
    /// beats letting the user conclude that search is broken.
    /// </summary>
    public bool ShowLexicalOnlyNotice => !_search.IsSemanticAvailable;

    public SearchViewModel(ISemanticSearchService search, INavigationService navigation)
    {
        _search = search;
        _navigation = navigation;
        Title = AppResources.SearchTitle;
    }

    partial void OnQueryChanged(string value) => _debouncer.Debounce(RunSearchAsync);

    partial void OnScopeChanged(SearchScope value) => _debouncer.Debounce(RunSearchAsync);

    [RelayCommand]
    private void SetScope(string scope) =>
        Scope = scope switch
        {
            "diary" => SearchScope.Diary,
            "notes" => SearchScope.Notes,
            _ => SearchScope.All,
        };

    private async Task RunSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            ResultsSummary = string.Empty;
            ShowEmptyState = false;
            OnPropertyChanged(nameof(HasResults));
            return;
        }

        IsBusy = true;
        try
        {
            var hits = await _search.SearchAsync(Query, Scope);

            Results.Clear();
            foreach (var hit in hits)
            {
                Results.Add(new SearchResultItem(hit));
            }

            ResultsSummary = hits.Count > 0
                ? string.Format(AppResources.SearchResultsCountFormat, hits.Count)
                : string.Empty;
            ShowEmptyState = hits.Count == 0;
            OnPropertyChanged(nameof(HasResults));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync(SearchResultItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Hit.SourceKind == EmbeddingSourceKind.Note)
        {
            await _navigation.NavigateToAsync(
                "NoteDetail",
                new Dictionary<string, object> { ["NoteId"] = item.Hit.SourceId });
            return;
        }

        // A diary hit opens the day, not DiaryDetail. That page edits three fields — title, emotion,
        // body — while a match can just as easily be in gratitude, sleep notes or meals. Landing on
        // a page that does not contain the text you searched for reads as the search having lied.
        // The full day form is the main tab, told which date to show.
        WeakReferenceMessenger.Default.Send(new NavigateToDateMessage(item.Hit.SourceDate));
        await _navigation.NavigateToAsync("//MainPage");
    }
}

/// <summary>One row of results, with the presentation details the hit itself has no business knowing.</summary>
public sealed class SearchResultItem
{
    public SearchResultItem(SearchHit hit)
    {
        Hit = hit;
    }

    public SearchHit Hit { get; }

    public string Snippet => Hit.Snippet;

    public string SourceLabel => Hit.SourceKind == EmbeddingSourceKind.Note
        ? AppResources.SearchSourceNote
        : AppResources.SearchSourceDiary;

    public string DateLabel => Hit.SourceDate.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
}
