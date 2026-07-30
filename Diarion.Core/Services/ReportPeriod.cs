using System;
using System.Collections.Generic;

namespace Diarion.Services;

public enum ReportBucketUnit
{
    /// <summary>Rolling seven-day spans, not ISO weeks.</summary>
    Week,
    /// <summary>Calendar months.</summary>
    Month
}

/// <summary>One bucket of a report window. Both ends inclusive.</summary>
public readonly record struct ReportBucketRange(DateTime Start, DateTime End, bool IsPartial);

/// <summary>
/// Splits a reporting window into buckets and finds the window before it. Pure date arithmetic with no
/// finance types and no culture: every date here is a calendar day, never an instant.
/// </summary>
public static class ReportPeriod
{
    /// <summary>At or below a month the window is bucketed by week; above it, by calendar month.</summary>
    public const int MaxDaysForWeeklyBuckets = 31;

    public static ReportBucketUnit ChooseUnit(int days)
        => days <= MaxDaysForWeeklyBuckets ? ReportBucketUnit.Week : ReportBucketUnit.Month;

    /// <summary>
    /// The buckets covering [<paramref name="start"/>, <paramref name="end"/>], oldest first. Generated
    /// from the bounds rather than from any data, so a stretch with no rows still produces its buckets
    /// instead of vanishing from the chart.
    ///
    /// Weeks are anchored to the <b>end</b> of the window and walked backwards, so the short leftover
    /// bucket lands on the left where it is cosmetic. Anchored the other way it would land on the right,
    /// next to today, where a two-day stub bar reads as a collapse in spending. Calendar months cannot be
    /// re-anchored, so for them both edges may be partial.
    /// </summary>
    public static List<ReportBucketRange> Buckets(DateTime start, DateTime end, ReportBucketUnit unit)
    {
        var from = start.Date;
        var to = end.Date;
        var buckets = new List<ReportBucketRange>();
        if (to < from) return buckets;

        if (unit == ReportBucketUnit.Month)
        {
            for (var cursor = new DateTime(from.Year, from.Month, 1); cursor <= to; cursor = cursor.AddMonths(1))
            {
                var monthEnd = cursor.AddMonths(1).AddDays(-1);
                var bucketStart = cursor < from ? from : cursor;
                var bucketEnd = monthEnd > to ? to : monthEnd;
                buckets.Add(new ReportBucketRange(
                    bucketStart, bucketEnd, bucketStart != cursor || bucketEnd != monthEnd));
            }

            return buckets;
        }

        for (var cursor = to; cursor >= from; cursor = cursor.AddDays(-7))
        {
            var full = cursor.AddDays(-6);
            var bucketStart = full < from ? from : full;
            buckets.Add(new ReportBucketRange(bucketStart, cursor, bucketStart != full));
        }

        buckets.Reverse();
        return buckets;
    }

    /// <summary>
    /// The window of the same length immediately before this one — contiguous and non-overlapping.
    /// Note this is calendar-day arithmetic: the baseline for a 30-day window is the 30 days before it,
    /// not "last month". That is the only reading consistent with period chips whose value is a day count,
    /// but it is why the comparison card should show its two date ranges rather than the word "month".
    /// </summary>
    public static (DateTime Start, DateTime End) PreviousWindow(DateTime start, DateTime end)
    {
        var length = (end.Date - start.Date).Days + 1;
        return (start.Date.AddDays(-length), start.Date.AddDays(-1));
    }
}
