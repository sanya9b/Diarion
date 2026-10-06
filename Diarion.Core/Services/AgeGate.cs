using System;

namespace Diarion.Services;

/// <summary>
/// The two age thresholds the app enforces on itself, and the one way age is counted.
/// </summary>
/// <remarks>
/// Kept pure — birth date and "today" both come in as arguments — so the boundaries can be tested on
/// fixed dates: the birthday itself, the day before it, and 29 February. An unknown birth date fails
/// every threshold: the sections behind these gates are opt-in extras, and "not shown until you say
/// how old you are" is the only direction that is safe to get wrong.
///
/// Neither number comes from a store rule. App Store and Play rate the whole app by its content, not
/// by who it is shown to, so this changes nothing about the rating; it is the product's own choice
/// (2026-09-20) about which parts of a diary belong to whom.
/// </remarks>
public static class AgeGate
{
    /// <summary>The intimate-life row of the health block.</summary>
    public const int IntimateLifeMinimumAge = 18;

    /// <summary>The cycle calendar, on top of the existing gender rule.</summary>
    public const int CycleMinimumAge = 14;

    /// <summary>Full years lived as of <paramref name="today"/>; null when the birth date is unknown or in the future.</summary>
    public static int? YearsOld(DateTime? birthDate, DateTime today)
    {
        if (birthDate is null) return null;

        var birth = birthDate.Value.Date;
        var day = today.Date;
        if (birth > day) return null;

        var years = day.Year - birth.Year;
        // AddYears lands a 29 February birthday on 28 February in common years, so such a person
        // turns a year older on the 28th — the more generous of the two readings, and the one .NET
        // gives for free.
        if (birth.AddYears(years) > day) years--;

        return years;
    }

    public static bool IsAtLeast(DateTime? birthDate, int years, DateTime today)
        => YearsOld(birthDate, today) is { } age && age >= years;
}
