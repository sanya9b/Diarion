using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The reports tab end to end: a real LiteDB behind a real FinanceService behind the real StatisticsService
/// behind the real ViewModel. Covers everything the on-screen cards show except the pixels — which bucket
/// labels appear, when a card hides itself, how figures are formatted, and whether the breakdown adds up.
/// </summary>
public class FinanceStatsViewModelTests : IDisposable
{
    private readonly DatabaseContext _dbContext;
    private readonly FinanceService _finance;
    private readonly FinanceStatsViewModel _viewModel;

    public FinanceStatsViewModelTests()
    {
        _dbContext = new DatabaseContext(useInMemory: true);
        _finance = new FinanceService(_dbContext);
        var statistics = new StatisticsService(
            new Mock<IDiaryService>().Object, new Mock<ITodoService>().Object, _finance);
        _viewModel = new FinanceStatsViewModel(statistics, TestProfiles.Service());
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task AddAsync(TransactionType type, decimal amount, int daysAgo, string category = "Food", Guid? accountId = null)
        => await _finance.SaveFinanceTransactionAsync(new FinanceTransaction
        {
            Type = type,
            Amount = amount,
            Date = DateTime.Today.AddDays(-daysAgo),
            Category = category,
            AccountId = accountId
        });

    [Fact]
    public async Task LoadDataAsync_ShortPeriod_HidesTheTrendCardInsteadOfDrawingAStub()
    {
        await AddAsync(TransactionType.Expense, 100m, 1);

        await _viewModel.LoadDataAsync(7);

        // A week is one bucket. One bar is not a trend.
        _viewModel.HasTrend.Should().BeFalse();
    }

    [Fact]
    public async Task LoadDataAsync_MonthPeriod_ShowsWeeklyBucketsAndSaysSo()
    {
        await AddAsync(TransactionType.Expense, 100m, 1);
        await AddAsync(TransactionType.Income, 500m, 20);

        await _viewModel.LoadDataAsync(30);

        _viewModel.HasTrend.Should().BeTrue();
        _viewModel.TrendBuckets.Should().HaveCount(5);
        _viewModel.TrendTitle.Should().Be(Diarion.Resources.Localization.AppResources.StatsTrendByWeek);
        _viewModel.TrendPeak.Should().Be(500d);
    }

    [Fact]
    public async Task LoadDataAsync_LongPeriod_SwitchesToMonthlyBucketsAndSaysSo()
    {
        await AddAsync(TransactionType.Expense, 100m, 1);

        await _viewModel.LoadDataAsync(365);

        _viewModel.TrendTitle.Should().Be(Diarion.Resources.Localization.AppResources.StatsTrendByMonth);
        // 12 or 13, depending on today: a year ending on the last day of a month starts on the first of
        // one, so both edges land on month boundaries. Exact edges are pinned in ReportPeriodTests.
        _viewModel.TrendBuckets.Count.Should().BeInRange(12, 13);
        _viewModel.TrendBuckets.Should().OnlyContain(b => !string.IsNullOrWhiteSpace(b.Label));
    }

    [Fact]
    public async Task LoadDataAsync_PartialEdgeBucketsAreFlaggedSoTheyCanBeDimmed()
    {
        await AddAsync(TransactionType.Expense, 100m, 1);

        // Weekly, because 30 is not a multiple of 7: the leftover bucket exists on every calendar day of
        // every year. Which *monthly* edges get clipped depends on today, so that lives in ReportPeriodTests.
        await _viewModel.LoadDataAsync(30);

        // An undimmed short bar reads as a collapse in spending, so the flag has to reach the chart item.
        _viewModel.TrendBuckets.First().IsPartial.Should().BeTrue();
        _viewModel.TrendBuckets.Last().IsPartial.Should().BeFalse("weeks anchor to the window end");
    }

    [Fact]
    public async Task LoadDataAsync_WithNoEarlierData_HidesTheComparisonRowsRatherThanShowingZeroes()
    {
        await AddAsync(TransactionType.Expense, 100m, 1);

        await _viewModel.LoadDataAsync(30);

        _viewModel.HasComparison.Should().BeFalse();
        _viewModel.ComparisonMetrics.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadDataAsync_WithABaseline_FillsTheComparisonRows()
    {
        await AddAsync(TransactionType.Expense, 100m, 1);
        await AddAsync(TransactionType.Expense, 200m, 40);   // inside the preceding 30-day window

        await _viewModel.LoadDataAsync(30);

        _viewModel.HasComparison.Should().BeTrue();
        _viewModel.ComparisonMetrics.Should().HaveCount(3);

        var expense = _viewModel.ComparisonMetrics[1];
        expense.DeltaText.Should().Be("-50%");
        expense.IsGood.Should().BeTrue("spending less is the good direction even though the number fell");
    }

    [Fact]
    public async Task LoadDataAsync_MoversCarryABadgeWhenACategoryAppearsOrDisappears()
    {
        await AddAsync(TransactionType.Expense, 400m, 1, "Cafe");     // new this period
        await AddAsync(TransactionType.Expense, 500m, 40, "Taxi");    // gone this period

        await _viewModel.LoadDataAsync(30);

        var taxi = _viewModel.ExpenseMovers.Single(m => m.Category == "Taxi");
        taxi.Badge.Should().Be(Diarion.Resources.Localization.AppResources.StatsMoverGone);
        taxi.IsGood.Should().BeTrue();

        var cafe = _viewModel.ExpenseMovers.Single(m => m.Category == "Cafe");
        cafe.Badge.Should().Be(Diarion.Resources.Localization.AppResources.StatsMoverNew);
        cafe.IsGood.Should().BeFalse();
    }

    [Fact]
    public async Task LoadDataAsync_BreakdownNamesEveryAccountAndAddsUpToTheHeadline()
    {
        var card = new Account { Name = "Card", Icon = "💳", ColorHex = "#C26D53" };
        await _finance.SaveAccountAsync(card);

        await AddAsync(TransactionType.Expense, 120m, 1, accountId: card.Id);
        await AddAsync(TransactionType.Expense, 30m, 2, accountId: null);   // never assigned

        await _viewModel.LoadDataAsync(30);

        _viewModel.HasAccountBreakdown.Should().BeTrue();
        _viewModel.AccountBreakdown.Should().Contain(a => a.Name == "Card" && a.Icon == "💳");
        _viewModel.AccountBreakdown.Should()
            .Contain(a => a.Name == Diarion.Resources.Localization.AppResources.AccountUnassigned,
                "an unassigned row must still be visible, not silently dropped");
        _viewModel.TotalExpense.Should().Be(150m);
    }

    [Fact]
    public async Task LoadDataAsync_ScopedToOneAccount_NarrowsTheFiguresAndDropsTheBreakdown()
    {
        var card = new Account { Name = "Card" };
        var cash = new Account { Name = "Cash" };
        await _finance.SaveAccountAsync(card);
        await _finance.SaveAccountAsync(cash);

        await AddAsync(TransactionType.Expense, 120m, 1, accountId: card.Id);
        await AddAsync(TransactionType.Expense, 480m, 1, accountId: cash.Id);

        await _viewModel.LoadDataAsync(30, card.Id);

        _viewModel.TotalExpense.Should().Be(120m);
        _viewModel.HasAccountBreakdown.Should().BeFalse("one account is not a breakdown");
    }

    [Fact]
    public async Task LoadDataAsync_TransfersShowPerAccountButStayOutOfIncomeAndExpense()
    {
        var card = new Account { Name = "Card" };
        var cash = new Account { Name = "Cash" };
        await _finance.SaveAccountAsync(card);
        await _finance.SaveAccountAsync(cash);
        await AddAsync(TransactionType.Expense, 10m, 1, accountId: card.Id);
        await _finance.SaveTransferAsync(new Transfer
        {
            FromAccountId = card.Id,
            ToAccountId = cash.Id,
            Amount = 500m,
            Date = DateTime.Today.AddHours(15)   // a time of day, as the finance page writes them
        });

        await _viewModel.LoadDataAsync(30);

        _viewModel.TotalExpense.Should().Be(10m, "a transfer is not a spend");
        _viewModel.AccountBreakdown.Single(a => a.Name == "Card").HasTransfers.Should().BeTrue();
        _viewModel.AccountBreakdown.Single(a => a.Name == "Cash").HasTransfers.Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_WithNothingAtAll_LeavesEveryCardHidden()
    {
        await _viewModel.LoadDataAsync(30);

        _viewModel.IsEmpty.Should().BeTrue();
        _viewModel.IsNotEmpty.Should().BeFalse();
        _viewModel.HasTrend.Should().BeFalse();
        _viewModel.HasComparison.Should().BeFalse();
    }

    [Fact]
    public async Task LoadDataAsync_WithAnAccountButNoMovement_DoesNotShowABreakdownOfZeroes()
    {
        // Otherwise a card of zeros sits directly under the "no data for this period" notice.
        await _finance.SaveAccountAsync(new Account { Name = "Card" });

        await _viewModel.LoadDataAsync(30);

        _viewModel.HasAccountBreakdown.Should().BeFalse();
    }
}
