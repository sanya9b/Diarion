using System.Text.Json;
using System.Text.Json.Serialization;
using Diarion.Models.Widgets;

namespace Diarion.Services.Widgets;

/// <summary>
/// The one encoding of the snapshot file, shared by the writer in the app and the reader in the
/// Android widget. Source-generated rather than reflection-based so the trimmed Android and iOS
/// builds carry no warning and no surprise: camelCase names and enums as strings are what the Swift
/// side decodes against, so neither may change without a schema bump.
/// </summary>
public static class WidgetSnapshotJson
{
    public const string FileName = "widget-snapshot.json";

    public static string Serialize(WidgetSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, WidgetSnapshotJsonContext.Default.WidgetSnapshot);

    /// <summary>Null for an empty or unreadable file — the widget then draws its placeholder.</summary>
    public static WidgetSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize(json, WidgetSnapshotJsonContext.Default.WidgetSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(WidgetSnapshot))]
internal sealed partial class WidgetSnapshotJsonContext : JsonSerializerContext
{
}
