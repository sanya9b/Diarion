using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Services.Store;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>Spec 15, R1.6 and AC10: reminders to write stop while writing is closed.</summary>
public class ReminderAccessCoordinatorTests
{
    private static readonly AccessState Subscribed = new(true, AccessTier.Base, AccessSource.Subscription, null, true);

    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IProfileService> _profiles = new();
    private readonly Mock<IHabitService> _habits = new();
    private readonly InMemoryEntitlementStore _store = new();

    private readonly HabitDefinition _dailyHabit = new() { Id = Guid.NewGuid(), Name = "Read", ReminderTime = new TimeSpan(9, 0, 0) };
    private readonly HabitDefinition _weeklyHabit = new()
    {
        Id = Guid.NewGuid(),
        Name = "Gym",
        ReminderTime = new TimeSpan(18, 30, 0),
        Schedule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new List<int> { 1, 3 } }
    };
    private readonly HabitDefinition _silentHabit = new() { Id = Guid.NewGuid(), Name = "Water" };
    private readonly HarmfulHabitTracker _tracker = new() { Id = Guid.NewGuid(), HarmfulHabitName = "Smoking", ReminderTime = new TimeSpan(21, 0, 0) };

    public ReminderAccessCoordinatorTests()
    {
        _profiles.Setup(p => p.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile { IsDailyReminderEnabled = true, DailyReminderTime = new TimeSpan(20, 0, 0) });
        _habits.Setup(h => h.GetLiveHabitDefinitionsAsync())
            .ReturnsAsync(() => new List<HabitDefinition> { _dailyHabit, _weeklyHabit, _silentHabit });
        _habits.Setup(h => h.GetHarmfulHabitTrackersAsync())
            .ReturnsAsync(() => new List<HarmfulHabitTracker> { _tracker });
    }

    private ReminderAccessCoordinator Create(FixedEntitlementService entitlements) =>
        new(entitlements, _notifications.Object, _profiles.Object, _habits.Object, _store);

    [Fact]
    public async Task Going_read_only_cancels_the_daily_and_every_habit_reminder()
    {
        var coordinator = Create(new FixedEntitlementService(Subscribed));
        await coordinator.StartAsync();

        await coordinator.ApplyAsync(AccessState.ReadOnly);

        _notifications.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
        _notifications.Verify(n => n.CancelHabitReminder(_dailyHabit.Id), Times.Once);
        _notifications.Verify(n => n.CancelHabitReminder(_weeklyHabit.Id), Times.Once);
        _notifications.Verify(n => n.CancelHabitReminder(_tracker.Id), Times.Once);
        _notifications.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task Task_reminders_are_left_alone()
    {
        var coordinator = Create(new FixedEntitlementService(Subscribed));

        await coordinator.ApplyAsync(AccessState.ReadOnly);

        _notifications.Verify(n => n.CancelTodoReminder(It.IsAny<Guid>()), Times.Never);
        _notifications.Verify(n => n.CancelRepeatingTaskReminder(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Access_returning_schedules_everything_again_from_the_same_settings()
    {
        var coordinator = Create(new FixedEntitlementService(AccessState.ReadOnly));
        await coordinator.StartAsync();

        await coordinator.ApplyAsync(Subscribed);

        _notifications.Verify(n => n.ScheduleDailyJournalReminder(new TimeSpan(20, 0, 0)), Times.Once);
        _notifications.Verify(n => n.ScheduleHabitReminder(_dailyHabit.Id, "Read", new TimeSpan(9, 0, 0), null), Times.Once);
        _notifications.Verify(n => n.ScheduleHabitReminder(_weeklyHabit.Id, "Gym", new TimeSpan(18, 30, 0),
            It.Is<IReadOnlyList<int>>(d => d.Count == 2 && d[0] == 1 && d[1] == 3)), Times.Once);
        _notifications.Verify(n => n.ScheduleHabitReminder(_tracker.Id, "Smoking", new TimeSpan(21, 0, 0), null), Times.Once);
        _notifications.Verify(n => n.ScheduleHabitReminder(_silentHabit.Id, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public async Task A_disabled_daily_reminder_stays_off_when_access_returns()
    {
        _profiles.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile { IsDailyReminderEnabled = false });
        var coordinator = Create(new FixedEntitlementService(AccessState.ReadOnly));
        await coordinator.StartAsync();

        await coordinator.ApplyAsync(Subscribed);

        _notifications.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task A_writable_start_with_nothing_paused_touches_nothing()
    {
        await Create(new FixedEntitlementService(Subscribed)).StartAsync();

        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Repeating_the_same_state_does_not_cancel_or_schedule_twice()
    {
        var coordinator = Create(new FixedEntitlementService(Subscribed));

        await coordinator.ApplyAsync(AccessState.ReadOnly);
        await coordinator.ApplyAsync(AccessState.ReadOnly);

        _notifications.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
    }

    [Fact]
    public async Task Reminders_paused_in_one_session_come_back_in_the_next()
    {
        // Session one: the subscription lapses, reminders are paused, the app is closed.
        await Create(new FixedEntitlementService(Subscribed)).ApplyAsync(AccessState.ReadOnly);

        // Session two starts already writable — renewed on another device while the app was shut.
        _notifications.Invocations.Clear();
        await Create(new FixedEntitlementService(Subscribed)).StartAsync();

        _notifications.Verify(n => n.ScheduleDailyJournalReminder(new TimeSpan(20, 0, 0)), Times.Once);
    }

    [Fact]
    public async Task The_coordinator_follows_live_changes()
    {
        var entitlements = new FixedEntitlementService(Subscribed);
        await Create(entitlements).StartAsync();

        entitlements.Set(AccessState.ReadOnly);

        await WaitUntil(() => _notifications.Invocations.Count > 0);
        _notifications.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
    }

    [Fact]
    public async Task A_failure_reading_habits_does_not_escape()
    {
        _habits.Setup(h => h.GetLiveHabitDefinitionsAsync()).ThrowsAsync(new InvalidOperationException("db closed"));
        var coordinator = Create(new FixedEntitlementService(Subscribed));

        await coordinator.Invoking(c => c.ApplyAsync(AccessState.ReadOnly)).Should().NotThrowAsync();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        condition().Should().BeTrue();
    }
}

public class AccessAwareNotificationServiceTests
{
    private readonly Mock<INotificationService> _inner = new();
    private readonly FixedEntitlementService _entitlements = new(AccessState.ReadOnly);

    private AccessAwareNotificationService Create() => new(_inner.Object, new AccessGuard(_entitlements));

    [Fact]
    public void Read_only_blocks_new_daily_and_habit_reminders()
    {
        var service = Create();

        service.ScheduleDailyJournalReminder(new TimeSpan(20, 0, 0));
        service.ScheduleHabitReminder(Guid.NewGuid(), "Read", new TimeSpan(9, 0, 0), null);

        _inner.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
        _inner.Verify(n => n.ScheduleHabitReminder(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public void Writable_passes_them_through()
    {
        _entitlements.Set(AccessState.Legacy);
        var service = Create();

        service.ScheduleDailyJournalReminder(new TimeSpan(20, 0, 0));

        _inner.Verify(n => n.ScheduleDailyJournalReminder(new TimeSpan(20, 0, 0)), Times.Once);
    }

    [Fact]
    public async Task Cancelling_tasks_and_permissions_always_pass_through()
    {
        var service = Create();
        var id = Guid.NewGuid();

        service.CancelDailyJournalReminder();
        service.CancelHabitReminder(id);
        service.ScheduleTodoReminder(id, "Call", "", DateTime.Today.AddDays(1));
        await service.RequestPermissionsAsync();

        _inner.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
        _inner.Verify(n => n.CancelHabitReminder(id), Times.Once);
        _inner.Verify(n => n.ScheduleTodoReminder(id, "Call", "", It.IsAny<DateTime>()), Times.Once);
        _inner.Verify(n => n.RequestPermissionsAsync(), Times.Once);
    }
}

public class HabitServiceLiveDefinitionsTests : IDisposable
{
    private readonly DatabaseContext _db = new(useInMemory: true);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Every_undeleted_habit_is_returned_whatever_day_it_is_scheduled_for()
    {
        var service = new HabitService(_db);
        var weekly = new HabitDefinition
        {
            Name = "Gym",
            CreatedAt = DateTime.Today.AddDays(-10),
            Schedule = new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = new List<int> { ((int)DateTime.Today.DayOfWeek + 1) % 7 } }
        };
        var deleted = new HabitDefinition { Name = "Old", CreatedAt = DateTime.Today.AddDays(-10) };
        await service.AddHabitDefinitionAsync(weekly);
        await service.AddHabitDefinitionAsync(deleted);
        await service.DeleteHabitDefinitionAsync(deleted.Id, DateTime.Today);

        var live = await service.GetLiveHabitDefinitionsAsync();

        live.Should().Contain(h => h.Id == weekly.Id, "not scheduled today, but its reminder still exists");
        live.Should().NotContain(h => h.Id == deleted.Id);
    }
}
