using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Models.Widgets;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Services.Widgets;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The coordinator between the diary's saves and the widget file (spec 20, R4/R9). The builder is a
/// stub that records the level it was asked for; the publisher records what it was handed.
/// </summary>
public class WidgetSnapshotCoordinatorTests : IDisposable
{
    private readonly DatabaseContext _db;
    private readonly ProfileService _profiles;
    private readonly Mock<IAppLockService> _lock = new();
    private readonly Mock<IWidgetSnapshotPublisher> _publisher = new();
    private readonly Mock<IWidgetSnapshotService> _snapshots = new();
    private readonly List<WidgetPrivacyLevel> _builtAt = new();
    private readonly List<string> _published = new();
    private int _cleared;

    public WidgetSnapshotCoordinatorTests()
    {
        _db = new DatabaseContext(useInMemory: true);
        _profiles = new ProfileService(_db);
        _publisher.SetupGet(p => p.IsSupported).Returns(true);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<string>()))
            .Callback<string>(_published.Add)
            .Returns(Task.CompletedTask);
        _publisher.Setup(p => p.ClearAsync())
            .Callback(() => _cleared++)
            .Returns(Task.CompletedTask);
        _snapshots.Setup(s => s.BuildAsync(It.IsAny<DateTime>(), It.IsAny<WidgetPrivacyLevel>()))
            .Returns<DateTime, WidgetPrivacyLevel>((day, level) =>
            {
                _builtAt.Add(level);
                return Task.FromResult(Stub(day, level));
            });
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
        _db.Dispose();
    }

    private static WidgetSnapshot Stub(DateTime day, WidgetPrivacyLevel level) => new()
    {
        Date = day.ToString("yyyy-MM-dd"),
        GeneratedAt = DateTimeOffset.Now,
        Locale = "en",
        PrivacyLevel = level,
        Labels = new WidgetLabels { AppName = "Diarion", Open = "Open", DateShort = "d", Updated = "u" }
    };

    private WidgetSnapshotCoordinator Create(TimeSpan? debounce = null)
        => new(_snapshots.Object, _publisher.Object, _profiles, _lock.Object, debounce: debounce ?? TimeSpan.FromMilliseconds(30));

    private async Task SetChosenLevelAsync(WidgetPrivacyLevel? level)
    {
        var profile = await _profiles.GetUserProfileAsync();
        profile.WidgetPrivacyLevel = level;
        await _profiles.SaveUserProfileAsync(profile);
    }

    [Fact]
    public async Task Refresh_builds_at_the_level_the_policy_resolves()
    {
        _lock.SetupGet(l => l.IsLockEnabled).Returns(true);

        await Create().RefreshAsync();

        _builtAt.Should().Equal(WidgetPrivacyLevel.ButtonOnly);
        _published.Should().ContainSingle().Which.Should().Contain("\"privacyLevel\":\"ButtonOnly\"");
    }

    [Fact]
    public async Task Refresh_honours_a_chosen_level_over_the_lock()
    {
        _lock.SetupGet(l => l.IsLockEnabled).Returns(true);
        await SetChosenLevelAsync(WidgetPrivacyLevel.WithText);

        await Create().RefreshAsync();

        _builtAt.Should().Equal(WidgetPrivacyLevel.WithText);
    }

    [Fact]
    public async Task Lock_switched_on_forgets_the_choice_clears_and_publishes_button_only()
    {
        await SetChosenLevelAsync(WidgetPrivacyLevel.WithText);
        _lock.SetupGet(l => l.IsLockEnabled).Returns(true);

        await Create().OnLockEnabledAsync();

        (await _profiles.GetUserProfileAsync()).WidgetPrivacyLevel.Should().BeNull();
        _cleared.Should().Be(1);
        _builtAt.Should().Equal(WidgetPrivacyLevel.ButtonOnly);
        _published.Should().ContainSingle();
    }

    [Fact]
    public async Task Saves_coalesce_into_one_publish_after_the_debounce()
    {
        var coordinator = Create(TimeSpan.FromMilliseconds(80));
        coordinator.Start();
        await Task.Delay(250);
        _published.Clear();

        WeakReferenceMessenger.Default.Send(new DocumentChangedMessage(DocumentKind.Diary, "a"));
        WeakReferenceMessenger.Default.Send(new TodoStoreChangedMessage());
        WeakReferenceMessenger.Default.Send(new WidgetRefreshRequestedMessage());
        await Task.Delay(400);

        _published.Should().ContainSingle("three saves within the window are one write");
    }

    [Fact]
    public async Task Note_saves_do_not_touch_the_widget()
    {
        var coordinator = Create();
        coordinator.Start();
        await Task.Delay(200);
        _published.Clear();

        WeakReferenceMessenger.Default.Send(new DocumentChangedMessage(DocumentKind.Note, "n"));
        await Task.Delay(200);

        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task Lock_message_is_handled_without_the_debounce()
    {
        await SetChosenLevelAsync(WidgetPrivacyLevel.Counts);
        var coordinator = Create(TimeSpan.FromSeconds(30));
        coordinator.Start();

        WeakReferenceMessenger.Default.Send(new AppLockChangedMessage(true));
        await Task.Delay(300);

        _cleared.Should().Be(1);
        (await _profiles.GetUserProfileAsync()).WidgetPrivacyLevel.Should().BeNull();
    }

    [Fact]
    public async Task Flush_runs_the_pending_refresh_now()
    {
        var coordinator = Create(TimeSpan.FromSeconds(30));
        coordinator.RequestRefresh();

        await coordinator.FlushAsync();

        _published.Should().ContainSingle();
    }

    [Fact]
    public async Task Unsupported_platform_never_builds_or_publishes()
    {
        _publisher.SetupGet(p => p.IsSupported).Returns(false);
        var coordinator = Create();

        coordinator.Start();
        coordinator.RequestRefresh();
        await coordinator.RefreshAsync();
        await coordinator.OnLockEnabledAsync();
        await coordinator.FlushAsync();
        await Task.Delay(100);

        _builtAt.Should().BeEmpty();
        _published.Should().BeEmpty();
        _cleared.Should().Be(0);
    }

    [Fact]
    public async Task A_failing_builder_is_logged_not_thrown()
    {
        _snapshots.Setup(s => s.BuildAsync(It.IsAny<DateTime>(), It.IsAny<WidgetPrivacyLevel>()))
            .ThrowsAsync(new InvalidOperationException("database closed"));

        var act = async () => await Create().RefreshAsync();

        await act.Should().NotThrowAsync();
        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_task_saved_through_the_service_republishes()
    {
        // The detail page saves without telling the planner; the store itself must say so (R4).
        var coordinator = Create();
        coordinator.Start();
        await Task.Delay(200);
        _published.Clear();

        var todos = new TodoService(_db, null);
        await todos.SaveTodoAsync(new TodoItem { TargetDate = DateTime.Today, TaskDescription = "from the detail page" });
        await Task.Delay(300);

        _published.Should().ContainSingle();
    }
}
