using System;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Reading a task out of ordinary typing. Pure, so every trap is pinned here rather than through the form.
/// </summary>
public class TaskInputParserTests
{
    // A Wednesday, chosen so "next Thursday" is tomorrow and "next Tuesday" is nearly a week away —
    // both directions of the weekday walk get exercised by real cases.
    private static readonly DateTime Today = new(2026, 8, 12);

    private static ParsedTaskInput Parse(string text) => TaskInputParser.Parse(text, Today);

    // --- the three shapes the feature was asked for ---

    [Fact]
    public void AWeeklyDayWithATimeAndATask()
    {
        var result = Parse("щовівторка о 18:00 теніс");

        result.Description.Should().Be("теніс");
        result.Recurrence!.Kind.Should().Be(RecurrenceKind.Weekly);
        result.Recurrence.DaysOfWeek.Should().Equal((int)DayOfWeek.Tuesday);
        result.TimeOfDay.Should().Be(new TimeSpan(18, 0, 0));
        result.Date.Should().BeNull();
    }

    [Fact]
    public void TheOtherWayOfSayingEveryThursday()
    {
        var result = Parse("кожного четверга на 10:00 плавання");

        result.Description.Should().Be("плавання");
        result.Recurrence!.DaysOfWeek.Should().Equal((int)DayOfWeek.Thursday);
        result.TimeOfDay.Should().Be(new TimeSpan(10, 0, 0));
    }

    [Fact]
    public void ANamedDateIsAOneOffEvenThoughAWeekdayIsSaidToo()
    {
        // The trap the whole disambiguation rule exists for: "у четвер" here confirms which day the 26th
        // is, it does not ask for every Thursday. Read as a rule, this would put a birthday in the diary
        // once a week forever.
        var result = Parse("в четвер 26 серпня день народження на 18:00");

        result.Description.Should().Be("день народження");
        result.Recurrence.Should().BeNull();
        result.Date.Should().Be(new DateTime(2026, 8, 26));
        result.TimeOfDay.Should().Be(new TimeSpan(18, 0, 0));
    }

    // --- recurrence ---

    [Fact]
    public void TwoWeekdaysAreOneRuleAndNotTwo()
    {
        var result = Parse("щовівторка і щочетверга басейн");

        result.Description.Should().Be("басейн");
        result.Recurrence!.DaysOfWeek.Should().BeEquivalentTo(new[] { (int)DayOfWeek.Tuesday, (int)DayOfWeek.Thursday });
    }

    [Fact]
    public void AListAfterOneMarkerIsAlsoOneRule()
    {
        var result = Parse("по вівторках та четвергах басейн");

        result.Description.Should().Be("басейн");
        result.Recurrence!.DaysOfWeek.Should().BeEquivalentTo(new[] { (int)DayOfWeek.Tuesday, (int)DayOfWeek.Thursday });
    }

    [Theory]
    [InlineData("щодня пити воду")]
    [InlineData("кожен день пити воду")]
    [InlineData("daily пити воду")]
    public void DailyInItsSeveralForms(string text)
    {
        var result = Parse(text);

        result.Recurrence!.Kind.Should().Be(RecurrenceKind.Daily);
        result.Description.Should().Be("пити воду");
    }

    [Fact]
    public void EveryNDays()
    {
        var result = Parse("кожні 3 дні поливати квіти");

        result.Recurrence!.Kind.Should().Be(RecurrenceKind.IntervalDays);
        result.Recurrence.EveryN.Should().Be(3);
        result.Description.Should().Be("поливати квіти");
    }

    [Fact]
    public void MonthlyOnADate()
    {
        var result = Parse("щомісяця 15-го оренда");

        result.Recurrence!.Kind.Should().Be(RecurrenceKind.MonthlyByDay);
        result.Recurrence.DayOfMonth.Should().Be(15);
        result.Description.Should().Be("оренда");
    }

    [Fact]
    public void WeeklyWithNoDayNamedMeansTheDayItAlreadySitsOn()
    {
        var result = Parse("щотижня звіт");

        result.Recurrence!.Kind.Should().Be(RecurrenceKind.Weekly);
        result.Recurrence.DaysOfWeek.Should().Equal((int)Today.DayOfWeek);
    }

    // --- dates ---

    [Fact]
    public void Tomorrow()
    {
        var result = Parse("завтра подзвонити мамі");

        result.Date.Should().Be(Today.AddDays(1));
        result.Description.Should().Be("подзвонити мамі");
    }

    [Fact]
    public void ABareWeekdayMeansTheNextOne()
    {
        var result = Parse("у п'ятницю забрати посилку");

        result.Date.Should().Be(new DateTime(2026, 8, 14));
        result.Description.Should().Be("забрати посилку");
    }

    [Fact]
    public void SayingTodaysOwnWeekdayMeansNextWeekNotThisMorning()
    {
        // Today is a Wednesday. "у середу" has to mean the one coming, or a task typed on Wednesday
        // evening lands in a day that is already over.
        var result = Parse("у середу прибирання");

        result.Date.Should().Be(Today.AddDays(7));
    }

    [Fact]
    public void ADayAndMonthAlreadyPastRollsToNextYear()
    {
        var result = Parse("3 січня зустріч");

        result.Date.Should().Be(new DateTime(2027, 1, 3));
    }

    [Fact]
    public void ANumericDate()
    {
        var result = Parse("26.08 зустріч");

        result.Date.Should().Be(new DateTime(2026, 8, 26));
    }

    [Fact]
    public void AnImpossibleDateIsNotADate()
    {
        // 31 February must not silently become the 3rd of March.
        var result = Parse("31.02 зустріч");

        result.Date.Should().BeNull();
    }

    // --- times ---

    [Theory]
    [InlineData("о 18:30", 18, 30)]
    [InlineData("на 9:05", 9, 5)]
    [InlineData("at 7:15", 7, 15)]
    [InlineData("о 18", 18, 0)]
    [InlineData("at 6pm", 18, 0)]
    public void TimesInTheirSeveralForms(string fragment, int hour, int minute)
    {
        var result = Parse($"{fragment} теніс");

        result.TimeOfDay.Should().Be(new TimeSpan(hour, minute, 0));
        result.Description.Should().Be("теніс");
    }

    [Fact]
    public void ABareNumberIsNotATime()
    {
        // "Купити 5 яблук" must stay a task about five apples.
        var result = Parse("купити 5 яблук");

        result.TimeOfDay.Should().BeNull();
        result.Description.Should().Be("купити 5 яблук");
    }

    [Fact]
    public void AnImpossibleClockTimeIsNotATime()
    {
        var result = Parse("рейс 99:99");

        result.TimeOfDay.Should().BeNull();
    }

    // --- a stretch of the day ---

    [Theory]
    [InlineData("з 13:00 до 16:00")]
    [InlineData("від 13:00 до 16:00")]
    [InlineData("13:00-16:00")]
    [InlineData("13:00 – 16:00")]
    [InlineData("з 13 до 16")]
    [InlineData("from 13:00 to 16:00")]
    public void ARangeInItsSeveralForms(string fragment)
    {
        var result = Parse($"{fragment} зустріч");

        result.TimeOfDay.Should().Be(new TimeSpan(13, 0, 0));
        result.EndTimeOfDay.Should().Be(new TimeSpan(16, 0, 0));
        result.Description.Should().Be("зустріч");
    }

    [Fact]
    public void ARangeInTwelveHourClock()
    {
        var result = Parse("from 1pm to 4pm standup");

        result.TimeOfDay.Should().Be(new TimeSpan(13, 0, 0));
        result.EndTimeOfDay.Should().Be(new TimeSpan(16, 0, 0));
        result.Description.Should().Be("standup");
    }

    [Fact]
    public void ARangeTakesBothOfItsClockTimesOutOfTheTitle()
    {
        // The reason the range runs before the single-time patterns: one of those would take the "13:00"
        // and leave "з до 16:00" as the name of the task.
        var result = Parse("з 13:00 до 16:00 зустріч з клієнтом");

        result.Description.Should().Be("зустріч з клієнтом");
        result.Matched.Should().Contain("з 13:00 до 16:00");
    }

    [Fact]
    public void APlainTimeStillHasNoEnd()
    {
        var result = Parse("о 15:00 дзвінок");

        result.TimeOfDay.Should().Be(new TimeSpan(15, 0, 0));
        result.EndTimeOfDay.Should().BeNull();
    }

    [Fact]
    public void ARangeReadBackwardsIsLeftAloneEntirely()
    {
        // Not half-applied. Taking the 16:00 and leaving "до 13:00" in the title is a worse answer than
        // reading nothing: the user sees a time they did not intend and a title they have to repair.
        var result = Parse("з 16:00 до 13:00 зустріч");

        result.TimeOfDay.Should().BeNull();
        result.EndTimeOfDay.Should().BeNull();
        result.Description.Should().Be("з 16:00 до 13:00 зустріч");
    }

    [Fact]
    public void ARangeEndingWhereItStartsIsNotARange()
    {
        var result = Parse("з 13:00 до 13:00 зустріч");

        result.FoundAnything.Should().BeFalse();
    }

    [Theory]
    [InlineData("купити 2-3 яблука")]
    [InlineData("почитати від 5 до 10 сторінок")]
    [InlineData("розім'ятися 5-10 хвилин")]
    public void CountingIsNotAStretchOfTheDay(string text)
    {
        // A dash between bare numbers is nearly always a quantity, and "до" between them can be one too.
        var result = Parse(text);

        result.TimeOfDay.Should().BeNull();
        result.EndTimeOfDay.Should().BeNull();
        result.Description.Should().Be(text);
    }

    [Fact]
    public void ARangeSurvivesAlongsideARepeat()
    {
        var result = Parse("щовівторка з 13:00 до 16:00 курси");

        result.Recurrence!.DaysOfWeek.Should().Equal((int)DayOfWeek.Tuesday);
        result.TimeOfDay.Should().Be(new TimeSpan(13, 0, 0));
        result.EndTimeOfDay.Should().Be(new TimeSpan(16, 0, 0));
        result.Description.Should().Be("курси");
    }

    [Fact]
    public void AnImpossibleEndIsNotARange()
    {
        var result = Parse("зустріч 13:00-45:00");

        result.EndTimeOfDay.Should().BeNull();
    }

    // --- English ---

    [Fact]
    public void TheEnglishGrammarRunsOnTheSameInput()
    {
        var result = Parse("every tuesday at 18:00 tennis");

        result.Recurrence!.DaysOfWeek.Should().Equal((int)DayOfWeek.Tuesday);
        result.TimeOfDay.Should().Be(new TimeSpan(18, 0, 0));
        result.Description.Should().Be("tennis");
    }

    [Fact]
    public void EnglishPluralWeekdaysNeedNoMarker()
    {
        var result = Parse("mondays gym");

        result.Recurrence!.DaysOfWeek.Should().Equal((int)DayOfWeek.Monday);
        result.Description.Should().Be("gym");
    }

    // --- leaving ordinary text alone ---

    [Fact]
    public void PlainTextIsUntouched()
    {
        var result = Parse("подзвонити стоматологу");

        result.FoundAnything.Should().BeFalse();
        result.Description.Should().Be("подзвонити стоматологу");
        result.Matched.Should().BeEmpty();
    }

    [Fact]
    public void EmptyInputIsSafe()
    {
        TaskInputParser.Parse(null, Today).Description.Should().BeEmpty();
        TaskInputParser.Parse("   ", Today).FoundAnything.Should().BeFalse();
    }

    [Fact]
    public void TheOriginalIsKeptSoAWrongReadingCanBeUndone()
    {
        var result = Parse("щовівторка теніс");

        result.OriginalText.Should().Be("щовівторка теніс");
        result.Matched.Should().Contain("щовівторка");
    }

    [Fact]
    public void AnApostropheTypedAnyWayStillMatches()
    {
        Parse("щоп’ятниці звіт").Recurrence!.DaysOfWeek.Should().Equal((int)DayOfWeek.Friday);
        Parse("щоп'ятниці звіт").Recurrence!.DaysOfWeek.Should().Equal((int)DayOfWeek.Friday);
    }

    [Fact]
    public void CuttingThePhraseOutDoesNotLeaveDebrisBehind()
    {
        var result = Parse("теніс, щовівторка о 18:00");

        result.Description.Should().Be("теніс");
    }
}
