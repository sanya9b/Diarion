using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Diarion.Models;
using Diarion.Services.Database;
using Diarion.Services.Database.Migrations;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

public class MigrationRunnerTests
{
    [Fact]
    public void Run_FreshDatabase_SetsCurrentVersion()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.UserVersion.Should().Be(0);

        MigrationRunner.Run(db);

        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_NormalizesDiaryDatesToMidnight_AndSetsVersion()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var id = Guid.NewGuid();
        entries.Insert(new DiaryEntry { Id = id, Date = new DateTime(2026, 1, 10, 14, 30, 0) });

        MigrationRunner.Run(db);

        var migrated = entries.FindById(id);
        migrated.Date.TimeOfDay.Should().Be(TimeSpan.Zero);
        migrated.Date.Date.Should().Be(new DateTime(2026, 1, 10));
        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_BackfillsNoteTagsAndLinks_FromLegacyContent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var notes = db.GetCollection<Note>(DatabaseConstants.NotesCollection);
        // A note created before tag/link parsing existed: content has them, but the fields are empty.
        var note = new Note { Title = "Legacy", Content = "Idea #spark linking [[Other Note]]" };
        note.Tags = new();
        note.LinkedTitles = new();
        notes.Insert(note);

        MigrationRunner.Run(db);

        var migrated = notes.FindById(note.Id);
        migrated.Tags.Should().Equal("spark");
        migrated.LinkedTitles.Should().Equal("other note");
        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_CreatesDefaultAccount_AndBackfillsTransactionAccountId()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var tx = new FinanceTransaction { Amount = 10m, Date = new DateTime(2026, 1, 10) };
        transactions.Insert(tx);

        MigrationRunner.Run(db);

        var account = db.GetCollection<Account>(DatabaseConstants.AccountsCollection).FindAll().Should().ContainSingle().Subject;
        // The name is stored as a resource key so it follows the UI language rather than whatever
        // culture happened to be active when the database was first migrated.
        account.ResourceKey.Should().Be("DefaultAccountName");
        transactions.FindById(tx.Id).AccountId.Should().Be(account.Id);
        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_WithExistingAccount_ReusesItInsteadOfCreatingAnother()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var accounts = db.GetCollection<Account>(DatabaseConstants.AccountsCollection);
        var existing = new Account { Name = "Cash", CreatedAt = new DateTime(2025, 1, 1) };
        accounts.Insert(existing);

        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var tx = new FinanceTransaction { Amount = 10m, Date = new DateTime(2026, 1, 10) };
        transactions.Insert(tx);

        MigrationRunner.Run(db);

        accounts.FindAll().Should().ContainSingle().Which.Id.Should().Be(existing.Id);
        transactions.FindById(tx.Id).AccountId.Should().Be(existing.Id);
    }

    [Fact]
    public void Run_DoesNotReassignTransactionsThatAlreadyHaveAnAccount()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var accounts = db.GetCollection<Account>(DatabaseConstants.AccountsCollection);
        var first = new Account { Name = "First", CreatedAt = new DateTime(2025, 1, 1) };
        var second = new Account { Name = "Second", CreatedAt = new DateTime(2025, 6, 1) };
        accounts.InsertBulk(new[] { first, second });

        var transactions = db.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection);
        var tx = new FinanceTransaction { Amount = 10m, Date = new DateTime(2026, 1, 10), AccountId = second.Id };
        transactions.Insert(tx);

        MigrationRunner.Run(db);

        transactions.FindById(tx.Id).AccountId.Should().Be(second.Id);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]   // already on the new scale — ambiguous, so left alone
    [InlineData(6, 3)]
    [InlineData(7, 4)]   // midpoint rounds up
    [InlineData(8, 4)]
    [InlineData(9, 5)]   // midpoint rounds up
    [InlineData(10, 5)]
    public void Rescale_HalvesOnlyValuesAboveTheNewMaximum(int stored, int expected)
    {
        M004_NormalizeRatingScales.Rescale(stored).Should().Be(expected);
    }

    [Fact]
    public void Run_NormalizesOutOfTenRatings_AndLeavesValidOnesAlone()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);

        var legacy = new DiaryEntry { Date = new DateTime(2026, 1, 10), SleepQuality = 9, HealthStatus = 7 };
        var current = new DiaryEntry { Date = new DateTime(2026, 1, 11), SleepQuality = 4, HealthStatus = 5 };
        entries.Insert(legacy);
        entries.Insert(current);

        MigrationRunner.Run(db);

        var migrated = entries.FindById(legacy.Id);
        migrated.SleepQuality.Should().Be(5);
        migrated.HealthStatus.Should().Be(4);

        var untouched = entries.FindById(current.Id);
        untouched.SleepQuality.Should().Be(4);
        untouched.HealthStatus.Should().Be(5);
    }

    [Fact]
    public void Run_RatingNormalization_IsIdempotent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var entries = db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var entry = new DiaryEntry { Date = new DateTime(2026, 1, 10), SleepQuality = 10 };
        entries.Insert(entry);

        MigrationRunner.Run(db);
        MigrationRunner.Run(db);

        // A second pass must not halve the already-halved value down to 3.
        entries.FindById(entry.Id).SleepQuality.Should().Be(5);
    }

    [Fact]
    public void Run_IsIdempotent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
          .Insert(new DiaryEntry { Date = new DateTime(2026, 1, 10, 9, 0, 0) });

        MigrationRunner.Run(db);
        MigrationRunner.Run(db); // second run must be a no-op

        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
    }

    [Fact]
    public void Run_DoesNotDowngradeNewerDatabase()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.UserVersion = MigrationRunner.CurrentVersion + 5; // simulate a future app schema

        MigrationRunner.Run(db);

        db.UserVersion.Should().Be(MigrationRunner.CurrentVersion + 5);
    }

    [Fact]
    public void Run_MovesTheLastPeriodDateIntoTheCycleLog()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var start = new DateTime(2026, 6, 10);
        db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection)
          .Insert(new UserProfile { LastPeriodStartDate = start, PeriodLength = 4 });

        MigrationRunner.Run(db);

        var logs = db.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection).FindAll().ToList();
        logs.Select(l => l.Date).Should().BeEquivalentTo(
            new[] { start, start.AddDays(1), start.AddDays(2), start.AddDays(3) });

        // Cleared so the log is the only source of cycle history from here on.
        db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection)
          .FindAll().First().LastPeriodStartDate.Should().BeNull();
    }

    [Fact]
    public void Run_LeavesAnExistingCycleLogAlone()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection)
          .Insert(new UserProfile { LastPeriodStartDate = new DateTime(2026, 6, 10) });
        db.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection)
          .Insert(new CycleLog { Date = new DateTime(2026, 6, 20) });

        MigrationRunner.Run(db);
        MigrationRunner.Run(db);

        // Real logging supersedes the anchor; pouring it back in would invent an episode.
        db.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection).Count().Should().Be(1);
    }

    [Fact]
    public void Run_WithNoAnchorDate_AddsNothing()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection).Insert(new UserProfile());

        MigrationRunner.Run(db);

        db.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection).Count().Should().Be(0);
    }

    // --- M006: splitting the habit schedule into a recurrence rule and a quota ---

    /// <summary>
    /// Writes a habit the way the pre-M006 model did: a Schedule sub-document with a Type discriminator.
    /// Built by hand rather than through the typed model, because the old model no longer exists and its
    /// enum names are exactly what the migration has to translate.
    /// </summary>
    private static void InsertLegacyHabit(LiteDatabase db, BsonValue type, int timesPerWeek = 3, BsonArray? daysOfWeek = null)
    {
        db.GetCollection(DatabaseConstants.HabitDefinitionsCollection).Insert(new BsonDocument
        {
            ["_id"] = Guid.NewGuid(),
            ["Name"] = "Legacy",
            ["ResourceKey"] = "",
            ["Order"] = 0,
            ["CreatedAt"] = new DateTime(2026, 1, 1),
            ["Schedule"] = new BsonDocument
            {
                ["Type"] = type,
                ["DaysOfWeek"] = daysOfWeek ?? new BsonArray(),
                ["TimesPerWeek"] = timesPerWeek
            }
        });
    }

    private static HabitDefinition ReadOnlyHabit(LiteDatabase db)
        => db.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection).FindAll().Single();

    [Fact]
    public void Run_MigratedHabit_DeserializesIntoTheNewModel()
    {
        // The enum is stored by name, and "SpecificDays" is not a RecurrenceKind. Without the migration
        // this read throws, which would be a launch crash for anyone who ever picked a non-daily schedule.
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, "SpecificDays", daysOfWeek: new BsonArray { 1, 3 });

        MigrationRunner.Run(db);

        var act = () => ReadOnlyHabit(db);
        act.Should().NotThrow();
    }

    [Fact]
    public void Run_MigratesSpecificDaysScheduleToAWeeklyRecurrence()
    {
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, "SpecificDays", daysOfWeek: new BsonArray { 1, 3 });

        MigrationRunner.Run(db);

        var habit = ReadOnlyHabit(db);
        habit.Schedule.Kind.Should().Be(RecurrenceKind.Weekly);
        habit.Schedule.DaysOfWeek.Should().Equal(1, 3);
        habit.Target.Should().BeNull();
    }

    [Fact]
    public void Run_MigratesTimesPerWeekScheduleToADailyRecurrencePlusAQuota()
    {
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, "TimesPerWeek", timesPerWeek: 4);

        MigrationRunner.Run(db);

        var habit = ReadOnlyHabit(db);
        // The old IsScheduledOn answered true on every day for a quota, so Daily preserves the behaviour.
        habit.Schedule.Kind.Should().Be(RecurrenceKind.Daily);
        habit.Target.Should().NotBeNull();
        habit.Target!.TimesPerWeek.Should().Be(4);
    }

    [Fact]
    public void Run_LeavesADailyScheduleAsDaily()
    {
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, "Daily");

        MigrationRunner.Run(db);

        var habit = ReadOnlyHabit(db);
        habit.Schedule.Kind.Should().Be(RecurrenceKind.Daily);
        habit.Target.Should().BeNull();
    }

    [Fact]
    public void Run_MigratedHabitKeepsNoAnchor_SoHistoricalDaysStillCount()
    {
        // Seeding the anchor from CreatedAt would look tidy and would silently change the answer for every
        // date before the habit existed — which is exactly what strength and streak walk over.
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, "Daily");

        MigrationRunner.Run(db);

        var habit = ReadOnlyHabit(db);
        habit.Schedule.Anchor.Should().Be(DateTime.MinValue);
        habit.IsScheduledOn(new DateTime(2020, 5, 17)).Should().BeTrue();
    }

    [Fact]
    public void Run_HabitStoredWithAnIntegerEnum_MigratesToo()
    {
        // Not how the default mapper writes it, but guessing wrong would turn a quota habit into an
        // every-Nth-day one with nothing on screen to show for it.
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, 2, timesPerWeek: 5); // 2 == TimesPerWeek

        MigrationRunner.Run(db);

        var habit = ReadOnlyHabit(db);
        habit.Schedule.Kind.Should().Be(RecurrenceKind.Daily);
        habit.Target!.TimesPerWeek.Should().Be(5);
    }

    [Fact]
    public void Run_HabitWithNoStoredSchedule_IsUntouched()
    {
        using var db = new LiteDatabase(new MemoryStream());
        db.GetCollection(DatabaseConstants.HabitDefinitionsCollection).Insert(new BsonDocument
        {
            ["_id"] = Guid.NewGuid(),
            ["Name"] = "Scheduleless",
            ["CreatedAt"] = new DateTime(2026, 1, 1)
        });

        MigrationRunner.Run(db);

        var habit = ReadOnlyHabit(db);
        habit.Schedule.Kind.Should().Be(RecurrenceKind.Daily);
        habit.Target.Should().BeNull();
    }

    [Fact]
    public void Run_HabitScheduleSplit_IsIdempotent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyHabit(db, "TimesPerWeek", timesPerWeek: 4);

        MigrationRunner.Run(db);
        // A second pass has to be a no-op even though the runner would skip it on version alone.
        new M006_SplitHabitScheduleIntoRecurrence().Up(db);

        var habit = ReadOnlyHabit(db);
        habit.Schedule.Kind.Should().Be(RecurrenceKind.Daily);
        habit.Target!.TimesPerWeek.Should().Be(4);

        var raw = db.GetCollection(DatabaseConstants.HabitDefinitionsCollection).FindAll().Single();
        raw["Schedule"].AsDocument.ContainsKey("Type").Should().BeFalse();
    }

    // --- M007: extracting todo repeats into recurring task rules ---

    /// <summary>
    /// Writes a todo the way the pre-M007 model did. Built by hand because the three fields that made a
    /// series are exactly the ones the migration removes, so the typed model can no longer express them.
    /// </summary>
    private static Guid InsertLegacyTodo(
        LiteDatabase db,
        DateTime targetDate,
        string description = "Стретчинг",
        bool isDailyRepeat = true,
        DateTime? repeatEndDate = null,
        string? repeatGroupId = null,
        string priority = "Medium")
    {
        var id = Guid.NewGuid();
        var doc = new BsonDocument
        {
            ["_id"] = id,
            ["TargetDate"] = targetDate,
            ["TaskDescription"] = description,
            ["IsCompleted"] = false,
            ["IsDailyRepeat"] = isDailyRepeat,
            ["HasTime"] = false,
            ["HasReminder"] = false,
            ["Priority"] = priority,
            ["CreatedAt"] = targetDate
        };
        if (repeatEndDate != null) doc["RepeatEndDate"] = repeatEndDate.Value;
        if (repeatGroupId != null) doc["RepeatGroupId"] = repeatGroupId;

        db.GetCollection(DatabaseConstants.TodosCollection).Insert(doc);
        return id;
    }

    private static List<RecurringTask> Rules(LiteDatabase db)
        => db.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection).FindAll().ToList();

    private static List<TodoItem> Todos(LiteDatabase db)
        => db.GetCollection<TodoItem>(DatabaseConstants.TodosCollection).FindAll().ToList();

    /// <summary>
    /// M007 is not in the runner's array yet — it lands there together with the code that reads the rules,
    /// so that no build exists in which the fields have been stripped but the old reader is still live.
    /// These drive it directly.
    /// </summary>
    private static void MigrateTodoRepeats(LiteDatabase db)
        => new M007_ExtractTodoRepeatsIntoRecurringTasks().Up(db);

    [Fact]
    public void Run_MigratesAGroupIdSeriesIntoOneRule()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatGroupId: group);
        InsertLegacyTodo(db, new DateTime(2026, 7, 2), repeatGroupId: group);

        MigrateTodoRepeats(db);

        var rule = Rules(db).Single();
        rule.TaskDescription.Should().Be("Стретчинг");
        rule.Recurrence.Kind.Should().Be(RecurrenceKind.Daily);
        Todos(db).Should().OnlyContain(t => t.RecurringTaskId == rule.Id);
    }

    [Fact]
    public void Run_MigratesADescriptionOnlySeriesIntoOneRule()
    {
        // Rows written straight to the collection never got a group id, and neither did the clones the
        // generator inserted from them. Grouping by description was how the old code coped; the migration
        // has to cope the same way or those series arrive as one rule per row.
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyTodo(db, new DateTime(2026, 7, 1));
        InsertLegacyTodo(db, new DateTime(2026, 7, 2));

        MigrateTodoRepeats(db);

        Rules(db).Should().ContainSingle();
        Todos(db).Select(t => t.RecurringTaskId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Run_TwoLegacySeriesWithTheSameDescriptionStillMerge()
    {
        // Faithful to the old definition rather than to the new one: without a group id these rows *were*
        // one series, and inventing a split here would be guessing at history the database does not hold.
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), "Прибрати");
        InsertLegacyTodo(db, new DateTime(2026, 7, 2), "Прибрати");

        MigrateTodoRepeats(db);

        Rules(db).Should().ContainSingle();
    }

    [Fact]
    public void Run_TurnedOffInstanceKeepsItsProvenance()
    {
        // Turning a series off left RepeatEndDate on every row but IsDailyRepeat=false on the one the user
        // edited. Selecting on the flag alone drops it, and a dropped row loses the pin that keeps
        // auto-migration from walking it into today.
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        var ended = new DateTime(2026, 7, 1);
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatEndDate: ended, repeatGroupId: group);
        InsertLegacyTodo(db, new DateTime(2026, 7, 2), isDailyRepeat: false, repeatEndDate: ended, repeatGroupId: group);

        MigrateTodoRepeats(db);

        Rules(db).Should().ContainSingle();
        Todos(db).Should().OnlyContain(t => t.RecurringTaskId != null);
    }

    [Fact]
    public void Run_ATurnedOffSeriesCarriesItsEndDateOntoTheRule()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatEndDate: new DateTime(2026, 7, 3), repeatGroupId: group);

        MigrateTodoRepeats(db);

        Rules(db).Single().Recurrence.EndDate.Should().Be(new DateTime(2026, 7, 3));
    }

    [Fact]
    public void Run_ASeriesWithOneOpenRowStaysOpenEnded()
    {
        // The old generation filter kept producing while a single row had no end date, so a group holding
        // both must migrate as open. Reading it the other way silently kills a live series.
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatEndDate: new DateTime(2026, 7, 3), repeatGroupId: group);
        InsertLegacyTodo(db, new DateTime(2026, 7, 2), repeatGroupId: group);

        MigrateTodoRepeats(db);

        Rules(db).Single().Recurrence.EndDate.Should().BeNull();
    }

    [Fact]
    public void Run_TheRuleAnchorsAtTheEarliestInstance()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 9), repeatGroupId: group);
        InsertLegacyTodo(db, new DateTime(2026, 7, 4), repeatGroupId: group);

        MigrateTodoRepeats(db);

        Rules(db).Single().Recurrence.Anchor.Should().Be(new DateTime(2026, 7, 4));
    }

    [Fact]
    public void Run_TheTemplateComesFromTheLatestInstance()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 4), "Стара назва", repeatGroupId: group, priority: "Low");
        InsertLegacyTodo(db, new DateTime(2026, 7, 9), "Нова назва", repeatGroupId: group, priority: "High");

        MigrateTodoRepeats(db);

        var rule = Rules(db).Single();
        rule.TaskDescription.Should().Be("Нова назва");
        rule.Priority.Should().Be(TodoPriority.High);
    }

    [Fact]
    public void Run_ANonRepeatingTodoIsLeftAlone()
    {
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), "Разова", isDailyRepeat: false);

        MigrateTodoRepeats(db);

        Rules(db).Should().BeEmpty();
        Todos(db).Single().RecurringTaskId.Should().BeNull();
    }

    [Fact]
    public void Run_TodoRepeatExtraction_IsIdempotent()
    {
        using var db = new LiteDatabase(new MemoryStream());
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatGroupId: "g");

        MigrateTodoRepeats(db);
        MigrateTodoRepeats(db);

        Rules(db).Should().ContainSingle();
        var raw = db.GetCollection(DatabaseConstants.TodosCollection).FindAll().Single();
        raw.ContainsKey("IsDailyRepeat").Should().BeFalse();
        raw.ContainsKey("RepeatGroupId").Should().BeFalse();
    }

    [Fact]
    public void Run_ResumedHalfwayThroughAGroup_DoesNotCreateASecondRule()
    {
        // The runner has no transaction. A crash between stamping the first row and the second leaves the
        // rest unmigrated, and a freshly generated rule id would make them a second series — which means a
        // duplicate task every single day, forever.
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatGroupId: group);
        var second = InsertLegacyTodo(db, new DateTime(2026, 7, 2), repeatGroupId: group);

        MigrateTodoRepeats(db);

        // Put one row back the way it was, as an interrupted run would have left it.
        var raw = db.GetCollection(DatabaseConstants.TodosCollection);
        var doc = raw.FindById(second);
        doc.Remove("RecurringTaskId");
        doc["IsDailyRepeat"] = true;
        doc["RepeatGroupId"] = group;
        raw.Update(doc);

        MigrateTodoRepeats(db);

        Rules(db).Should().ContainSingle();
        Todos(db).Select(t => t.RecurringTaskId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Run_AResumedGroupKeepsTheAnchorFromTheWholeSeries()
    {
        // The second pass can only see what is left, so recomputing the rule from it would move the anchor
        // forward onto whichever row happened to survive.
        using var db = new LiteDatabase(new MemoryStream());
        var group = Guid.NewGuid().ToString();
        InsertLegacyTodo(db, new DateTime(2026, 7, 1), repeatGroupId: group);
        var later = InsertLegacyTodo(db, new DateTime(2026, 7, 20), repeatGroupId: group);

        MigrateTodoRepeats(db);

        var raw = db.GetCollection(DatabaseConstants.TodosCollection);
        var doc = raw.FindById(later);
        doc.Remove("RecurringTaskId");
        doc["IsDailyRepeat"] = true;
        doc["RepeatGroupId"] = group;
        raw.Update(doc);

        MigrateTodoRepeats(db);

        Rules(db).Single().Recurrence.Anchor.Should().Be(new DateTime(2026, 7, 1));
    }
}
