using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class PromptHistoryViewModelTests
{
    private static readonly Guid CustomPromptId = Guid.NewGuid();

    private static PromptLibrary Library() => new(new List<GuidedPrompt>
    {
        new() { Id = CustomPromptId, TextUk = "Що сьогодні вдалося?", TextEn = "What went well today?" }
    });

    private static (PromptHistoryViewModel Vm, Mock<INavigationService> Nav) Build(params PromptAnswerDto[] answers)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetPromptAnswersAsync()).ReturnsAsync(answers.ToList());

        var prompts = new Mock<IGuidedPromptService>();
        prompts.Setup(s => s.GetLibraryAsync()).ReturnsAsync(Library());

        var nav = new Mock<INavigationService>();
        return (new PromptHistoryViewModel(diary.Object, prompts.Object, nav.Object), nav);
    }

    [Fact]
    public async Task LoadAsync_ResolvesTheQuestionForEachAnswer()
    {
        var (vm, _) = Build(new PromptAnswerDto
        {
            EntryId = Guid.NewGuid(),
            Date = DateTime.Today,
            PromptReference = CustomPromptId.ToString(),
            Answer = "  Finished the migration.  "
        });

        await vm.LoadAsync();

        vm.Answers.Should().ContainSingle();
        vm.Answers[0].HasQuestion.Should().BeTrue();
        vm.Answers[0].Answer.Should().Be("Finished the migration.", "the answer is trimmed for the card");
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_UnresolvableReference_StillShowsTheAnswer()
    {
        var (vm, _) = Build(new PromptAnswerDto
        {
            EntryId = Guid.NewGuid(),
            Date = DateTime.Today,
            PromptReference = "PromptFromABuildThatNoLongerExists",
            Answer = "Something I wrote anyway."
        });

        await vm.LoadAsync();

        vm.Answers.Should().ContainSingle();
        vm.Answers[0].HasQuestion.Should().BeFalse("an orphaned reference must not hide the writing");
        vm.Answers[0].Answer.Should().Be("Something I wrote anyway.");
    }

    [Fact]
    public async Task Search_MatchesAnswerAndQuestion_AndReportsNoMatches()
    {
        var (vm, _) = Build(
            new PromptAnswerDto { EntryId = Guid.NewGuid(), Date = DateTime.Today, PromptReference = CustomPromptId.ToString(), Answer = "Went running" },
            new PromptAnswerDto { EntryId = Guid.NewGuid(), Date = DateTime.Today.AddDays(-1), PromptReference = CustomPromptId.ToString(), Answer = "Read a book" });

        await vm.LoadAsync();

        vm.SearchQuery = "running";
        vm.Answers.Should().ContainSingle();

        // A slice of the question as it actually resolved — which language that is depends on the
        // ambient culture, and the search must not care either way.
        vm.SearchQuery = string.Empty;
        var questionFragment = vm.Answers[0].Question[..6];

        vm.SearchQuery = questionFragment;      // matches the question, not any answer
        vm.Answers.Should().HaveCount(2);

        vm.SearchQuery = "nothing like this";
        vm.Answers.Should().BeEmpty();
        vm.HasNoMatches.Should().BeTrue();
        vm.IsEmpty.Should().BeFalse("there are answers; they just do not match the search");

        vm.SearchQuery = string.Empty;
        vm.Answers.Should().HaveCount(2);
        vm.HasNoMatches.Should().BeFalse();
    }

    [Fact]
    public async Task OpenDay_NavigatesToThatEntry()
    {
        var entryId = Guid.NewGuid();
        var (vm, nav) = Build(new PromptAnswerDto
        {
            EntryId = entryId,
            Date = DateTime.Today,
            PromptReference = CustomPromptId.ToString(),
            Answer = "An answer"
        });

        await vm.LoadAsync();
        await vm.OpenDayCommand.ExecuteAsync(vm.Answers[0]);

        nav.Verify(n => n.NavigateToAsync($"DiaryDetail?Id={entryId}"), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_WithNothingAnswered_IsEmpty()
    {
        var (vm, _) = Build();

        await vm.LoadAsync();

        vm.IsEmpty.Should().BeTrue();
        vm.HasAnswers.Should().BeFalse();
        vm.HasNoMatches.Should().BeFalse("nothing written is a different state from nothing matching");
    }
}
