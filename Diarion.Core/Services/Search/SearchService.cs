using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services.Search;

/// <summary>
/// Word search over the diary and notes. This was the lexical half of a hybrid search whose other half
/// matched by meaning through the on-device encoder; the encoder was removed on 2026-10-21 and this half
/// stays, unchanged in what it finds.
/// </summary>
public class SearchService : ISearchService
{
    private const int SnippetLength = 180;

    private readonly IDiaryService _diaryService;
    private readonly INoteService _noteService;

    public SearchService(IDiaryService diaryService, INoteService noteService)
    {
        _diaryService = diaryService;
        _noteService = noteService;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        SearchScope scope = SearchScope.All,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var terms = query
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        var hits = new List<SearchHit>();

        if (scope.HasFlag(SearchScope.Notes))
        {
            // The notes screen's own search, so the two entry points cannot drift apart.
            foreach (var note in await _noteService.SearchNotesAsync(query).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                hits.Add(new SearchHit(
                    DocumentKind.Note,
                    note.Id.ToString(),
                    note.UpdatedAt,
                    Snippet(string.IsNullOrWhiteSpace(note.Content) ? note.Title : note.Content),
                    1f));
            }
        }

        if (scope.HasFlag(SearchScope.Diary))
        {
            foreach (var entry in await _diaryService.GetAllEntriesAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entry.HasContent())
                {
                    continue;
                }

                var matched = EntryText.Segments(entry)
                    .FirstOrDefault(s => terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase)));
                if (matched is null)
                {
                    continue;
                }

                hits.Add(new SearchHit(
                    DocumentKind.Diary,
                    entry.Id.ToString(),
                    entry.Date,
                    Snippet(matched),
                    1f));
            }
        }

        return hits.OrderByDescending(h => h.SourceDate).Take(limit).ToList();
    }

    private static string Snippet(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= SnippetLength ? trimmed : trimmed[..SnippetLength].TrimEnd() + "…";
    }
}
