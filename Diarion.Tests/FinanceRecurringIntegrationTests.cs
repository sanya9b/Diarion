using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The recurring-transaction path end to end: a real LiteDB behind a real FinanceService behind the real
/// ViewModel, with only dialogs and the profile faked. The Moq-based ViewModel tests cannot see the failure
/// that actually matters here — a rule posting the same day twice — because they stub the very call that
/// would do it. Reopening the finance page is the single most common thing a user does, and every mutation
/// command triggers another load, so this runs the loop for real.
/// </summary>
public class FinanceRecurringIntegrationTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly FinanceService _service;

    public FinanceRecurringIntegrationTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _service = new FinanceService(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    private FinanceViewModel NewViewModel()
    {
        var profile = new Mock<IProfileService>();
        profile.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile());
        return new FinanceViewModel(_service, new Mock<IDialogService>().Object, profile.Object);
    }

    private async Task<RecurringTransaction> AddRuleAsync(int startedDaysAgo, bool autoPost)
    {
        var start = DateTime.Today.AddDays(-startedDaysAgo);
        var rule = new RecurringTransaction
        {
            Type = TransactionType.Expense,
            Amount = 8000m,
            Category = "Rent",
            AutoPost = autoPost,
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = start },
            LastPostedThrough = start.AddDays(-1)
        };
        await _service.SaveRecurringTransactionAsync(rule);
        return rule;
    }

    [Fact]
    public async Task ReopeningTheFinancePageRepeatedly_NeverPostsTheSameDayTwice()
    {
        await AddRuleAsync(startedDaysAgo: 3, autoPost: true);
        var viewModel = NewViewModel();

        await viewModel.LoadAsync();
        var afterFirst = (await _service.GetFinanceTransactionsAsync()).Count;

        await viewModel.LoadAsync();
        await viewModel.LoadAsync();
        await viewModel.LoadAsync();

        afterFirst.Should().Be(4); // the start day plus the three since
        (await _service.GetFinanceTransactionsAsync()).Should().HaveCount(afterFirst);
        viewModel.Feed.OfType<TransactionFeedItem>().Should().HaveCount(afterFirst);
    }

    [Fact]
    public async Task PostedRowsCarryTheRuleAndAreMarkedInTheFeed()
    {
        var rule = await AddRuleAsync(startedDaysAgo: 1, autoPost: true);
        var viewModel = NewViewModel();

        await viewModel.LoadAsync();

        var rows = await _service.GetFinanceTransactionsAsync();
        rows.Should().OnlyContain(t => t.RecurringTransactionId == rule.Id);
        rows.Should().OnlyContain(t => t.Amount == 8000m && t.Category == "Rent");
        rows.Should().OnlyContain(t => t.Type == TransactionType.Expense);
        // The default account seeded by M003 is the fallback, so nothing lands account-less.
        rows.Should().OnlyContain(t => t.AccountId != null);
        viewModel.Feed.OfType<TransactionFeedItem>().Should().OnlyContain(i => i.IsFromPlan);
    }

    [Fact]
    public async Task AnAwaitingRule_KeepsOfferingTheSameOccurrencesAcrossLoads_AndWritesNothing()
    {
        await AddRuleAsync(startedDaysAgo: 2, autoPost: false);
        var viewModel = NewViewModel();

        await viewModel.LoadAsync();
        var firstPending = viewModel.Feed.OfType<PlannedFeedItem>().Select(i => i.Date).ToList();

        await viewModel.LoadAsync();

        firstPending.Should().HaveCount(3);
        viewModel.Feed.OfType<PlannedFeedItem>().Select(i => i.Date).Should().Equal(firstPending);
        (await _service.GetFinanceTransactionsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmingOneOccurrence_PostsItOnceAndLeavesTheOthersPending()
    {
        await AddRuleAsync(startedDaysAgo: 2, autoPost: false);
        var viewModel = NewViewModel();
        await viewModel.LoadAsync();

        // Oldest first, which is the order the feed offers them in.
        var oldest = viewModel.Feed.OfType<PlannedFeedItem>().OrderBy(i => i.Date).First();
        await viewModel.ConfirmPlannedCommand.ExecuteAsync(oldest);

        var posted = (await _service.GetFinanceTransactionsAsync()).Should().ContainSingle().Subject;
        posted.Date.Should().Be(oldest.Date);

        // ConfirmPlanned reloads internally; loading again must not post it a second time.
        await viewModel.LoadAsync();
        (await _service.GetFinanceTransactionsAsync()).Should().HaveCount(1);
        viewModel.Feed.OfType<PlannedFeedItem>().Should().HaveCount(2);
    }

    [Fact]
    public async Task SkippingAnOccurrence_DropsItWithoutEverPostingIt()
    {
        await AddRuleAsync(startedDaysAgo: 2, autoPost: false);
        var viewModel = NewViewModel();
        await viewModel.LoadAsync();

        var oldest = viewModel.Feed.OfType<PlannedFeedItem>().OrderBy(i => i.Date).First();
        await viewModel.SkipPlannedCommand.ExecuteAsync(oldest);

        (await _service.GetFinanceTransactionsAsync()).Should().BeEmpty();
        viewModel.Feed.OfType<PlannedFeedItem>().Should().HaveCount(2)
                 .And.NotContain(i => i.Date == oldest.Date);
    }

    [Fact]
    public async Task CreatingARuleThroughTheForm_PostsNothingRetroactively()
    {
        var viewModel = NewViewModel();
        await viewModel.LoadAsync();

        viewModel.ShowRecurringFormCommand.Execute(null);
        viewModel.NewRecurringAmountText = "8000";
        viewModel.NewRecurringCategory = "Rent";
        await viewModel.SaveRecurringRuleCommand.ExecuteAsync(null);

        // "From now on": today's occurrence has not arrived as far as the rule is concerned.
        (await _service.GetFinanceTransactionsAsync()).Should().BeEmpty();
        (await _service.GetRecurringTransactionsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AutoPostedMoney_ReachesBalancesAndBudgetsLikeAnyOtherSpend()
    {
        // The whole point of materializing real rows: nothing downstream needed changing.
        var account = (await _service.GetAccountsAsync()).Single();
        account.InitialBalance = 30000m;
        await _service.SaveAccountAsync(account);
        await _service.SaveBudgetAsync(new Budget { Category = "Rent", MonthlyLimit = 10000m });
        await AddRuleAsync(startedDaysAgo: 0, autoPost: true);

        var viewModel = NewViewModel();
        await viewModel.LoadAsync();

        viewModel.TotalBalance.Should().Be(22000m);   // 30000 opening − 8000 posted
        viewModel.MonthExpense.Should().Be(8000m);
        viewModel.Budgets.Single(b => b.Category == "Rent").AmountText.Should().StartWith("8");
    }

    [Fact]
    public async Task DeletingTheAccountARuleUsed_KeepsTheTotalEqualToTheSumOfAccounts()
    {
        var original = (await _service.GetAccountsAsync()).Single();
        var keeper = new Account { Name = "Keeper", InitialBalance = 1000m };
        await _service.SaveAccountAsync(keeper);

        var rule = await AddRuleAsync(startedDaysAgo: 0, autoPost: true);
        rule.AccountId = original.Id;
        await _service.SaveRecurringTransactionAsync(rule);

        var viewModel = NewViewModel();
        await viewModel.LoadAsync();

        await _service.DeleteAccountAsync(original.Id, keeper.Id);
        await viewModel.LoadAsync();

        // Had the rule kept pointing at the deleted account, its rows would have vanished from every
        // per-account balance while still counting in the total.
        (await _service.GetRecurringTransactionsAsync()).Single().AccountId.Should().Be(keeper.Id);
        viewModel.TotalBalance.Should().Be(1000m - 8000m);

        viewModel.SelectAccountCommand.Execute(viewModel.Accounts.Single(a => a.Name == "Keeper"));
        viewModel.TotalBalance.Should().Be(1000m - 8000m);
    }

    [Fact]
    public async Task WithTheFeatureDisabled_NothingIsPostedEvenThoughARuleIsDue()
    {
        await AddRuleAsync(startedDaysAgo: 3, autoPost: true);

        var profile = new Mock<IProfileService>();
        profile.Setup(p => p.GetUserProfileAsync())
               .ReturnsAsync(new UserProfile { IsPlannedTransactionsEnabled = false });
        var viewModel = new FinanceViewModel(_service, new Mock<IDialogService>().Object, profile.Object);

        await viewModel.LoadAsync();

        (await _service.GetFinanceTransactionsAsync()).Should().BeEmpty();
        // The rule survives untouched, so switching the feature back on resumes rather than restarts.
        (await _service.GetRecurringTransactionsAsync()).Should().ContainSingle();
    }
}
