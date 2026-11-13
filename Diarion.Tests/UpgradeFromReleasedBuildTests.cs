using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using LiteDB;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The upgrade path as a user lives it: a database written by a build that shipped, opened by the
/// code that is about to ship. TestFlight never exercises this — a tester's database has walked
/// through every intermediate build — and the App Store user's has not. Each fixture under
/// <c>Fixtures/</c> is one released schema; the current build writes its own with
/// <see cref="WriteCurrentBuildFixture_WhenAsked"/> before a release, so the next one can be tested
/// against it.
/// </summary>
public class UpgradeFromReleasedBuildTests : IDisposable
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly string _dir;

    public UpgradeFromReleasedBuildTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "diarion_upgrade_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    public static IEnumerable<object[]> ReleasedFixtures()
    {
        if (!Directory.Exists(FixturesDirectory)) yield break;
        foreach (var file in Directory.GetFiles(FixturesDirectory, "schema-v*.db").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return new object[] { Path.GetFileName(file) };
        }
    }

    [Fact]
    public void TheFirstStoreBuildIsCovered()
    {
        // App Store 1.0 (build 2, 2026-09-20) wrote schema 7. Everyone who installed before 1.2 has it.
        File.Exists(Path.Combine(FixturesDirectory, "schema-v7-build2.db")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures))]
    public void ReleasedDatabase_MigratesAndEveryCollectionStillDeserialises(string fixture)
    {
        var path = CopyFixture(fixture);

        using var context = new DatabaseContext(path, new DatabaseSeeder());

        context.MigrationFailure.Should().BeNull($"{fixture} must migrate without a rollback");
        using (var raw = OpenRaw(context))
        {
            raw.UserVersion.Should().Be(MigrationRunner.CurrentVersion);
        }

        foreach (var collection in DatabaseSchema.Collections)
        {
            var documents = context.GetCollection<BsonDocument>(collection.Name).FindAll().ToList();
            foreach (var document in documents)
            {
                var act = () => BsonMapper.Global.Deserialize(collection.DocumentType, document);
                act.Should().NotThrow($"{fixture}: a {collection.DocumentType.Name} written by the old build ({document["_id"]}) must still read");
            }
        }

        // The ones the fixture is known to fill. Zero here would mean the collection was lost, not empty.
        context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).Count().Should().BeGreaterThan(0);
        context.GetCollection<TodoItem>(DatabaseConstants.TodosCollection).Count().Should().BeGreaterThan(0);
        context.GetCollection<HabitDefinition>(DatabaseConstants.HabitDefinitionsCollection).Count().Should().BeGreaterThan(0);
        context.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection).Count().Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures))]
    public async Task ReleasedDatabase_HomeScreenReadsEveryBlockOfAWrittenDay(string fixture)
    {
        // What MainViewModel.LoadEntriesAsync does, service by service, on a day the old build wrote.
        // This is the path that came back empty for 1.0 users after the 1.2 update (2026-11-09).
        var path = CopyFixture(fixture);
        using var context = new DatabaseContext(path, new DatabaseSeeder());

        var profiles = new ProfileService(context);
        var todos = new TodoService(context);
        var habits = new HabitService(context);
        var diary = new DiaryService(context, todos, profiles);
        var prompts = new GuidedPromptService(context);
        var sync = new DiaryHabitSyncService(habits);

        var profile = await profiles.GetUserProfileAsync();
        profile.Should().NotBeNull();

        var written = context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection)
            .FindAll().Where(e => !string.IsNullOrEmpty(e.Content)).OrderByDescending(e => e.Date).First();

        var entry = await diary.GetEntryForDateAsync(written.Date);
        entry.Id.Should().Be(written.Id, "the day the old build wrote must be found by date");
        entry.Content.Should().Be(written.Content);
        entry.Triggers.Should().NotBeNullOrEmpty();
        entry.HourlyMood.Should().NotBeEmpty();

        await sync.SyncHabitsForEntryAsync(entry);
        entry.HabitsList.Should().NotBeEmpty();

        var library = await prompts.GetLibraryAsync();
        library.Should().NotBeNull();

        (await todos.GetTodosForDateAsync(written.Date)).Should().NotBeEmpty();
        (await habits.GetLiveHabitDefinitionsAsync()).Should().NotBeEmpty();
        (await diary.GetCurrentStreakAsync()).Should().NotBeNull();
        (await diary.GetContentDatesAsync(written.Date.AddDays(-30), written.Date)).Should().Contain(written.Date);

        var finance = new FinanceService(context);
        (await finance.GetAccountsAsync(includeArchived: true)).Should().NotBeEmpty();
    }

    /// <summary>
    /// Writes <c>Fixtures/schema-v{N}-build{B}.db</c> from the current code when
    /// <c>DIARION_WRITE_FIXTURE</c> holds the build number. Run once per release, commit the file:
    /// it is what the next release will be tested against. Does nothing otherwise.
    /// </summary>
    [Fact]
    public void WriteCurrentBuildFixture_WhenAsked()
    {
        var build = Environment.GetEnvironmentVariable("DIARION_WRITE_FIXTURE");
        if (string.IsNullOrWhiteSpace(build)) return;

        var repoFixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));
        Directory.CreateDirectory(repoFixtures);
        var path = Path.Combine(repoFixtures, $"schema-v{MigrationRunner.CurrentVersion}-build{build.Trim()}.db");
        File.Delete(path);
        File.Delete(DatabaseContext.LogFilePath(path));

        using (var context = new DatabaseContext(path, new DatabaseSeeder()))
        {
            FixtureContent.Write(context);
        }
        File.Delete(DatabaseContext.LogFilePath(path));

        new FileInfo(path).Length.Should().BeGreaterThan(0);
    }

    private string CopyFixture(string fixture)
    {
        var path = Path.Combine(_dir, fixture);
        File.Copy(Path.Combine(FixturesDirectory, fixture), path);
        return path;
    }

    private static LiteDatabase OpenRaw(DatabaseContext context) =>
        new(new ConnectionString { Filename = context.DatabasePath, ReadOnly = true });

    /// <summary>
    /// What goes into a fixture beyond the DEBUG mock diary the seeder writes: a row in every
    /// collection, every emotion, every block with text. Kept in step with the models by compiling
    /// against them — a property that disappears breaks this file, which is the reminder to think
    /// about the rows that still carry it.
    /// </summary>
    private static class FixtureContent
    {
        public static void Write(IDatabaseContext context)
        {
            var today = DateTime.Today;
            var entries = context.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
            var emotions = Enum.GetValues<Emotion>();
            var all = entries.FindAll().OrderBy(e => e.Date).ToList();
            all.Should().NotBeEmpty("the DEBUG seeder fills the diary; fixtures are written from a Debug test run");
            for (var i = 0; i < all.Count; i++)
            {
                var e = all[i];
                e.Emotion = emotions[i % emotions.Length];
                e.HourlyMood = new List<HourMood>
                {
                    new() { Hour = 9, Mood = emotions[(i + 1) % emotions.Length] },
                    new() { Hour = 21, Mood = emotions[(i + 2) % emotions.Length] },
                };
                e.Triggers = "тригер " + i;
                e.Gratitude = "вдячність " + i;
                e.SoulFood = "їжа для душі " + i;
                e.SupportForOthers = "підтримка " + i;
                e.Title = "День " + i;
                e.Content = "Текст запису " + i;
                e.PromptAnswer = "відповідь " + i;
                e.SleepNotes = "сон " + i;
                e.HealthNotes = "здоров'я " + i;
                entries.Update(e);
            }

            context.GetCollection<UserProfile>(DatabaseConstants.ProfileCollection).Insert(new UserProfile
            {
                Name = "Тест",
                CycleLength = 28,
                PeriodLength = 5,
                LastPeriodStartDate = today.AddDays(-10),
                IsDailyReminderEnabled = true,
                CurrencyCode = "UAH",
                QuickMenuOrder = new List<string> { "Finance", "Notes", "Habits" },
            });

            var accounts = context.GetCollection<Account>(DatabaseConstants.AccountsCollection);
            var account = new Account { Name = "Картка", Icon = "card", ColorHex = "#8FA083", InitialBalance = 1000m, CreatedAt = today.AddDays(-60) };
            accounts.Insert(account);

            context.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection).Insert(new FinanceTransaction
            {
                Type = TransactionType.Expense, AccountId = account.Id, Amount = 120.5m, Category = "Їжа", Note = "обід",
                Date = today.AddDays(-2), CreatedAt = today.AddDays(-2)
            });
            context.GetCollection<Budget>(DatabaseConstants.BudgetsCollection).Insert(new Budget { Category = "Їжа", MonthlyLimit = 4000m, CreatedAt = today.AddDays(-40) });
            context.GetCollection<Note>(DatabaseConstants.NotesCollection).Insert(new Note
            {
                Title = "Нотатка", Content = "# Заголовок\nТекст з #тегом", Tags = new List<string> { "тег" },
                CreatedAt = today.AddDays(-7), UpdatedAt = today.AddDays(-6)
            });
            context.GetCollection<WishlistEntry>(DatabaseConstants.WishlistCollection).Insert(new WishlistEntry { WantText = "хочу", WishText = "бажаю", GetText = "отримую", Date = today.AddDays(-15), CreatedAt = today.AddDays(-15) });
            context.GetCollection<ReadingTrackerBook>(DatabaseConstants.ReadingTrackerBooksCollection).Insert(new ReadingTrackerBook { SlotNumber = 1, BookTitle = "Книга", CompletedOn = today.AddDays(-12), CreatedAt = today.AddDays(-12) });
            context.GetCollection<HappyMoment>(DatabaseConstants.HappyMomentsCollection).Insert(new HappyMoment { SlotNumber = 1, Title = "Мить", Date = today.AddDays(-4), CreatedAt = today.AddDays(-4) });
            context.GetCollection<GoodDeed>(DatabaseConstants.GoodDeedsCollection).Insert(new GoodDeed { SlotNumber = 1, Title = "Добра справа", Date = today.AddDays(-3), CreatedAt = today.AddDays(-3) });
            context.GetCollection<HarmfulHabitTracker>(DatabaseConstants.HarmfulHabitTrackersCollection).Insert(new HarmfulHabitTracker
            {
                HarmfulHabitName = "Кава", StartDate = today.AddDays(-30), CreatedAt = today.AddDays(-30),
                MarkedDays = new List<DateTime> { today.AddDays(-2), today.AddDays(-1) }, CostPerUnit = 45m, UnitsPerDay = 2,
            });
            context.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection).Insert(new CycleLog { Date = today.AddDays(-10), Symptoms = new List<string> { "cramps" }, CreatedAt = today.AddDays(-10) });
            context.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection).Insert(new RecurringTask
            {
                TaskDescription = "Щоденна зарядка", Priority = TodoPriority.High, HasTime = true, TargetTime = new TimeSpan(7, 0, 0),
                Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = today.AddDays(-30) }, CreatedAt = today.AddDays(-30)
            });
        }
    }
}
