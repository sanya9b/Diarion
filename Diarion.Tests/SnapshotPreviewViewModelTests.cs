using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Services;
using Diarion.Services.Ai.Reports;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The screen's whole job is to be trustworthy about one thing: that what it displays is what would
/// be sent. So the assertions are mostly about agreement — between the switches and the options the
/// builder is handed, and between the text on screen and the serializer's own output.
/// </summary>
public class SnapshotPreviewViewModelTests
{
    private readonly Mock<ISnapshotBuilder> _builder = new(MockBehavior.Strict);
    private readonly Mock<IProfileService> _profile = new();

    private readonly List<SnapshotOptions> _requested = new();
    private PeriodSnapshot _snapshot = Snapshot();

    public SnapshotPreviewViewModelTests()
    {
        _profile.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true });

        _builder
            .Setup(b => b.BuildAsync(
                It.IsAny<PeriodKind>(),
                It.IsAny<StatsRange>(),
                It.IsAny<SnapshotOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback((PeriodKind _, StatsRange _, SnapshotOptions o, CancellationToken _) => _requested.Add(o))
            .ReturnsAsync(() => _snapshot);
    }

    private SnapshotPreviewViewModel CreateVm() => new(_builder.Object, _profile.Object);

    /// <summary>A week of blank days unless <paramref name="written"/> puts words on one of them.</summary>
    private static PeriodSnapshot Snapshot(string? written = null) => new()
    {
        PeriodKind = "week",
        Start = "2026-08-03",
        End = "2026-08-09",
        DayCount = 7,
        Language = "uk",
        Currency = "UAH",
        Days = Enumerable.Range(0, 7)
            .Select(i => new SnapshotDay
            {
                Date = new DateTime(2026, 8, 3).AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Text = i == 0 ? written : null
            })
            .ToList()
    };

    private SnapshotOptions LastRequested => _requested[^1];

    [Fact]
    public async Task Loading_shows_the_period_and_the_payload()
    {
        var vm = CreateVm();
        vm.Range = new StatsRange(new DateTime(2026, 8, 3), new DateTime(2026, 8, 9));

        await vm.LoadAsync();

        vm.Json.Should().Be(SnapshotSerializer.ToJson(_snapshot));
        vm.PeriodText.Should().Contain(new DateTime(2026, 8, 3).ToString("d", CultureInfo.CurrentCulture));
        vm.PeriodText.Should().Contain(new DateTime(2026, 8, 9).ToString("d", CultureInfo.CurrentCulture));
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task The_size_line_counts_the_characters_that_would_be_sent()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.SizeText.Should().Contain("7");
        vm.SizeText.Should().Contain(vm.Json.Length.ToString("N0", CultureInfo.CurrentCulture));
    }

    [Fact]
    public async Task Loading_builds_once()
    {
        // Setting the switches from the profile must not count as the user flipping them, or every
        // open of the screen would serialize the period twice.
        var vm = CreateVm();

        await vm.LoadAsync();
        await vm.FlushAsync();

        _requested.Should().HaveCount(1);
    }

    [Fact]
    public async Task Both_private_fields_start_out()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.IncludeIntimateLife.Should().BeFalse();
        vm.IncludeCycle.Should().BeFalse();
        LastRequested.IncludeIntimateLife.Should().BeFalse();
        LastRequested.IncludeCycle.Should().BeFalse();
    }

    [Fact]
    public async Task Switching_intimate_life_on_rebuilds_with_it_in()
    {
        var vm = CreateVm();
        await vm.LoadAsync();

        vm.IncludeIntimateLife = true;
        await vm.FlushAsync();

        _requested.Should().HaveCount(2);
        LastRequested.IncludeIntimateLife.Should().BeTrue();
    }

    [Fact]
    public async Task Flipping_both_switches_rebuilds_once_with_both()
    {
        // Two switches a moment apart are one intent. Rebuilding twice would serialize a year of
        // diary for a payload nobody ever saw.
        var vm = CreateVm();
        await vm.LoadAsync();

        vm.IncludeIntimateLife = true;
        vm.IncludeCycle = true;
        await vm.FlushAsync();

        _requested.Should().HaveCount(2);
        LastRequested.IncludeIntimateLife.Should().BeTrue();
        LastRequested.IncludeCycle.Should().BeTrue();
    }

    [Fact]
    public async Task The_cycle_switch_is_offered_when_the_profile_tracks_a_cycle()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.IsCycleAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task The_cycle_switch_is_absent_for_a_profile_without_one()
    {
        _profile.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { Gender = GenderType.Male });
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.IsCycleAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Cycle_data_never_goes_out_for_a_profile_that_hides_the_switch()
    {
        // The switch is invisible, but the property is still settable — from a previous load, or from
        // a binding that outlived the profile change. The options are the last word, not the switch.
        _profile.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { Gender = GenderType.Male });
        var vm = CreateVm();
        await vm.LoadAsync();

        vm.IncludeCycle = true;
        await vm.FlushAsync();

        LastRequested.IncludeCycle.Should().BeFalse();
        vm.Options.IncludeCycle.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_the_cycle_off_at_the_profile_clears_a_switch_left_on()
    {
        var vm = CreateVm();
        await vm.LoadAsync();
        vm.IncludeCycle = true;
        await vm.FlushAsync();

        _profile.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = false });
        await vm.LoadAsync();

        vm.IncludeCycle.Should().BeFalse();
        LastRequested.IncludeCycle.Should().BeFalse();
    }

    [Fact]
    public async Task A_week_with_nothing_written_says_so()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.IsWordless.Should().BeTrue();
    }

    [Fact]
    public async Task One_written_day_is_enough_to_be_worth_sending()
    {
        _snapshot = Snapshot("Довгий день, але добрий.");
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.IsWordless.Should().BeFalse();
    }

    [Fact]
    public async Task Refreshing_rereads_the_diary()
    {
        // The screen claims to show the current payload; an entry edited in another tab has to land.
        var vm = CreateVm();
        await vm.LoadAsync();
        _snapshot = Snapshot("Дописано вже після того, як екран відкрився.");

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Json.Should().Be(SnapshotSerializer.ToJson(_snapshot));
        vm.IsWordless.Should().BeFalse();
    }

    [Fact]
    public async Task The_kind_and_the_window_reach_the_builder_unchanged()
    {
        var range = new StatsRange(new DateTime(2026, 4, 1), new DateTime(2026, 6, 30));
        var vm = CreateVm();
        vm.Kind = PeriodKind.Quarter;
        vm.Range = range;

        await vm.LoadAsync();

        _builder.Verify(
            b => b.BuildAsync(PeriodKind.Quarter, range, It.IsAny<SnapshotOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void The_default_window_is_the_week_that_has_already_finished()
    {
        // An unfinished period would preview one payload today and send a different one tomorrow.
        var vm = CreateVm();

        vm.Kind.Should().Be(PeriodKind.Week);
        vm.Range.Should().Be(PeriodBoundaries.LastClosed(PeriodKind.Week, DateTime.Today));
        PeriodBoundaries.IsClosed(vm.Range, DateTime.Today).Should().BeTrue();
    }

    [Fact]
    public async Task The_screen_stops_spinning_even_when_the_build_fails()
    {
        _builder
            .Setup(b => b.BuildAsync(
                It.IsAny<PeriodKind>(),
                It.IsAny<StatsRange>(),
                It.IsAny<SnapshotOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the diary would not open"));
        var vm = CreateVm();

        var load = async () => await vm.LoadAsync();

        await load.Should().ThrowAsync<InvalidOperationException>();
        vm.IsBusy.Should().BeFalse();
    }
}
