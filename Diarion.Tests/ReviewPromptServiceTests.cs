using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Review;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The service around the policy: it reads days-with-entries from the diary, keeps the last request
/// on the profile, writes that date before asking, and never lets a failure reach the caller.
/// </summary>
public class ReviewPromptServiceTests
{
    private static readonly DateTime Today = new(2026, 11, 13);

    private readonly Mock<IDiaryService> _diary = new();
    private readonly Mock<IProfileService> _profiles = new();
    private readonly Mock<IReviewPrompter> _prompter = new();
    private readonly UserProfile _profile = new();

    public ReviewPromptServiceTests()
    {
        _profiles.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(_profile);
        _profiles.Setup(p => p.SaveUserProfileAsync(It.IsAny<UserProfile>())).Returns(Task.CompletedTask);
        _prompter.Setup(p => p.RequestAsync()).ReturnsAsync(true);
    }

    private ReviewPromptService Service() => new(_diary.Object, _profiles.Object, _prompter.Object, () => Today);

    private void DiaryHasDays(int count)
    {
        IReadOnlyList<DateTime> dates = Enumerable.Range(0, count).Select(i => Today.AddDays(-i)).ToList();
        _diary.Setup(d => d.GetContentDatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(dates);
    }

    [Fact]
    public async Task Asks_once_the_diary_has_seven_days_and_stamps_the_profile_first()
    {
        DiaryHasDays(7);
        var order = new List<string>();
        _profiles.Setup(p => p.SaveUserProfileAsync(It.IsAny<UserProfile>())).Callback(() => order.Add("save")).Returns(Task.CompletedTask);
        _prompter.Setup(p => p.RequestAsync()).Callback(() => order.Add("ask")).ReturnsAsync(true);

        await Service().MaybeRequestAsync();

        _profile.ReviewPromptedAt.Should().Be(Today);
        order.Should().Equal("save", "ask");
    }

    [Fact]
    public async Task Six_days_is_not_enough()
    {
        DiaryHasDays(6);

        await Service().MaybeRequestAsync();

        _prompter.Verify(p => p.RequestAsync(), Times.Never);
        _profile.ReviewPromptedAt.Should().BeNull();
    }

    [Fact]
    public async Task Inside_the_cooldown_the_diary_is_not_even_read()
    {
        _profile.ReviewPromptedAt = Today.AddDays(-10);

        await Service().MaybeRequestAsync();

        _diary.Verify(d => d.GetContentDatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        _prompter.Verify(p => p.RequestAsync(), Times.Never);
    }

    [Fact]
    public async Task After_the_cooldown_it_asks_again_and_moves_the_stamp()
    {
        _profile.ReviewPromptedAt = Today.AddDays(-ReviewPromptPolicy.CooldownDays);
        DiaryHasDays(40);

        await Service().MaybeRequestAsync();

        _prompter.Verify(p => p.RequestAsync(), Times.Once);
        _profile.ReviewPromptedAt.Should().Be(Today);
    }

    [Fact]
    public async Task The_stamp_is_kept_even_when_the_platform_stays_silent()
    {
        DiaryHasDays(10);
        _prompter.Setup(p => p.RequestAsync()).ReturnsAsync(false);

        await Service().MaybeRequestAsync();

        _profile.ReviewPromptedAt.Should().Be(Today);
    }

    [Fact]
    public async Task A_failing_diary_read_is_swallowed()
    {
        _diary.Setup(d => d.GetContentDatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("db closed"));

        var act = async () => await Service().MaybeRequestAsync();

        await act.Should().NotThrowAsync();
        _prompter.Verify(p => p.RequestAsync(), Times.Never);
    }
}
