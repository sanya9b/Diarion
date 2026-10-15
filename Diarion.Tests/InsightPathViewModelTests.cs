using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Models.Ai;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.InsightPath;
using Diarion.Services.Store;
using Diarion.ViewModels;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>Spec 17, R1.5–R1.6: the strip's view model maps the service state to a count, refreshes on
/// the messages a paired day can arrive through, shows the first hypothesis once, and opens statistics
/// on the window it counted.</summary>
public class InsightPathViewModelTests
{
    private const string Sleep = CorrelationService.Factors.SleepDuration;

    private static InsightPathState Walking(int paired, string? factor = Sleep) =>
        new(true, paired, 14, factor, InsightPathMilestones.Reached(paired), InsightPathMilestones.Next(paired), null);

    private static InsightPathState Arrived(bool visible = true) =>
        new(visible, 14, 14, Sleep, 14, null,
            new MoodCorrelation { FactorKey = Sleep, Coefficient = 0.42, Confidence = 4, SampleSize = 14 });

    private static (InsightPathViewModel Vm, Mock<IInsightPathService> Path, Mock<INavigationService> Nav) Build(InsightPathState state)
    {
        var path = new Mock<IInsightPathService>();
        path.Setup(p => p.GetAsync()).ReturnsAsync(state);
        path.Setup(p => p.HideUntilNextMilestoneAsync()).Returns(Task.CompletedTask);

        var nav = new Mock<INavigationService>();
        nav.Setup(n => n.NavigateToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>?>()))
           .Returns(Task.CompletedTask);

        // The dispatcher runs inline: the tests are the main thread.
        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(d => d.InvokeOnMainThread(It.IsAny<Action>())).Callback<Action>(a => a());

        var entitlements = new Mock<IEntitlementService>();

        return (new InsightPathViewModel(path.Object, nav.Object, dispatcher.Object, entitlements.Object), path, nav);
    }

    [Fact]
    public async Task Refresh_MapsTheStateToTheCount()
    {
        var (vm, path, _) = Build(Walking(9));
        using var _ = vm;

        await vm.RefreshAsync();

        vm.IsVisible.Should().BeTrue();
        vm.PairedDays.Should().Be(9);
        vm.RequiredDays.Should().Be(14);
        vm.ProgressText.Should().Be(string.Format(AppResources.InsightPathProgressFormat, 9, 14));
        vm.AccessibleSummary.Should().Be(string.Format(AppResources.InsightPathAccessibleFormat, 9, 14));
        vm.HasHypothesis.Should().BeFalse();
        path.Verify(p => p.HideUntilNextMilestoneAsync(), Times.Never, "walking the path hides nothing");
    }

    [Fact]
    public async Task Refresh_ClampsThePairedDaysToTheBar()
    {
        var (vm, _, _) = Build(new InsightPathState(true, 20, 14, Sleep, 14, null, null));
        using var _ = vm;

        await vm.RefreshAsync();

        vm.PairedDays.Should().Be(14, "the strip has fourteen notches and nothing past them");
    }

    [Fact]
    public async Task Arrived_ShowsTheHypothesisInTheStatisticsWords_AndRetiresThePathForNextLaunch()
    {
        var (vm, path, _) = Build(Arrived());
        using var _ = vm;

        await vm.RefreshAsync();

        vm.IsVisible.Should().BeTrue();
        vm.HasHypothesis.Should().BeTrue();
        vm.HypothesisText.Should().Contain(FactorNames.Display(Sleep))
            .And.Contain("↑")
            .And.Contain(string.Format(AppResources.StatsCorrelationEvidenceFormat, 14));
        path.Verify(p => p.HideUntilNextMilestoneAsync(), Times.Once, "the last milestone is shown once");
    }

    [Fact]
    public async Task Arrived_StaysOnScreenForTheSessionAfterTheServiceHasHiddenIt()
    {
        var (vm, path, _) = Build(Arrived());
        using var _ = vm;
        await vm.RefreshAsync();

        // The service now reports the path as finished; this instance keeps the row.
        path.Setup(p => p.GetAsync()).ReturnsAsync(Arrived(visible: false));
        await vm.RefreshAsync();

        vm.IsVisible.Should().BeTrue();
        vm.HasHypothesis.Should().BeTrue();
        path.Verify(p => p.HideUntilNextMilestoneAsync(), Times.Once, "hidden once, not on every refresh");
    }

    [Fact]
    public async Task Arrived_AlreadyHiddenBeforeThisInstance_StaysHidden()
    {
        // The next launch: the service says hidden, nothing was shown by this instance.
        var (vm, path, _) = Build(Arrived(visible: false));
        using var _ = vm;

        await vm.RefreshAsync();

        vm.IsVisible.Should().BeFalse();
        path.Verify(p => p.HideUntilNextMilestoneAsync(), Times.Never);
    }

    [Fact]
    public async Task Open_NavigatesFirstAndThenAsksStatisticsForTheSameWindow()
    {
        var (vm, _, nav) = Build(Walking(14));
        using var _ = vm;

        var received = new List<StatsRange>();
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<StatisticsRangeRequestedMessage>(recipient, (_, m) => received.Add(m.Range));
        try
        {
            await vm.OpenCommand.ExecuteAsync(null);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        nav.Verify(n => n.NavigateToAsync("//StatisticsPage", It.IsAny<IDictionary<string, object>?>()), Times.Once);
        received.Should().ContainSingle().Which.Should().Be(StatsRange.LastDays(InsightPathService.WindowDays));
    }

    [Fact]
    public async Task DiaryChanges_RefreshAfterTheDebounce_NotesDoNot()
    {
        var (vm, path, _) = Build(Walking(1));
        using var _ = vm;

        WeakReferenceMessenger.Default.Send(new DocumentChangedMessage(EmbeddingSourceKind.Diary, Guid.NewGuid().ToString()));
        WeakReferenceMessenger.Default.Send(new DocumentChangedMessage(EmbeddingSourceKind.Diary, Guid.NewGuid().ToString()));
        await vm.FlushAsync();

        path.Verify(p => p.GetAsync(), Times.Once, "two saves in one burst are one readiness pass");

        WeakReferenceMessenger.Default.Send(new DocumentChangedMessage("note", Guid.NewGuid().ToString()));
        await vm.FlushAsync();

        path.Verify(p => p.GetAsync(), Times.Once, "a note never pairs with a mood");
    }

    [Fact]
    public async Task TodoAndCycleChanges_RefreshToo()
    {
        var (vm, path, _) = Build(Walking(1));
        using var _ = vm;

        WeakReferenceMessenger.Default.Send(new TodoChangedMessage(DateTime.Today));
        await vm.FlushAsync();
        WeakReferenceMessenger.Default.Send(new CycleLogChangedMessage(DateTime.Today));
        await vm.FlushAsync();

        path.Verify(p => p.GetAsync(), Times.Exactly(2));
    }
}
