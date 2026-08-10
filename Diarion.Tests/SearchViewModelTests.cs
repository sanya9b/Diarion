using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// What search says about itself now that the on-device encoder is retired. The results are
/// unaffected — lexical search never depended on a model — but the two lines of copy around the
/// box did, and both would now be pointing at something that is not there.
/// </summary>
public class SearchViewModelTests
{
    private readonly Mock<ISemanticSearchService> _search = new();

    private SearchViewModel Create()
    {
        _search.Setup(s => s.SearchAsync(
                It.IsAny<string>(), It.IsAny<SearchScope>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchHit>());

        return new SearchViewModel(_search.Object, new Mock<INavigationService>().Object);
    }

    [Fact]
    public void SemanticCopy_IsNotOffered_WhileTheLocalModelsAreRetired()
    {
        // Drives the phrase hint: "a phrase works better than one word — the model reads sentences".
        // There is no model reading sentences, and keyword search rewards the opposite advice.
        Create().IsSemanticOffered.Should().BeFalse();
    }

    [Fact]
    public async Task LexicalOnlyNotice_StaysHidden_EvenThoughSearchIsLexicalOnly()
    {
        // The notice reads "turn AI on in settings". The tab it means is gone, so the advice would
        // send the user looking for a control that does not exist — worse than saying nothing.
        _search.Setup(s => s.IsSemanticAvailableAsync()).ReturnsAsync(false);

        var vm = Create();
        vm.Query = "спав погано";

        await WaitForSearchAsync(vm);

        vm.ShowLexicalOnlyNotice.Should().BeFalse();
    }

    [Fact]
    public async Task Searching_StillReturnsResults_WithoutAskingAboutTheEncoder()
    {
        _search.Setup(s => s.IsSemanticAvailableAsync()).ReturnsAsync(false);

        var vm = Create();
        vm.Query = "спав погано";

        await WaitForSearchAsync(vm);

        _search.Verify(
            s => s.SearchAsync("спав погано", SearchScope.All, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _search.Verify(s => s.IsSemanticAvailableAsync(), Times.Never,
            "short-circuiting on the flag saves a database read on every keystroke");
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
