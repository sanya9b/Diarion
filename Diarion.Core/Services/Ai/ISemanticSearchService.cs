using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>
/// Searches the diary and notes together, by meaning and by words at once.
/// </summary>
/// <remarks>
/// This is where the diary finally gets a query path. It lives on the search service rather than on
/// <c>IDiaryService</c> deliberately: the diary interface is about days, and adding retrieval to it
/// would drag the vector store into every caller that only wants to open a date.
/// </remarks>
public interface ISemanticSearchService
{
    /// <summary>
    /// True when the encoder is installed and AI is switched on, so callers can say why results
    /// look lexical. Both halves matter: an encoder sitting on disk under a switched-off toggle
    /// produces exactly the same lexical-only results as no encoder at all.
    /// </summary>
    Task<bool> IsSemanticAvailableAsync();

    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        SearchScope scope = SearchScope.All,
        int limit = 30,
        CancellationToken cancellationToken = default);
}
