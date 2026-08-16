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
/// What search says about itself. The results never depended on a model — lexical search is lexical
/// search — but the two lines of copy around the box do, and both have to point at something that is
/// actually there. The screen itself is unreachable while the quick-menu tile is retired; these tests
/// keep it honest for the day it comes back.
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
    public void SemanticCopy_FollowsTheEncoderAndNotTheGenerativeModel()
    {
        // Drives the phrase hint: "a phrase works better than one word — the model reads sentences".
        // The model that reads sentences is the encoder, which is still offered; the retired
        // generative model was never the one answering this question.
        Create().IsSemanticOffered.Should().Be(OnDeviceAi.EmbeddingsOffered);
        Create().IsSemanticOffered.Should().BeTrue("the encoder is what makes a phrase worth typing");
    }

    [Fact]
    public async Task LexicalOnlyNotice_AppearsWhenTheEncoderIsNotThere()
    {
        // The notice reads "turn AI on in settings" — and the tab it means exists again, so the
        // advice is actionable rather than a hunt for a control that was removed.
        _search.Setup(s => s.IsSemanticAvailableAsync()).ReturnsAsync(false);

        var vm = Create();
        vm.Query = "спав погано";

        await WaitForSearchAsync(vm);

        vm.ShowLexicalOnlyNotice.Should().BeTrue();
    }

    [Fact]
    public async Task LexicalOnlyNotice_StaysAwayWhenTheEncoderIsReady()
    {
        // Nothing to apologise for: the results are the meaning-based ones the copy promises.
        _search.Setup(s => s.IsSemanticAvailableAsync()).ReturnsAsync(true);

        var vm = Create();
        vm.Query = "спав погано";

        await WaitForSearchAsync(vm);

        vm.ShowLexicalOnlyNotice.Should().BeFalse();
    }

    [Fact]
    public async Task Searching_ReturnsResults_WhateverTheEncoderAnswers()
    {
        // The notice is decoration around results that come back either way. A search that failed
        // because no model was installed would be the worst reading of this screen.
        _search.Setup(s => s.IsSemanticAvailableAsync()).ReturnsAsync(false);

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
