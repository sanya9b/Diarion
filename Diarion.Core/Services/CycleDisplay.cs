namespace Diarion.Services;

/// <summary>
/// What the cycle forecast is allowed to draw. The forecast itself is untouched:
/// <see cref="CycleForecastCalculator"/> still computes every value it ever did, and the cycle
/// screen still names the fertile window in words.
/// </summary>
/// <remarks>
/// Same arrangement as <c>OnDeviceAi.GenerationOffered</c> — nothing is deleted, a flag decides
/// what is offered — and for the same reason: the feature is postponed, not abandoned.
/// </remarks>
public static class CycleDisplay
{
    /// <summary>
    /// The pacifier drawn on fertile-window days in the month calendar. Off for v1.
    /// </summary>
    /// <remarks>
    /// Both the FDA (21 CFR 884.5370) and the EU MDR (Rule 15) read a product's intended purpose
    /// from how it presents itself, and the interface is part of that presentation, not an exception
    /// to it. A pacifier beside a predicted window states "this is about conceiving" in every
    /// language at once, which is a claim about purpose made in a picture — so keeping the words out
    /// of the store listing, as store/app-store-listing.md does, buys nothing while it is drawn.
    ///
    /// What settles it is where the two things live. The honest sentence exists already — "not
    /// medical advice and not a method of contraception", CycleDisclaimer — but it is on the cycle
    /// screen, and this marker is drawn by CalendarView, which MainPage hosts. The strongest signal
    /// of intent was on the screen without the disclaimer, and the disclaimer on the screen without
    /// the signal. Turning the marker off puts the claim and its qualifier back on one screen; the
    /// label "Fertile window (estimate)" stays there, directly above the disclaimer.
    ///
    /// It also costs nothing a user can name. The marker carried no information the label does not,
    /// and the days it marked are still marked in the model — see CalendarDay.IsFertileWindow.
    ///
    /// Flip this to <c>true</c> and the pacifier returns exactly where it was. The thing to fix
    /// first is the pairing: whatever screen shows the marker has to show the disclaimer too.
    /// </remarks>
    public static bool FertileWindowMarkerOffered { get; } = false;
}
