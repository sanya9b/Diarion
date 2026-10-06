using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Services.Store;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class AccessGuardTests
{
    [Fact]
    public void A_read_only_state_refuses_writes()
    {
        var guard = new AccessGuard(new FixedEntitlementService(AccessState.ReadOnly));

        guard.CanWrite.Should().BeFalse();
        guard.Invoking(g => g.EnsureWritable()).Should().ThrowExactly<ReadOnlyAccessException>();
    }

    [Theory]
    [MemberData(nameof(WritableStates))]
    public void Every_writable_state_passes(AccessState state)
    {
        var guard = new AccessGuard(new FixedEntitlementService(state));

        guard.CanWrite.Should().BeTrue();
        guard.Invoking(g => g.EnsureWritable()).Should().NotThrow();
    }

    public static IEnumerable<object[]> WritableStates()
    {
        yield return new object[] { AccessState.Legacy };
        yield return new object[] { AccessState.StoreUnavailable };
        yield return new object[] { new AccessState(true, AccessTier.Base, AccessSource.Trial, DateTime.UtcNow.AddDays(14), true) };
        yield return new object[] { new AccessState(true, AccessTier.Base, AccessSource.GracePeriod, DateTime.UtcNow.AddDays(3), true) };
    }

    [Fact]
    public void The_guard_follows_the_state_as_it_changes()
    {
        var entitlements = new FixedEntitlementService(AccessState.ReadOnly);
        var guard = new AccessGuard(entitlements);

        entitlements.Set(AccessState.Legacy);
        guard.CanWrite.Should().BeTrue();

        entitlements.Set(AccessState.ReadOnly);
        guard.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void The_unrestricted_guard_never_refuses()
    {
        AccessGuard.Unrestricted.CanWrite.Should().BeTrue();
        AccessGuard.Unrestricted.Invoking(g => g.EnsureWritable()).Should().NotThrow();
    }
}

/// <summary>
/// Spec 15, AC2: every data-changing method of every guarded service, called while read-only.
/// Arguments are deliberately null or default — the guard must throw before any of them is
/// touched, so a NullReferenceException here means the guard is not the first statement.
/// </summary>
public class ReadOnlyWriteGuardTests : IDisposable
{
    private static readonly IAccessGuard ReadOnly = new AccessGuard(new FixedEntitlementService(AccessState.ReadOnly));

    private readonly DatabaseContext _db = new(useInMemory: true);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Diary_service_refuses_every_write()
    {
        var service = new DiaryService(_db, new TodoService(_db), new ProfileService(_db), ReadOnly);

        await ShouldRefuse(
            ("SaveEntryAsync", () => service.SaveEntryAsync(null!)),
            ("DeleteEntryAsync", () => service.DeleteEntryAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task Todo_service_refuses_every_write()
    {
        var service = new TodoService(_db, null, ReadOnly);

        await ShouldRefuse(
            ("SaveTodoAsync", () => service.SaveTodoAsync(null!)),
            ("DeleteTodoAsync", () => service.DeleteTodoAsync(Guid.NewGuid())),
            ("DeleteRecurringTaskAsync", () => service.DeleteRecurringTaskAsync(Guid.NewGuid())),
            ("SetRecurrenceAsync", () => service.SetRecurrenceAsync(Guid.NewGuid(), null)),
            ("DeleteTodosByDiaryEntryAsync", () => service.DeleteTodosByDiaryEntryAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task Habit_service_refuses_every_write()
    {
        var service = new HabitService(_db, ReadOnly);

        await ShouldRefuse(
            ("AddHabitDefinitionAsync", () => service.AddHabitDefinitionAsync(null!)),
            ("UpdateHabitDefinitionAsync", () => service.UpdateHabitDefinitionAsync(null!)),
            ("DeleteHabitDefinitionAsync", () => service.DeleteHabitDefinitionAsync(Guid.NewGuid(), DateTime.Today)),
            ("UpdateHabitDefinitionsOrderAsync", () => service.UpdateHabitDefinitionsOrderAsync(null!)),
            ("SaveHarmfulHabitTrackerAsync", () => service.SaveHarmfulHabitTrackerAsync(null!)),
            ("DeleteHarmfulHabitTrackerAsync", () => service.DeleteHarmfulHabitTrackerAsync(Guid.NewGuid())),
            ("AddRelapseAsync", () => service.AddRelapseAsync(Guid.NewGuid(), DateTime.Today, null)),
            ("SetHarmfulHabitDayMarkedAsync", () => service.SetHarmfulHabitDayMarkedAsync(Guid.NewGuid(), DateTime.Today, true)));
    }

    [Fact]
    public async Task Note_service_refuses_every_write()
    {
        var service = new NoteService(_db, ReadOnly);

        await ShouldRefuse(
            ("SaveNoteAsync", () => service.SaveNoteAsync(null!)),
            ("DeleteNoteAsync", () => service.DeleteNoteAsync("any")));
    }

    [Fact]
    public async Task Finance_service_refuses_every_write()
    {
        var service = new FinanceService(_db, ReadOnly);

        await ShouldRefuse(
            ("SaveFinanceTransactionAsync", () => service.SaveFinanceTransactionAsync(null!)),
            ("DeleteFinanceTransactionAsync", () => service.DeleteFinanceTransactionAsync(Guid.NewGuid())),
            ("SaveBudgetAsync", () => service.SaveBudgetAsync(null!)),
            ("DeleteBudgetAsync", () => service.DeleteBudgetAsync(Guid.NewGuid())),
            ("SaveAccountAsync", () => service.SaveAccountAsync(null!)),
            ("DeleteAccountAsync", () => service.DeleteAccountAsync(Guid.NewGuid(), Guid.NewGuid())),
            ("SaveTransferAsync", () => service.SaveTransferAsync(null!)),
            ("DeleteTransferAsync", () => service.DeleteTransferAsync(Guid.NewGuid())),
            ("SaveRecurringTransactionAsync", () => service.SaveRecurringTransactionAsync(null!)),
            ("DeleteRecurringTransactionAsync", () => service.DeleteRecurringTransactionAsync(Guid.NewGuid(), false)),
            ("ConfirmOccurrenceAsync", () => service.ConfirmOccurrenceAsync(Guid.NewGuid(), DateTime.Today)),
            ("SkipOccurrenceAsync", () => service.SkipOccurrenceAsync(Guid.NewGuid(), DateTime.Today)));
    }

    [Fact]
    public async Task Cycle_log_service_refuses_every_write()
    {
        var service = new CycleLogService(_db, ReadOnly);

        await ShouldRefuse(
            ("AddEpisodeAsync", () => service.AddEpisodeAsync(DateTime.Today, 5)),
            ("RemoveEpisodeAsync", () => service.RemoveEpisodeAsync(DateTime.Today)),
            ("SetSymptomsAsync", () => service.SetSymptomsAsync(DateTime.Today, Array.Empty<string>())));
    }

    [Fact]
    public async Task Wishlist_service_refuses_every_write()
    {
        var service = new WishlistService(_db, ReadOnly);

        await ShouldRefuse(
            ("SaveWishlistEntryAsync", () => service.SaveWishlistEntryAsync(null!)),
            ("DeleteWishlistEntryAsync", () => service.DeleteWishlistEntryAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task Guided_prompt_service_refuses_every_write()
    {
        var service = new GuidedPromptService(_db, ReadOnly);

        await ShouldRefuse(
            ("AddAsync", () => service.AddAsync(null!)),
            ("UpdateAsync", () => service.UpdateAsync(null!)),
            ("DeleteAsync", () => service.DeleteAsync(Guid.NewGuid(), DateTime.Today)),
            ("UpdateOrderAsync", () => service.UpdateOrderAsync(null!)));
    }

    [Fact]
    public async Task Auxiliary_service_refuses_every_write()
    {
        var service = new AuxiliaryService(_db, ReadOnly);

        await ShouldRefuse(
            ("SaveReadingTrackerBookAsync", () => service.SaveReadingTrackerBookAsync(null!)),
            ("DeleteReadingTrackerBookAsync", () => service.DeleteReadingTrackerBookAsync(1)),
            ("DeleteHappyMomentAsync", () => service.DeleteHappyMomentAsync(1)),
            ("SaveHappyMomentAsync", () => service.SaveHappyMomentAsync(null!)),
            ("SaveGoodDeedAsync", () => service.SaveGoodDeedAsync(null!)),
            ("DeleteGoodDeedAsync", () => service.DeleteGoodDeedAsync(1)));
    }

    [Fact]
    public async Task Automatic_write_paths_fall_silent_instead_of_throwing()
    {
        // The planner's day load and the Finance page's due postings run by themselves on open;
        // read-only must not turn opening a screen into an error (spec 15, R1.4).
        var todos = new TodoService(_db, null, ReadOnly);
        var finance = new FinanceService(_db, ReadOnly);

        var day = await todos.GetTodosForDateAsync(DateTime.Today);
        var postings = await finance.ApplyDuePostingsAsync(DateTime.Today);

        day.Should().NotBeNull();
        postings.PostedCount.Should().Be(0);
    }

    [Fact]
    public async Task Reads_are_untouched_by_the_guard()
    {
        var notes = new NoteService(_db, ReadOnly);
        var habits = new HabitService(_db, ReadOnly);
        var finance = new FinanceService(_db, ReadOnly);
        var diary = new DiaryService(_db, new TodoService(_db), new ProfileService(_db), ReadOnly);

        (await notes.GetAllNotesAsync()).Should().NotBeNull();
        (await habits.GetHarmfulHabitTrackersAsync()).Should().NotBeNull();
        (await finance.GetAccountsAsync()).Should().NotBeNull();
        (await diary.GetAllEntriesAsync()).Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(BackupService))]
    [InlineData(typeof(ExportService))]
    [InlineData(typeof(ProfileService))]
    [InlineData(typeof(DatabaseContext))]
    public void Backup_export_profile_and_the_database_do_not_know_the_guard_exists(Type type)
    {
        // Spec 15, R0.1: these paths stay open in read-only mode by construction — none of them
        // can even ask whether writing is allowed.
        type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Should().NotContain(typeof(IAccessGuard));
    }

    private static async Task ShouldRefuse(params (string Name, Func<Task> Call)[] writes)
    {
        foreach (var (name, call) in writes)
        {
            await FluentActions.Awaiting(call).Should()
                .ThrowExactlyAsync<ReadOnlyAccessException>($"{name} must check the guard before doing anything else");
        }
    }
}
