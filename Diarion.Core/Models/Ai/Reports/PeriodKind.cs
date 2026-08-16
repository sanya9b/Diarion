namespace Diarion.Models.Ai.Reports;

/// <summary>
/// The four report cadences, shortest first.
/// </summary>
/// <remarks>
/// The order is load-bearing rather than cosmetic: each level is summarised from the level below it,
/// never from the raw diary text a second time. A year of entries does not fit in a request and
/// would not be worth what it cost if it did, whereas fifty-two weekly reports fit comfortably —
/// which is also the reason a missed week stays missed. What was never summarised in time is not
/// recoverable at the yearly level except by paying for the whole year again.
/// </remarks>
public enum PeriodKind
{
    Week,
    Month,
    Quarter,
    Year
}
