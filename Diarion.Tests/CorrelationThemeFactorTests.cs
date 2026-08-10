using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// What the user wrote about, as a correlation factor. Every other factor is a field somebody had to
/// fill in; a theme is what they were already writing about anyway, which is the association no
/// single-purpose tracker can offer.
/// </summary>
public class CorrelationThemeFactorTests
{
    private const int Days = 24;

    private static Emotion Tier(int i) => i switch
    {
        < 6 => Emotion.Sad,
        < 12 => Emotion.Anxious,
        < 18 => Emotion.Calm,
        _ => Emotion.Happy
    };

    /// <summary>Day index 0 is the oldest, so a factor rising with the index rises with mood too.</summary>
    private static DateTime DateFor(int i) => DateTime.Today.AddDays(-(Days - 1 - i));

    private static List<DiaryEntryStatsDto> MoodOnly(int count = Days) =>
        Enumerable.Range(0, count)
            .Select(i => new DiaryEntryStatsDto { Date = DateFor(i), Emotion = Tier(i) })
            .ToList();

    private static CorrelationService Build(
        IEnumerable<DiaryEntryStatsDto> entries,
        IThemeClusterService themes)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(entries.ToList());

        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync(new List<CycleLog>());

        var profile = new Mock<IProfileService>();
        profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

        var todos = new Mock<ITodoService>();
        todos.Setup(s => s.GetTodosForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(new List<TodoStatsDto>());

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(new List<FinanceTransaction>());

        return new CorrelationService(
            diary.Object, cycle.Object, profile.Object, todos.Object, finance.Object, themes);
    }

    /// <summary>A theme present on the given day indices, over a window written on every day.</summary>
    private static StubThemes Themes(string label, IEnumerable<int> presentOn, int writtenDays = Days) =>
        new(
            [new DiaryTheme(label, presentOn.Select(DateFor).Order().ToList())],
            Enumerable.Range(0, writtenDays).Select(DateFor).ToList());

    private static MoodCorrelation? Find(IReadOnlyList<MoodCorrelation> all, string label)
        => all.FirstOrDefault(c => c.FactorKey == CorrelationService.Factors.ThemePrefix + label);

    [Fact]
    public async Task A_theme_present_on_the_low_mood_days_becomes_a_factor()
    {
        // Mood rises with the day index, so a theme confined to the early half runs against it.
        var result = await Build(MoodOnly(), Themes("Стрес на роботі", Enumerable.Range(0, 12)))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        var theme = Find(result, "Стрес на роботі");
        theme.Should().NotBeNull();
        theme!.Coefficient.Should().BeLessThan(-0.7);
        theme.SampleSize.Should().Be(Days);
    }

    [Fact]
    public async Task The_factor_key_carries_the_theme_label()
    {
        // The label is the user's own sentence, so it cannot be looked up in a resource table — the
        // key has to carry it, and the prefix is what tells the view model to format rather than map.
        var result = await Build(MoodOnly(), Themes("Ранкові пробіжки", Enumerable.Range(0, 12)))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        result.Select(c => c.FactorKey).Should().Contain("Theme:Ранкові пробіжки");
    }

    [Fact]
    public async Task A_theme_barely_present_is_not_reported()
    {
        // Twenty-four paired days where the theme appeared on three is not twenty-four observations
        // of anything: the coefficient would be three days against a baseline.
        var result = await Build(MoodOnly(), Themes("Зубний біль", [0, 1, 2]))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        Find(result, "Зубний біль").Should().BeNull();
    }

    [Fact]
    public async Task A_theme_present_almost_every_day_is_not_reported_either()
    {
        // The mirror image, and the more likely one: something written about constantly has almost
        // no days to compare against.
        var result = await Build(MoodOnly(), Themes("Робота", Enumerable.Range(0, Days - 3)))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        Find(result, "Робота").Should().BeNull();
    }

    [Fact]
    public async Task Exactly_at_the_spread_bar_the_theme_counts()
    {
        var result = await Build(
                MoodOnly(),
                Themes("Прогулянки", Enumerable.Range(0, CorrelationService.MinBinaryGroup)))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        Find(result, "Прогулянки").Should().NotBeNull();
    }

    [Fact]
    public async Task Days_without_a_mood_are_not_paired()
    {
        // The theme spans every written day, but only the first fourteen recorded a mood.
        var result = await Build(MoodOnly(count: 14), Themes("Сон", Enumerable.Range(0, 7)))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        Find(result, "Сон")!.SampleSize.Should().Be(14);
    }

    [Fact]
    public async Task Days_the_diary_was_not_written_on_are_not_days_without_the_theme()
    {
        // The series spans the indexed days, not the calendar. Padding the untouched days with zeros
        // would invent nineteen observations of "the theme was absent" out of nineteen blanks.
        var themes = new StubThemes(
            [new DiaryTheme("Настрій", Enumerable.Range(0, 3).Select(DateFor).ToList())],
            Enumerable.Range(0, 5).Select(DateFor).ToList());

        var result = await Build(MoodOnly(), themes).GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        Find(result, "Настрій").Should().BeNull("five written days cannot clear the fourteen-pair floor");
    }

    [Fact]
    public async Task With_no_themes_the_result_is_the_same_as_before_the_factor_existed()
    {
        var entries = MoodOnly();
        for (var i = 0; i < Days; i++)
        {
            entries[i].HabitCompletion = i / (double)(Days - 1);
        }

        var without = await Build(entries, new NullThemeClusterService()).GetMoodCorrelationsAsync(StatsRange.LastDays(Days));
        var withEmpty = await Build(entries, new StubThemes([], [])).GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        without.Should().ContainSingle();
        withEmpty.Should().BeEquivalentTo(without);
    }

    [Fact]
    public async Task Readiness_ignores_a_theme_that_would_be_skipped_for_want_of_spread()
    {
        // Otherwise the empty state promises an insight that the correlation pass then refuses to
        // show — worse than the blank it exists to explain.
        var entries = MoodOnly();
        var readiness = await Build(entries, Themes("Зубний біль", [0, 1, 2])).GetReadinessAsync(StatsRange.LastDays(Days));

        readiness.PairedDays.Should().Be(0);
    }

    [Fact]
    public async Task The_correction_family_grows_with_the_themes()
    {
        // Recorded on purpose rather than discovered later: testing more hypotheses does mean each
        // one is believed less, so switching AI on can push a borderline structured finding below the
        // three dots the UI needs. That is what Benjamini-Hochberg is for, and spec R14 asks for one
        // shared family. Change this test before changing that.
        var entries = MoodOnly();
        for (var i = 0; i < Days; i++)
        {
            // Deliberately imperfect: a coefficient of 1.0 gives a p-value of exactly zero, and
            // nothing multiplies zero into anything visible.
            entries[i].HabitCompletion = (i + i % 6 * 3) / 40d;
        }

        // Present on eight days spread evenly across the mood range, so the theme itself correlates
        // with nothing — it costs a test without earning one.
        var alone = await Build(entries, new NullThemeClusterService()).GetMoodCorrelationsAsync(StatsRange.LastDays(Days));
        var alongside = await Build(entries, Themes("Робота", [0, 1, 6, 7, 12, 13, 18, 19]))
            .GetMoodCorrelationsAsync(StatsRange.LastDays(Days));

        var habitAlone = alone.Single(c => c.FactorKey == CorrelationService.Factors.HabitCompletion);
        var habitAlongside = alongside.Single(c => c.FactorKey == CorrelationService.Factors.HabitCompletion);

        habitAlongside.PValue.Should().BeApproximately(habitAlone.PValue, 1e-12);
        habitAlongside.AdjustedPValue.Should().BeGreaterThan(habitAlone.AdjustedPValue);
    }

    private sealed class StubThemes(IReadOnlyList<DiaryTheme> themes, IReadOnlyList<DateTime> indexedDays)
        : IThemeClusterService
    {
        public Task<IReadOnlyList<DiaryTheme>> ClusterAsync(
            DateTime start, DateTime end, int maxThemes = 5, CancellationToken cancellationToken = default) =>
            Task.FromResult(themes);

        public Task<ThemeSummary> SummariseAsync(
            DateTime start, DateTime end, int maxThemes = 5, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ThemeSummary(themes, indexedDays));
    }
}
