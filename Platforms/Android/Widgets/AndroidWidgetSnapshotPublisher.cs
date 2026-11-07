using System.IO;
using System.Text;
using System.Threading.Tasks;
using Android.Content;
using Diarion.Widgets;

namespace Diarion.Services.Widgets;

/// <inheritdoc />
/// <remarks>
/// Writes the snapshot into the app's private files directory — on Android the widget providers run
/// in this very process, so no shared container is needed and nothing is world-readable — then asks
/// every placed widget to redraw. The write goes through a temporary file and a rename, so a provider
/// that wakes mid-write reads the previous snapshot whole rather than half of the new one.
/// </remarks>
public sealed class AndroidWidgetSnapshotPublisher : IWidgetSnapshotPublisher
{
    public bool IsSupported => true;

    public Task PublishAsync(string json) => Task.Run(() =>
    {
        var context = global::Android.App.Application.Context;
        var path = WidgetSnapshotStore.PathFor(context);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
        WidgetSnapshotStore.RequestUpdate(context);
    });

    public Task ClearAsync() => Task.Run(() =>
    {
        var context = global::Android.App.Application.Context;
        var path = WidgetSnapshotStore.PathFor(context);
        if (File.Exists(path)) File.Delete(path);
        WidgetSnapshotStore.RequestUpdate(context);
    });
}
