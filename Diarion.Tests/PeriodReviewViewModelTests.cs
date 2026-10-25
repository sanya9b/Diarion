using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Reports;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Reports;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// What the review tab says. The numbers are the calculator's business; these tests are about the
/// three things the view adds — the arrow, the tone, and the words for "unknown" — because each of
/// them can mislead on its own.
/// </summary>
public class PeriodReviewViewModelTests
{
    private static readonly StatsRange Week = new(new DateTime(2026, 10, 5), new DateTime(2026, 10, 11));
    private static readonly StatsRange Before = PeriodReviewService.PreviousOf(Week);

    private static PeriodReviewViewModel Create(PeriodReview review)
    {
        var reviews = new Mock<IPeriodReviewService>();
        reviews.Setup(r => r.BuildAsync(It.IsAny<StatsRange>(), It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var profile = new Mock<IProfileService>();
        profile.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile { CurrencyCode = "UAH" });

        return new PeriodReviewViewModel(reviews.Object, profile.Object);
    }

    private static PeriodReview Review(Action<ReviewBuilder>? configure = null)
    {
        var builder = new ReviewBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static string Of(int a, int b) =>
        string.Format(CultureInfo.CurrentCulture, AppResources.ReviewOfFormat, a.ToString(CultureInfo.CurrentCulture), b);

    [Fact]
    public async Task An_empty_review_shows_the_empty_state()
    {
        var vm = Create(Review());

        await vm.LoadDataAsync(Week);

        vm.IsEmpty.Should().BeTrue();
        vm.IsNotEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Days_written_reads_as_so_many_of_the_period_with_the_change_beside_it()
    {
        var vm = Create(Review(r => r.DaysWritten = new ReviewMetric(5, 3)));

        await vm.LoadDataAsync(Week);

        vm.IsEmpty.Should().BeFalse();
        vm.DaysWrittenValue.Should().Be(Of(5, 7));
        vm.DaysWrittenDelta.Should().Be("▲ 2");
        vm.DaysWrittenTone.Should().Be(ReviewTone.Better);
    }

    [Fact]
    public async Task More_spending_is_worse_and_less_is_better()
    {
        var up = Create(Review(r => { r.DaysWritten = new ReviewMetric(1, 1); r.Expense = new ReviewMetric(1500, 1000); }));
        await up.LoadDataAsync(Week);
        up.ExpenseDelta.Should().StartWith("▲");
        up.ExpenseTone.Should().Be(ReviewTone.Worse);

        var down = Create(Review(r => { r.DaysWritten = new ReviewMetric(1, 1); r.Expense = new ReviewMetric(800, 1000); }));
        await down.LoadDataAsync(Week);
        down.ExpenseDelta.Should().StartWith("▼");
        down.ExpenseTone.Should().Be(ReviewTone.Better);
    }

    [Fact]
    public async Task A_metric_with_no_previous_period_says_so_instead_of_inventing_a_change()
    {
        var vm = Create(Review(r => { r.DaysWritten = new ReviewMetric(2, 0); r.Mood = new ReviewMetric(0.8, null); }));

        await vm.LoadDataAsync(Week);

        vm.MoodValue.Should().Be(AppResources.ReviewMoodGood);
        vm.MoodDelta.Should().Be(AppResources.ReviewNoPrevious);
        vm.MoodTone.Should().Be(ReviewTone.Neutral);
    }

    [Fact]
    public async Task A_metric_not_logged_this_period_shows_a_dash_and_says_not_logged()
    {
        var vm = Create(Review(r => r.DaysWritten = new ReviewMetric(2, 1)));

        await vm.LoadDataAsync(Week);

        vm.SleepValue.Should().Be(AppResources.ReviewNoValue);
        vm.SleepDelta.Should().Be(AppResources.ReviewNoData);
        vm.TasksValue.Should().Be(AppResources.ReviewNoValue);
        vm.ExpenseValue.Should().Be(AppResources.ReviewNoValue);
    }

    [Fact]
    public async Task A_change_too_small_to_print_is_no_change_rather_than_an_arrow_at_zero()
    {
        var vm = Create(Review(r => { r.DaysWritten = new ReviewMetric(3, 3); r.Mood = new ReviewMetric(0.503, 0.5); }));

        await vm.LoadDataAsync(Week);

        vm.MoodDelta.Should().Be(AppResources.ReviewNoChange);
        vm.MoodTone.Should().Be(ReviewTone.Neutral);
        vm.DaysWrittenDelta.Should().Be(AppResources.ReviewNoChange);
    }

    [Theory]
    [InlineData(-1.8, nameof(AppResources.ReviewMoodRough))]
    [InlineData(-0.8, nameof(AppResources.ReviewMoodLow))]
    [InlineData(0.0, nameof(AppResources.ReviewMoodSteady))]
    [InlineData(0.4, nameof(AppResources.ReviewMoodSteady))]
    [InlineData(1.0, nameof(AppResources.ReviewMoodGood))]
    [InlineData(1.6, nameof(AppResources.ReviewMoodGreat))]
    public void The_mood_is_a_word_not_a_signed_decimal(double valence, string key)
    {
        PeriodReviewViewModel.MoodWord(valence).Should().Be(AppResources.ResourceManager.GetString(key, AppResources.Culture));
    }

    [Theory]
    [InlineData(1.0, 0.8, nameof(AppResources.ReviewMoodSlightlyBetter), ReviewTone.Better)]
    [InlineData(1.0, 0.2, nameof(AppResources.ReviewMoodBetter), ReviewTone.Better)]
    [InlineData(0.0, 0.3, nameof(AppResources.ReviewMoodSlightlyWorse), ReviewTone.Worse)]
    [InlineData(-1.0, 0.5, nameof(AppResources.ReviewMoodWorse), ReviewTone.Worse)]
    [InlineData(0.55, 0.5, nameof(AppResources.ReviewNoChange), ReviewTone.Neutral)]
    public void The_mood_change_is_said_in_words_with_the_arrow_kept(double now, double before, string key, ReviewTone tone)
    {
        var (_, delta, actualTone) = PeriodReviewViewModel.MoodTile(new ReviewMetric(now, before));

        delta.Should().Be(AppResources.ResourceManager.GetString(key, AppResources.Culture));
        actualTone.Should().Be(tone);
    }

    [Fact]
    public async Task Sleep_is_spelled_in_hours_and_minutes_and_a_short_delta_in_minutes_alone()
    {
        var vm = Create(Review(r => { r.DaysWritten = new ReviewMetric(3, 3); r.SleepHours = new ReviewMetric(7.5, 7.25); }));

        await vm.LoadDataAsync(Week);

        vm.SleepValue.Should().Be($"7 {AppResources.HoursShort} 30 {AppResources.MinutesShort}");
        vm.SleepDelta.Should().Be($"▲ 15 {AppResources.MinutesShort}");
    }

    [Fact]
    public async Task Habit_rows_carry_their_own_count_and_change()
    {
        var vm = Create(Review(r =>
        {
            r.DaysWritten = new ReviewMetric(3, 3);
            r.HabitCompletion = new ReviewMetric(0.5, 0.25);
            r.Habits = new List<ReviewHabit>
            {
                new("Water", 5, 7, 3, 7),
                new("Reading", 2, 3, null, null)
            };
        }));

        await vm.LoadDataAsync(Week);

        vm.HasHabitRows.Should().BeTrue();
        vm.HabitRows.Should().HaveCount(2);
        vm.HabitRows[0].ValueText.Should().Be(Of(5, 7));
        vm.HabitRows[0].Tone.Should().Be(ReviewTone.Better);
        vm.HabitRows[1].DeltaText.Should().Be(AppResources.ReviewNoPrevious);
        vm.HabitsValue.Should().Be(0.5.ToString("P0", CultureInfo.CurrentCulture));
    }

    [Fact]
    public async Task The_top_emotion_is_named_in_the_interface_language_and_hidden_when_absent()
    {
        var named = Create(Review(r => { r.DaysWritten = new ReviewMetric(1, 0); r.TopEmotion = "Calm"; }));
        await named.LoadDataAsync(Week);
        named.HasTopEmotion.Should().BeTrue();
        named.TopEmotionText.Should().Contain(Emotion.Calm.ToLocalizedName());

        var none = Create(Review(r => r.DaysWritten = new ReviewMetric(1, 0)));
        await none.LoadDataAsync(Week);
        none.HasTopEmotion.Should().BeFalse();
    }

    [Fact]
    public async Task Both_windows_are_named()
    {
        var vm = Create(Review(r => r.DaysWritten = new ReviewMetric(1, 0)));

        await vm.LoadDataAsync(Week);

        vm.PeriodText.Should().Contain(Week.Start.ToString("d MMM", CultureInfo.CurrentCulture));
        vm.PreviousPeriodText.Should().Contain(Before.End.ToString("d MMM", CultureInfo.CurrentCulture));
    }

    private sealed class ReviewBuilder
    {
        public ReviewMetric DaysWritten = new(0, 0);
        public ReviewMetric Mood = ReviewMetric.None;
        public ReviewMetric SleepHours = ReviewMetric.None;
        public ReviewMetric HabitCompletion = ReviewMetric.None;
        public ReviewMetric TasksCompleted = ReviewMetric.None;
        public ReviewMetric TasksTotal = ReviewMetric.None;
        public ReviewMetric Expense = ReviewMetric.None;
        public string TopEmotion = string.Empty;
        public IReadOnlyList<ReviewHabit> Habits = Array.Empty<ReviewHabit>();

        public PeriodReview Build() => new()
        {
            Range = Week,
            PreviousRange = Before,
            DayCount = Week.Days,
            DaysWritten = DaysWritten,
            Mood = Mood,
            SleepHours = SleepHours,
            SleepQuality = ReviewMetric.None,
            HabitCompletion = HabitCompletion,
            TasksCompleted = TasksCompleted,
            TasksTotal = TasksTotal,
            Expense = Expense,
            Income = ReviewMetric.None,
            TopEmotion = TopEmotion,
            Habits = Habits
        };
    }
}
