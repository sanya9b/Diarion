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

/// <summary>The settings bubble that records a period by its first and last day.</summary>
public class CycleRecordViewModelTests
{
    private readonly Mock<ICycleLogService> _cycleLogService = new();
    private readonly Mock<IProfileService> _profileService = new();

    public CycleRecordViewModelTests()
    {
        Profile(periodLength: 5);
        MarkedDates();
        Logs();
    }

    private void Logs(params CycleLog[] logs) =>
        _cycleLogService.Setup(s => s.GetLogsAsync()).ReturnsAsync(logs.ToList());

    private void Profile(int periodLength) =>
        _profileService.Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true, PeriodLength = periodLength });

    private void MarkedDates(params DateTime[] dates) =>
        _cycleLogService.Setup(s => s.GetMarkedDatesAsync()).ReturnsAsync(dates.ToList());

    private CycleRecordViewModel CreateVm() => new(_cycleLogService.Object, _profileService.Object);

    [Fact]
    public async Task Open_StartsOnToday_AndCapsTheDefaultEndAtToday()
    {
        var vm = CreateVm();

        await vm.OpenCommand.ExecuteAsync(null);

        vm.IsOpen.Should().BeTrue();
        vm.Start.Should().Be(DateTime.Today);
        vm.End.Should().Be(DateTime.Today, "the profile's five days would run into the future");
        vm.Days.Should().Be(1);
        vm.CanSave.Should().BeTrue();
    }

    [Fact]
    public async Task Open_OnTheDayTheHomeScreenShows_ShowsThatDaysSymptoms()
    {
        var day = DateTime.Today.AddDays(-2);
        Logs(new CycleLog { Date = day, Symptoms = { CycleSymptoms.Cramps, CycleSymptoms.Fatigue }, IsSymptomOnly = true });
        var vm = CreateVm();

        await vm.OpenCommand.ExecuteAsync(day);

        vm.SymptomDay.Should().Be(day);
        vm.Symptoms.Select(s => s.Key).Should().Equal(CycleSymptoms.All);
        vm.Symptoms.Where(s => s.IsSelected).Select(s => s.Key)
            .Should().BeEquivalentTo(new[] { CycleSymptoms.Cramps, CycleSymptoms.Fatigue });
        vm.CanLogSymptoms.Should().BeTrue();
        vm.SymptomsTitle.Should().NotBe(Diarion.Resources.Localization.AppResources.CycleSymptomsToday, "it is not today");
    }

    [Fact]
    public async Task Open_WithoutADay_ShowsTodaysSymptoms()
    {
        var vm = CreateVm();

        await vm.OpenCommand.ExecuteAsync(null);

        vm.SymptomDay.Should().Be(DateTime.Today);
        vm.SymptomsTitle.Should().Be(Diarion.Resources.Localization.AppResources.CycleSymptomsToday);
        vm.Symptoms.Should().HaveCount(CycleSymptoms.All.Length).And.OnlyContain(s => !s.IsSelected);
    }

    [Fact]
    public async Task ToggleSymptom_SavesTheDaysSymptomsAtOnce()
    {
        var day = DateTime.Today.AddDays(-1);
        Logs(new CycleLog { Date = day, Symptoms = { CycleSymptoms.Headache }, IsSymptomOnly = true });
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(day);

        await vm.ToggleSymptomCommand.ExecuteAsync(vm.Symptoms.Single(s => s.Key == CycleSymptoms.Cramps));

        _cycleLogService.Verify(s => s.SetSymptomsAsync(day,
            It.Is<IEnumerable<string>>(keys => keys.OrderBy(k => k).SequenceEqual(new[] { CycleSymptoms.Cramps, CycleSymptoms.Headache }.OrderBy(k => k)))),
            Times.Once);
        vm.Symptoms.Single(s => s.Key == CycleSymptoms.Cramps).FillColorKey.Should().Be("Theme_Berry");
    }

    [Fact]
    public async Task ADayStillToCome_HasNoSymptomsToNote()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(DateTime.Today.AddDays(3));

        await vm.ToggleSymptomCommand.ExecuteAsync(vm.Symptoms[0]);

        vm.CanLogSymptoms.Should().BeFalse();
        vm.Symptoms[0].IsSelected.Should().BeFalse();
        _cycleLogService.Verify(s => s.SetSymptomsAsync(It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task Open_Again_PutsTheBubbleAway()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(null);

        await vm.OpenCommand.ExecuteAsync(null);

        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task MovingTheFirstDay_DragsTheLastDayAlong_UntilTheUserPicksIt()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(null);

        vm.Start = DateTime.Today.AddDays(-10);
        vm.End.Should().Be(DateTime.Today.AddDays(-6), "five days from the profile, both ends counted");
        vm.Days.Should().Be(5);

        vm.End = DateTime.Today.AddDays(-8);
        vm.Start = DateTime.Today.AddDays(-9);

        vm.End.Should().Be(DateTime.Today.AddDays(-8), "the user chose the last day; it stays put");
        vm.Days.Should().Be(2);
    }

    [Fact]
    public async Task AFirstDayAfterTheChosenLastDay_PullsTheLastDayForward()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(null);
        vm.Start = DateTime.Today.AddDays(-10);
        vm.End = DateTime.Today.AddDays(-9);

        vm.Start = DateTime.Today.AddDays(-3);

        vm.End.Should().BeOnOrAfter(vm.Start);
        vm.End.Should().Be(DateTime.Today, "re-defaulted from the new first day, capped at today");
    }

    [Fact]
    public async Task Save_RecordsTheChosenRange_BroadcastsAndCloses()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(null);
        vm.Start = DateTime.Today.AddDays(-6);
        vm.End = DateTime.Today.AddDays(-4);

        await vm.SaveCommand.ExecuteAsync(null);

        _cycleLogService.Verify(s => s.AddEpisodeAsync(DateTime.Today.AddDays(-6), 3), Times.Once);
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Save_RefusesARunLongerThanAPeriod()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(null);
        vm.Start = DateTime.Today.AddDays(-30);
        vm.End = DateTime.Today.AddDays(-30 + CycleRecordViewModel.MaxPeriodDays);

        vm.CanSave.Should().BeFalse();
        await vm.SaveCommand.ExecuteAsync(null);

        _cycleLogService.Verify(s => s.AddEpisodeAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
        vm.IsOpen.Should().BeTrue("nothing was saved, so the bubble stays for a correction");
    }

    [Fact]
    public async Task Open_NamesTheLastRecordedPeriod()
    {
        var start = DateTime.Today.AddDays(-20);
        MarkedDates(Enumerable.Range(0, 4).Select(i => start.AddDays(i)).ToArray());
        var vm = CreateVm();

        await vm.OpenCommand.ExecuteAsync(null);

        vm.HasLastRecorded.Should().BeTrue();
        vm.LastRecordedText.Should().Contain(CycleViewModel.FormatRange(start, start.AddDays(3)));
    }

    [Fact]
    public async Task Open_WithNothingRecorded_ShowsNoLastPeriod()
    {
        var vm = CreateVm();

        await vm.OpenCommand.ExecuteAsync(null);

        vm.HasLastRecorded.Should().BeFalse();
        vm.LastRecordedText.Should().BeEmpty();
    }

    [Fact]
    public async Task Close_RecordsNothing()
    {
        var vm = CreateVm();
        await vm.OpenCommand.ExecuteAsync(null);

        vm.CloseCommand.Execute(null);

        vm.IsOpen.Should().BeFalse();
        _cycleLogService.Verify(s => s.AddEpisodeAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void FormatRange_NamesOneDayOnce()
    {
        var day = new DateTime(2026, 10, 3);

        CycleViewModel.FormatRange(day, day).Should().Be(day.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture));
        CycleViewModel.FormatRange(day, day.AddDays(4)).Should().Contain("–");
    }
}
