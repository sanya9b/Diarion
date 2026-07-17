using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new ExportService(_dbContext);

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

        csv.Should().StartWith("Date,Type,Category,Amount,Note");
        csv.Should().Contain("2026-01-10,Expense,Food,12.50,");
        csv.Should().Contain("\"Lunch, with a comma\""); // comma-bearing field is quoted
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
