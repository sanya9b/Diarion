using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Foundation;

namespace Diarion.Services.Widgets;

/// <inheritdoc />
/// <remarks>
/// The App Group hand-off to the WidgetKit extension (spec 20, R3/R6). The file is written atomically
/// (temp file + rename), protected until first unlock so a widget can draw after a reboot, and kept
/// out of iCloud backups — it is derived data the app rewrites on every launch. Without the App Group
/// entitlement the container is absent and <see cref="IsSupported"/> is false, so a build published
/// without <c>-p:DiarionWidgets=true</c> keeps working exactly as before, with the coordinator idle.
/// </remarks>
public sealed class IosWidgetSnapshotPublisher : IWidgetSnapshotPublisher
{
    public const string AppGroupId = "group.org.diarion.app";

    private readonly Lazy<string?> _root = new(() => NSFileManager.DefaultManager.GetContainerUrl(AppGroupId)?.Path);

    public bool IsSupported => _root.Value is not null;

    private string SnapshotPath
        => Path.Combine(_root.Value ?? throw new InvalidOperationException("The App Group container is not available."), WidgetSnapshotJson.FileName);

    public Task PublishAsync(string json) => Task.Run(() =>
    {
        var path = SnapshotPath;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
        Protect(path);
        ReloadWidgets();
    });

    public Task ClearAsync() => Task.Run(() =>
    {
        var path = SnapshotPath;
        if (File.Exists(path)) File.Delete(path);
        ReloadWidgets();
    });

    private static void Protect(string path)
    {
        var attributes = new NSFileAttributes { ProtectionKey = NSFileProtection.CompleteUntilFirstUserAuthentication };
        NSFileManager.DefaultManager.SetAttributes(attributes, path, out _);
        using var url = NSUrl.FromFilename(path);
        url.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out _);
    }

    /// <summary>WidgetKit is Swift-only; the facade in native/ios-storekit exports this one call (spec 20, R3).</summary>
    private static void ReloadWidgets()
    {
        try
        {
            dsk_reload_widgets();
        }
        catch (EntryPointNotFoundException)
        {
            // A build without the facade (the Windows type-check never links it): the file is written, the redraw waits.
        }
    }

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void dsk_reload_widgets();
}
