using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class CycleViewModelTests
{
    private readonly Mock<ICycleLogService> _cycleLogService = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<INavigationService> _navigation = new();
    private readonly Mock<IDialogService> _dialogs = new();

    public CycleViewModelTests()
    {
        _profileService.Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true });
        MarkedDates();
    }

    private void MarkedDates(params DateTime[] dates) =>
        _cycleLogService.Setup(s => s.GetMarkedDatesAsync()).ReturnsAsync(dates.ToList());

    /// <summary>Period days for episodes starting the given number of days before today.</summary>
    private static DateTime[] Episodes(int length, params int[] startsDaysAgo) =>
        startsDaysAgo
            .SelectMany(start => Enumerable.Range(0, length).Select(i => DateTime.Today.AddDays(-start + i)))
            .ToArray();

    private CycleViewModel CreateVm() =>
        new(_cycleLogService.Object, _profileService.Object, _navigation.Object, _dialogs.Object);

    [Fact]
    public async Task Load_WithNothingRecorded_ShowsTheEmptyState()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.HasData.Should().BeFalse();
        vm.HasNoData.Should().BeTrue();
        vm.Episodes.Should().BeEmpty();
        vm.NextPeriodText.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_ListsEpisodesNewestFirst()
    {
        MarkedDates(Episodes(3, 60, 32, 4));
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.Episodes.Should().HaveCount(3);
        vm.Episodes.Select(e => e.Start).Should().BeInDescendingOrder();
        vm.Episodes.First().Start.Should().Be(DateTime.Today.AddDays(-4));
    }

    [Fact]
    public async Task Load_ShowsTheIntervalToTheFollowingPeriod()
    {
        MarkedDates(Episodes(3, 32, 4));
        var vm = CreateVm();

        await vm.LoadAsync();

        // The newest has nothing after it, so only the older one carries an interval.
        vm.Episodes.First().IntervalText.Should().BeEmpty();
        vm.Episodes.Last().IntervalText.Should().Be(string.Format(AppResources.CycleIntervalFormat, 28));
    }

    [Fact]
    public async Task Load_OneEpisode_NamesTheProfileSettingAsItsBasis()
    {
        MarkedDates(Episodes(3, 4));
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.HasData.Should().BeTrue();
        vm.BasisText.Should().Be(string.Format(AppResources.CycleBasisProfileDefaultFormat, 28));
    }

    [Fact]
    public async Task Load_SeveralEpisodes_NamesTheRecordedCycles()
    {
        MarkedDates(Episodes(3, 60, 32, 4));
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.BasisText.Should().Be(string.Format(AppResources.CycleBasisAveragedFormat, 2, "28"));
    }

    [Fact]
    public async Task AddEpisode_RecordsUsingTheProfilePeriodLength()
    {
        var vm = CreateVm();
        await vm.LoadAsync();
        vm.NewEpisodeStart = DateTime.Today.AddDays(-2);

        await vm.AddEpisodeCommand.ExecuteAsync(null);

        _cycleLogService.Verify(s => s.AddEpisodeAsync(DateTime.Today.AddDays(-2), 5), Times.Once);
    }

    [Fact]
    public async Task DeleteEpisode_WithoutConfirmation_DoesNothing()
    {
        MarkedDates(Episodes(3, 4));
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
        var vm = CreateVm();
        await vm.LoadAsync();

        await vm.DeleteEpisodeCommand.ExecuteAsync(vm.Episodes.First());

        _cycleLogService.Verify(s => s.RemoveEpisodeAsync(It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task DeleteEpisode_Confirmed_RemovesThatEpisode()
    {
        MarkedDates(Episodes(3, 4));
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
        var vm = CreateVm();
        await vm.LoadAsync();

        await vm.DeleteEpisodeCommand.ExecuteAsync(vm.Episodes.First());

        _cycleLogService.Verify(s => s.RemoveEpisodeAsync(DateTime.Today.AddDays(-4)), Times.Once);
    }

    [Fact]
    public void MaximumDate_IsToday()
    {
        // The picker must not offer a start date that has not happened.
        CreateVm().MaximumDate.Should().Be(DateTime.Today);
    }
}
