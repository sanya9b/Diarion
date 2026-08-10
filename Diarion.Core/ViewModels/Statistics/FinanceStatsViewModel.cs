using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

public partial class FinanceStatsViewModel : BaseViewModel
{
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private decimal _totalIncome;

    [ObservableProperty]
    private decimal _totalExpense;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetBalanceText))]
    private decimal _netBalance;

    /// <summary>Net balance formatted with an explicit sign (e.g. "+1 149,50"), for the KPI tile.</summary>
    public string NetBalanceText => $"{(NetBalance >= 0 ? "+" : "-")}{Math.Abs(NetBalance):N2}";

    public ObservableCollection<CategoryStatItem> ExpenseByCategory { get; } = new();
    public ObservableCollection<CategoryStatItem> IncomeByCategory { get; } = new();

    // --- Trend ---

    public ObservableCollection<DivergingBarChartItem> TrendBuckets { get; } = new();

    /// <summary>Both halves of the diverging chart scale to this, so they stay comparable.</summary>
    [ObservableProperty]
    private double _trendPeak;

    /// <summary>False for windows too short to hold three buckets; the card hides rather than draw a stub.</summary>
    [ObservableProperty]
    private bool _hasTrend;

    /// <summary>"By month" or "by week" — the bucket unit is chosen from the period, so the title follows.</summary>
    [ObservableProperty]
    private string _trendTitle = string.Empty;

    // --- Comparison ---

    [ObservableProperty]
    private bool _hasComparison;

    [ObservableProperty]
    private string _comparisonRangeText = string.Empty;

    public ObservableCollection<MetricDeltaItem> ComparisonMetrics { get; } = new();
    public ObservableCollection<CategoryMoverItem> ExpenseMovers { get; } = new();

    // --- Per-account breakdown ---

    public ObservableCollection<AccountFlowItem> AccountBreakdown { get; } = new();

    [ObservableProperty]
    private bool _hasAccountBreakdown;

    private readonly IProfileService _profileService;
    private string _currencyCode = MoneyFormatter.FallbackCode;

    public FinanceStatsViewModel(IStatisticsService statisticsService, IProfileService profileService)
    {
        _statisticsService = statisticsService;
        _profileService = profileService;
    }

    private string Money(decimal amount) => MoneyFormatter.Format(amount, _currencyCode);
    private string MoneySigned(decimal amount) => MoneyFormatter.FormatSigned(amount, _currencyCode);

    public async Task LoadDataAsync(StatsRange range, Guid? accountId = null)
    {
        var culture = CultureInfo.CurrentCulture;
        _currencyCode = (await _profileService.GetUserProfileAsync())?.GetEffectiveCurrencyCode()
                        ?? MoneyFormatter.FallbackCode;
        var stats = await _statisticsService.GetFinanceStatisticsAsync(range, accountId);

        IsEmpty = stats.IsEmpty;

        TotalIncome = stats.TotalIncome;
        TotalExpense = stats.TotalExpense;
        NetBalance = stats.TotalIncome - stats.TotalExpense;

        ExpenseByCategory.Clear();
        foreach (var item in stats.ExpenseByCategory) ExpenseByCategory.Add(item);

        IncomeByCategory.Clear();
        foreach (var item in stats.IncomeByCategory) IncomeByCategory.Add(item);

        LoadTrend(stats.Trend, culture);
        LoadComparison(stats.Comparison, culture);
        LoadAccountBreakdown(stats.AccountBreakdown, culture);
    }

    private void LoadTrend(FinanceTrendReport trend, CultureInfo culture)
    {
        TrendBuckets.Clear();
        foreach (var bucket in trend.Buckets)
        {
            TrendBuckets.Add(new DivergingBarChartItem
            {
                Label = bucket.Unit == ReportBucketUnit.Month
                    ? bucket.Start.ToString("MMM", culture)
                    : bucket.Start.ToString("d.MM", culture),
                Income = (double)bucket.Income,
                Expense = (double)bucket.Expense,
                IsPartial = bucket.IsPartial
            });
        }

        TrendPeak = (double)trend.PeakMagnitude;
        HasTrend = trend.IsMeaningful && trend.HasAnyData;
        TrendTitle = trend.Unit == ReportBucketUnit.Month
            ? AppResources.StatsTrendByMonth
            : AppResources.StatsTrendByWeek;
    }

    private void LoadComparison(FinanceComparisonReport comparison, CultureInfo culture)
    {
        ComparisonMetrics.Clear();
        ExpenseMovers.Clear();

        HasComparison = comparison.HasBaseline;
        ComparisonRangeText = $"{comparison.PreviousStart.ToString("d MMM", culture)} – " +
                              $"{comparison.PreviousEnd.ToString("d MMM", culture)}";

        if (!HasComparison) return;

        // Falling expense is good news even though the number is negative, so the direction the view
        // colours by is per-metric, not per-sign.
        ComparisonMetrics.Add(Metric(AppResources.IncomeLabel, comparison.Income, higherIsBetter: true, culture));
        ComparisonMetrics.Add(Metric(AppResources.ExpenseLabel, comparison.Expense, higherIsBetter: false, culture));
        ComparisonMetrics.Add(new MetricDeltaItem
        {
            Label = AppResources.StatsKpiBalance,
            ValueText = Money(comparison.Net.Current),
            // No percentage on net: it crosses zero, and "+150%" on a swing from −100 to +50 is
            // arithmetically fine and cognitively useless.
            DeltaText = MoneySigned(comparison.Net.Change),
            IsGood = comparison.Net.Change >= 0
        });

        foreach (var mover in comparison.ExpenseMovers)
        {
            ExpenseMovers.Add(new CategoryMoverItem
            {
                Category = string.IsNullOrWhiteSpace(mover.Category) ? AppResources.CategoryOther : mover.Category,
                ChangeText = MoneySigned(mover.Change),
                Badge = mover.IsNew ? AppResources.StatsMoverNew
                      : mover.IsGone ? AppResources.StatsMoverGone
                      : string.Empty,
                // An expense going down is the good direction.
                IsGood = !mover.IsIncrease
            });
        }
    }

    private MetricDeltaItem Metric(string label, FinanceMetricDelta delta, bool higherIsBetter, CultureInfo culture)
        => new()
        {
            Label = label,
            ValueText = Money(delta.Current),
            // Null fraction means there was no baseline at all — a "new" badge, not a fabricated percent.
            DeltaText = delta.Fraction is { } fraction
                ? fraction.ToString("+0%;-0%;0%", culture)
                : AppResources.StatsMoverNew,
            IsGood = delta.IsUnchanged || delta.IsIncrease == higherIsBetter
        };

    private void LoadAccountBreakdown(System.Collections.Generic.List<FinanceAccountReportRow> rows, CultureInfo culture)
    {
        AccountBreakdown.Clear();
        foreach (var row in rows)
        {
            AccountBreakdown.Add(new AccountFlowItem
            {
                Name = row.IsUnassigned
                    ? AppResources.AccountUnassigned
                    : AccountLocalization.ResolveName(row.Account),
                Icon = row.Account?.Icon ?? "❓",
                ColorHex = row.Account?.ColorHex ?? "#929FA7",
                IncomeText = Money(row.Income),
                ExpenseText = Money(row.Expense),
                TransferText = MoneySigned(row.TransferNet),
                HasTransfers = row.HasTransfers,
                IsArchived = row.IsArchived
            });
        }

        // Accounts with no activity are kept as rows so the card does not reshuffle between periods, but
        // a card of nothing but zeros sitting under the "no data for this period" notice reads as a
        // contradiction. Nothing happened, so there is nothing to break down.
        HasAccountBreakdown = AccountBreakdown.Count > 0 && !IsEmpty;
    }
}

/// <summary>One row of the comparison card: a figure, how it moved, and whether that is good news.</summary>
public class MetricDeltaItem
{
    public string Label { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public string DeltaText { get; set; } = string.Empty;
    public bool IsGood { get; set; }
}

public class CategoryMoverItem
{
    public string Category { get; set; } = string.Empty;
    public string ChangeText { get; set; } = string.Empty;
    /// <summary>"new" or "gone", empty for an ordinary move.</summary>
    public string Badge { get; set; } = string.Empty;
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public bool IsGood { get; set; }
}

public class AccountFlowItem
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#929FA7";
    public string IncomeText { get; set; } = string.Empty;
    public string ExpenseText { get; set; } = string.Empty;
    public string TransferText { get; set; } = string.Empty;
    public bool HasTransfers { get; set; }
    public bool IsArchived { get; set; }
}
