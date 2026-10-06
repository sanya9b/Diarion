using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class FinanceRecurringServiceTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 7, 15);

    private readonly DatabaseContext _dbContext;
    private readonly FinanceService _service;

    public FinanceRecurringServiceTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new FinanceService(_dbContext);

        // The context runs migrations on construction, which seeds a default account.
        _dbContext.GetCollection<Account>(DatabaseConstants.AccountsCollection).DeleteAll();
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<RecurringTransaction> AddDailyRuleAsync(
        DateTime lastPostedThrough,
        bool autoPost = true,
        Guid? accountId = null)
    {
        var rule = new RecurringTransaction
        {
            Type = TransactionType.Expense,
            AccountId = accountId,
            Amount = 100m,
            Category = "Rent",
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily },
            AutoPost = autoPost,
            LastPostedThrough = lastPostedThrough
        };
        await _service.SaveRecurringTransactionAsync(rule);
        return rule;
    }

    private async Task<RecurringTransaction> ReloadAsync(Guid id)
        => (await _service.GetRecurringTransactionsAsync()).Single(r => r.Id == id);

    [Fact]
    public async Task SaveRecurringTransactionAsync_RoundTripsTheRule()
    {
        var rule = await AddDailyRuleAsync(Today);

        var stored = await ReloadAsync(rule.Id);

        stored.Amount.Should().Be(100m);
        stored.Category.Should().Be("Rent");
        stored.Recurrence.Kind.Should().Be(RecurrenceKind.Daily);
        stored.AutoPost.Should().BeTrue();
        stored.LastPostedThrough.Date.Should().Be(Today);
    }

    [Fact]
    public async Task ApplyDuePostingsAsync_InsertsRowsAndAdvancesTheWatermark()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-2));

        var result = await _service.ApplyDuePostingsAsync(Today);

        result.PostedCount.Should().Be(2);
        (await _service.GetFinanceTransactionsAsync()).Should().HaveCount(2);
        (await ReloadAsync(rule.Id)).LastPostedThrough.Date.Should().Be(Today);
    }

    [Fact]
    public async Task ApplyDuePostingsAsync_CalledTwice_InsertsOnce()
    {
        await AddDailyRuleAsync(Today.AddDays(-2));

        await _service.ApplyDuePostingsAsync(Today);
        await _service.ApplyDuePostingsAsync(Today);

        (await _service.GetFinanceTransactionsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyDuePostingsAsync_WithNoRules_DoesNothing()
    {
        var result = await _service.ApplyDuePostingsAsync(Today);

        result.PostedCount.Should().Be(0);
        result.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyDuePostingsAsync_NonAutoPostRule_ReturnsPendingAndWritesNothing()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-2), autoPost: false);

        var result = await _service.ApplyDuePostingsAsync(Today);

        result.PostedCount.Should().Be(0);
        result.Pending.Should().HaveCount(2);
        (await _service.GetFinanceTransactionsAsync()).Should().BeEmpty();
        (await ReloadAsync(rule.Id)).LastPostedThrough.Date.Should().Be(Today.AddDays(-2));
    }

    [Fact]
    public async Task ConfirmOccurrenceAsync_InsertsTheTransactionAndAdvancesTheWatermark()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-2), autoPost: false);

        await _service.ConfirmOccurrenceAsync(rule.Id, Today.AddDays(-1));

        var posted = (await _service.GetFinanceTransactionsAsync()).Should().ContainSingle().Subject;
        posted.Date.Should().Be(Today.AddDays(-1));
        posted.RecurringTransactionId.Should().Be(rule.Id);
        (await ReloadAsync(rule.Id)).LastPostedThrough.Date.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public async Task ConfirmOccurrenceAsync_CalledTwiceForTheSameDay_InsertsOnce()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-2), autoPost: false);

        await _service.ConfirmOccurrenceAsync(rule.Id, Today.AddDays(-1));
        await _service.ConfirmOccurrenceAsync(rule.Id, Today.AddDays(-1));

        (await _service.GetFinanceTransactionsAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task SkipOccurrenceAsync_AdvancesTheWatermarkWithoutInserting()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-2), autoPost: false);

        await _service.SkipOccurrenceAsync(rule.Id, Today.AddDays(-1));

        (await _service.GetFinanceTransactionsAsync()).Should().BeEmpty();
        (await ReloadAsync(rule.Id)).LastPostedThrough.Date.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public async Task ConfirmOccurrenceAsync_ForAnEarlierDayAfterALaterOne_DoesNotRewindTheWatermark()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-3), autoPost: false);

        await _service.ConfirmOccurrenceAsync(rule.Id, Today);
        await _service.ConfirmOccurrenceAsync(rule.Id, Today.AddDays(-2));

        // Both rows exist, but the mark only ever moves forward, so the older days stay dealt with.
        (await _service.GetFinanceTransactionsAsync()).Should().HaveCount(2);
        (await ReloadAsync(rule.Id)).LastPostedThrough.Date.Should().Be(Today);
    }

    [Fact]
    public async Task DeleteRecurringTransactionAsync_KeepingPosted_LeavesTheTransactions()
    {
        var rule = await AddDailyRuleAsync(Today.AddDays(-2));
        await _service.ApplyDuePostingsAsync(Today);

        await _service.DeleteRecurringTransactionAsync(rule.Id, deletePostedTransactions: false);

        (await _service.GetRecurringTransactionsAsync()).Should().BeEmpty();
        var remaining = await _service.GetFinanceTransactionsAsync();
        remaining.Should().HaveCount(2);
        // The link is left dangling on purpose: nulling it would lose provenance and disarm the guard
        // that stops an identical replacement rule re-posting the same days.
        remaining.Should().OnlyContain(t => t.RecurringTransactionId == rule.Id);
    }

    [Fact]
    public async Task DeleteRecurringTransactionAsync_DeletingPosted_RemovesOnlyItsOwnRows()
    {
        var doomed = await AddDailyRuleAsync(Today.AddDays(-2));
        var keeper = await AddDailyRuleAsync(Today.AddDays(-2));
        await _service.ApplyDuePostingsAsync(Today);
        await _service.SaveFinanceTransactionAsync(new FinanceTransaction { Amount = 5m, Date = Today });

        await _service.DeleteRecurringTransactionAsync(doomed.Id, deletePostedTransactions: true);

        var remaining = await _service.GetFinanceTransactionsAsync();
        remaining.Should().NotContain(t => t.RecurringTransactionId == doomed.Id);
        remaining.Count(t => t.RecurringTransactionId == keeper.Id).Should().Be(2);
        remaining.Should().ContainSingle(t => t.RecurringTransactionId == null);
    }

    [Fact]
    public async Task DeleteAccountAsync_ReassignsRecurringRules()
    {
        var doomed = new Account { Name = "Doomed" };
        var keeper = new Account { Name = "Keeper" };
        await _service.SaveAccountAsync(doomed);
        await _service.SaveAccountAsync(keeper);

        var rule = await AddDailyRuleAsync(Today, accountId: doomed.Id);

        await _service.DeleteAccountAsync(doomed.Id, keeper.Id);

        // Left behind, the rule would keep posting into a deleted account: those rows disappear from every
        // per-account balance while still counting in the total.
        (await ReloadAsync(rule.Id)).AccountId.Should().Be(keeper.Id);
    }
}
