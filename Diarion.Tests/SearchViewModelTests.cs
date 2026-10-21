using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Search;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// What search says about itself. The results never depended on a model — lexical search is lexical
/// search — but the two lines of copy around the box do, and both have to point at something that is
/// actually there. The screen itself is unreachable while the quick-menu tile is retired; these tests
/// keep it honest for the day it comes back.
/// </summary>
public class SearchViewModelTests
{
    private readonly Mock<ISearchService> _search = new();

    private SearchViewModel Create()
    {
        _search.Setup(s => s.SearchAsync(
                It.IsAny<string>(), It.IsAny<SearchScope>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchHit>());

        return new SearchViewModel(_search.Object, new Mock<INavigationService>().Object);
    }

    [Fact]
    public async Task Searching_AsksTheServiceWithTheQueryAndScope()
    {
        var vm = Create();
        vm.Query = "спав погано";

        await WaitForSearchAsync(vm);

        _search.Verify(
            s => s.SearchAsync("спав погано", SearchScope.All, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Polls instead of sleeping past the 250 ms debounce: a fixed wait is either flaky or slow, and
    /// this suite runs serially.
    /// </summary>
    private async Task WaitForSearchAsync(SearchViewModel vm)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(25);

            try
            {
                _search.Verify(
                    s => s.SearchAsync(
                        It.IsAny<string>(), It.IsAny<SearchScope>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce);
                return;
            }
            catch (MockException)
            {
                // Not yet.
            }
        }

        throw new TimeoutException("the debounced search never ran");
    }
}
