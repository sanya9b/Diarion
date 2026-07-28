using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
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
    public async Task Markdown_HasDatedSectionAndFields()
    {
        var md = await _service.BuildAsync(ExportFormat.Markdown);

        md.Should().Contain("# Diary Export");
        md.Should().Contain("## 2026-01-10");
        md.Should().Contain("Mood: Happy");
        md.Should().Contain("Gratitude: Sunny morning");
    }
}
