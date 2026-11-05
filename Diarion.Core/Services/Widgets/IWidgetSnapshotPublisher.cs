using System.Threading.Tasks;

namespace Diarion.Services.Widgets;

/// <summary>
/// Hands the snapshot file to the platform's widgets (spec 20, R3): writes it where the widget process
/// can read it and tells the widgets to redraw. The content is decided elsewhere; this only delivers.
/// </summary>
public interface IWidgetSnapshotPublisher
{
    /// <summary>False where the platform has no home-screen widgets (Windows, Mac Catalyst); the coordinator then does nothing at all.</summary>
    bool IsSupported { get; }

    Task PublishAsync(string json);

    /// <summary>Removes the file. Called when the app lock is switched on, before the button-only snapshot replaces it.</summary>
    Task ClearAsync();
}

/// <summary>The publisher for platforms without widgets.</summary>
public sealed class NullWidgetSnapshotPublisher : IWidgetSnapshotPublisher
{
    public bool IsSupported => false;

    public Task PublishAsync(string json) => Task.CompletedTask;

    public Task ClearAsync() => Task.CompletedTask;
}
