using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Diarion.Models.Ai.Reports;

namespace Diarion.Services.Ai.Reports;

/// <summary>
/// Turns a snapshot into the exact bytes that go over the wire — and into the exact text the preview
/// screen shows, because they have to be the same text or the preview proves nothing.
/// </summary>
/// <remarks>
/// <para>
/// Indented rather than compact. It costs tokens, and the trade is worth it: the one screen where a
/// user decides whether to hand their year to a stranger should not be a wall of unbroken JSON.
/// </para>
/// <para>
/// The relaxed encoder is what keeps Ukrainian readable. The default escapes every non-ASCII
/// character, so a preview of a Ukrainian diary would be page after page of <c>щ</c> — unreadable
/// to the person being asked to approve it, which defeats the point of showing it. Relaxed encoding is
/// only unsafe when the output lands in HTML; this goes into a JSON request body and a
/// <c>Label</c>.
/// </para>
/// </remarks>
public static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Blank fields disappear instead of arriving as "". A day with nothing written then costs
        // one line rather than nine, and reads as the empty day it was.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(PeriodSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <summary>
    /// A rough character count for the size and cost estimate shown before sending. Characters, not
    /// tokens: tokenisation is the provider's business and varies by model, whereas this number is
    /// exact, free to compute, and enough to tell a normal week from a year of daily essays.
    /// </summary>
    public static int MeasureCharacters(PeriodSnapshot snapshot) => ToJson(snapshot).Length;
}
