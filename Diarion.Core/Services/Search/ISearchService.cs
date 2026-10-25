using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services.Search;

/// <summary>
/// Searches the diary and notes together by the words in them. Lives apart from <c>IDiaryService</c>
/// on purpose: the diary interface is about days, and retrieval would drag search into every caller
/// that only wants to open a date.
/// </summary>
public interface ISearchService
{
    /// <summary>Documents containing every word of <paramref name="query"/>, newest first.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        SearchScope scope = SearchScope.All,
        int limit = 30,
        CancellationToken cancellationToken = default);
}
