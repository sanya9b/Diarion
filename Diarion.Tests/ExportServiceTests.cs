using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new ExportService(_dbContext, new Mock<IFileSystemService>().Object, new Mock<IShareService>().Object);

        _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).Insert(new DiaryEntry
        {
            Date = new DateTime(2026, 1, 10),
            Emotion = Emotion.Happy,
            SleepQuality = 8,
            Gratitude = "Sunny morning"
        });

        _dbContext.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection).Insert(new FinanceTransaction
        {
            Date = new DateTime(2026, 1, 10),
            Type = TransactionType.Expense,
            Category = "Food",
            Amount = 12.50m,
            Note = "Lunch, with a comma"
        });
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Json_ContainsAllCollectionsAndData()
    {
        var json = await _service.BuildAsync(ExportFormat.Json);

        json.Should().Contain("\"entries\"");
        json.Should().Contain("\"finance_transactions\"");
        json.Should().Contain("Sunny morning");
        json.Should().Contain("Food");
    }

    [Fact]
    public async Task Json_OmitsEmbeddings_EvenWhenTheIndexIsPopulated()
    {
        // Embeddings are derived data: rebuildable from the entries, unreadable to a human, and
        // megabytes of base64 in a file whose whole point is that the user can read it.
        _dbContext.GetCollection<EmbeddingChunk>(DatabaseConstants.EmbeddingsCollection).Insert(new EmbeddingChunk
        {
            Id = EmbeddingChunk.BuildId(EmbeddingSourceKind.Diary, "e1", 0),
            SourceKind = EmbeddingSourceKind.Diary,
            SourceId = "e1",
            Text = "Sunny morning",
            ModelId = "minilm-v1",
            Dim = 2,
            Vector = EmbeddingMath.ToBytes(new[] { 1f, 0f }),
        });

        var json = await _service.BuildAsync(ExportFormat.Json);

        json.Should().NotContain(DatabaseConstants.EmbeddingsCollection);
    }

    [Fact]
    public async Task Json_IncludesCycleHistory()
    {
        // It was missing from the whitelist entirely, so a user's portable copy silently lost every
        // logged period — the one export nobody would think to check until they needed it.
        _dbContext.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection)
            .Insert(new CycleLog { Date = new DateTime(2026, 6, 10) });

        var json = await _service.BuildAsync(ExportFormat.Json);

        // Asserting on the collection carrying a row, not on the date text: LiteDB writes dates as UTC,
        // so a local midnight lands in the file as the previous evening.
        json.Should().Contain("\"cycle_logs\":[{");
    }

    [Fact]
    public async Task Json_IncludesRecurringTransactions()
    {
        // Absent from the whitelist, rules would silently drop out of the user's portable copy.
        _dbContext.GetCollection<RecurringTransaction>(DatabaseConstants.RecurringTransactionsCollection)
            .Insert(new RecurringTransaction { Amount = 8000m, Category = "RentRule" });

        var json = await _service.BuildAsync(ExportFormat.Json);

        json.Should().Contain("\"finance_recurring\"");
        json.Should().Contain("RentRule");
    }

    [Fact]
    public async Task Json_IncludesRecurringTasks()
    {
        // Worse here than a missing block: a restored copy would hold tasks whose RecurringTaskId points
        // at a rule that no longer exists, and those rows are pinned out of auto-migration forever.
        _dbContext.GetCollection<RecurringTask>(DatabaseConstants.RecurringTasksCollection)
            .Insert(new RecurringTask { TaskDescription = "StretchRule" });

        var json = await _service.BuildAsync(ExportFormat.Json);

        json.Should().Contain("\"todo_recurring\"");
        json.Should().Contain("StretchRule");
    }

    [Fact]
    public async Task Csv_LeavesRecurringRulesOut()
    {
        // Posted rows are already in the transactions block; a block of rules would be a table of things
        // that have not happened, in a file people sum in a spreadsheet.
        _dbContext.GetCollection<RecurringTransaction>(DatabaseConstants.RecurringTransactionsCollection)
            .Insert(new RecurringTransaction { Amount = 8000m, Category = "RentRule" });

        var csv = await _service.BuildAsync(ExportFormat.Csv);

        csv.Should().NotContain("RentRule");
    }

    [Fact]
    public async Task Csv_HasHeaderAndQuotesFieldsWithCommas()
    {
        var csv = await _service.BuildAsync(ExportFormat.Csv);

        csv.Should().StartWith("Date,Type,Account,Category,Amount,Note");
        csv.Should().Contain("2026-01-10,Expense,,Food,12.50,");
        csv.Should().Contain("\"Lunch, with a comma\""); // comma-bearing field is quoted
    }

    [Fact]
    public async Task Csv_NamesTheOwningAccount_AndAppendsTransferBlock()
    {
        var cash = new Account { Name = "Cash" };
        var card = new Account { Name = "Card" };
        _dbContext.GetCollection<Account>(DatabaseConstants.AccountsCollection).InsertBulk(new[] { cash, card });

        _dbContext.GetCollection<FinanceTransaction>(DatabaseConstants.FinanceCollection).Insert(new FinanceTransaction
        {
            Date = new DateTime(2026, 2, 1),
            Type = TransactionType.Income,
            Category = "Salary",
            Amount = 900m,
            AccountId = card.Id
        });

        _dbContext.GetCollection<Transfer>(DatabaseConstants.TransfersCollection).Insert(new Transfer
        {
            Date = new DateTime(2026, 2, 2),
            FromAccountId = card.Id,
            ToAccountId = cash.Id,
            Amount = 300m,
            Note = "Withdrawal"
        });

        var csv = await _service.BuildAsync(ExportFormat.Csv);

        csv.Should().Contain("2026-02-01,Income,Card,Salary,900,");
        csv.Should().Contain("Date,From,To,Amount,Note");
        csv.Should().Contain("2026-02-02,Card,Cash,300,Withdrawal");
    }

    [Fact]
    public async Task Json_IncludesAccountsAndTransfers()
    {
        var json = await _service.BuildAsync(ExportFormat.Json);

        json.Should().Contain("\"finance_accounts\"");
        json.Should().Contain("\"finance_transfers\"");
    }

    [Fact]
    public async Task Markdown_IncludesHourlyMoodWhenLogged()
    {
        _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection).Insert(new DiaryEntry
        {
            Date = new DateTime(2026, 3, 4),
            Emotion = Emotion.Calm,
            HourlyMood =
            {
                new HourMood { Hour = 9, Mood = Emotion.Anxious },
                new HourMood { Hour = 21, Mood = Emotion.Happy }
            }
        });

        var md = await _service.BuildAsync(ExportFormat.Markdown);

        md.Should().Contain("- Mood by hour: 09:00 Anxious, 21:00 Happy");
    }

    [Fact]
    public async Task Markdown_OmitsHourlyMoodLineWhenNoneLogged()
    {
        var md = await _service.BuildAsync(ExportFormat.Markdown);

        md.Should().NotContain("Mood by hour");
    }

    [Fact]
    public async Task Markdown_IncludesTheCycleLogAsItsOwnSection()
    {
        var logs = _dbContext.GetCollection<CycleLog>(DatabaseConstants.CycleLogsCollection);
        // Two periods 28 days apart, plus a symptom logged on a day that is not one.
        foreach (var i in Enumerable.Range(0, 4))
        {
            logs.Insert(new CycleLog { Date = new DateTime(2026, 2, 1).AddDays(i) });
            logs.Insert(new CycleLog { Date = new DateTime(2026, 3, 1).AddDays(i) });
        }
        logs.Insert(new CycleLog
        {
            Date = new DateTime(2026, 2, 20),
            IsSymptomOnly = true,
            Symptoms = new List<string> { CycleSymptoms.Headache }
        });

        var md = await _service.BuildAsync(ExportFormat.Markdown);

        md.Should().Contain("# Cycle");
        md.Should().Contain("2026-02-01");
        md.Should().Contain("28 days since the previous start");
        md.Should().Contain("2026-02-20");
        md.Should().Contain(CycleSymptoms.Headache);
    }

    [Fact]
    public async Task Markdown_OmitsTheCycleSectionWhenNothingIsLogged()
    {
        var md = await _service.BuildAsync(ExportFormat.Markdown);

        md.Should().NotContain("# Cycle");
    }

    [Fact]
    public async Task Markdown_HasDatedSectionAndFields()
    {
        var md = await _service.BuildAsync(ExportFormat.Markdown);

        md.Should().Contain("# Diary Export");
        md.Should().Contain("## 2026-01-10");
        md.Should().Contain("Mood: Happy");
        md.Should().Contain("Gratitude: Sunny morning");
    }

    [Fact]
    public async Task Markdown_IncludesTheAnsweredPromptAndItsQuestion()
    {
        var prompt = new GuidedPrompt { TextEn = "What went well?", TextUk = "Що вдалося?" };
        _dbContext.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection).Insert(prompt);

        var entries = _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var entry = entries.FindAll().First();
        entry.PromptResourceKey = prompt.Id.ToString();
        entry.PromptAnswer = "Finished the chapter";
        entries.Update(entry);

        var md = await _service.BuildAsync(ExportFormat.Markdown);

        // The answer is the user's own writing; exporting it without the question loses its meaning.
        md.Should().Contain("Finished the chapter");
        md.Should().MatchRegex("Prompt: (What went well\\?|Що вдалося\\?)");
    }

    [Fact]
    public async Task Markdown_OmitsAnUnansweredPrompt()
    {
        var entries = _dbContext.GetCollection<DiaryEntry>(DatabaseConstants.EntriesCollection);
        var entry = entries.FindAll().First();
        entry.PromptResourceKey = Guid.NewGuid().ToString();
        entries.Update(entry);

        var md = await _service.BuildAsync(ExportFormat.Markdown);

        // An unanswered question was picked by the app, not written by the user.
        md.Should().NotContain("Prompt:");
    }

    [Fact]
    public async Task Json_IncludesThePromptCollection()
    {
        _dbContext.GetCollection<GuidedPrompt>(DatabaseConstants.GuidedPromptsCollection)
            .Insert(new GuidedPrompt { TextEn = "my own question" });

        var json = await _service.BuildAsync(ExportFormat.Json);

        json.Should().Contain(DatabaseConstants.GuidedPromptsCollection);
        json.Should().Contain("my own question");
    }
}
