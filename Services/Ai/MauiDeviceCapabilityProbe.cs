using System;
using System.IO;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.App;
using Android.Content;
#endif

namespace Diarion.Services.Ai;

/// <summary>
/// Reads what this device can actually be asked to run.
/// </summary>
/// <remarks>
/// Physical memory, not the per-process heap: Android's <c>MemoryClass</c> describes what one app
/// may allocate on the managed heap, while a model is mapped natively, so the heap limit answers a
/// different question. Accelerator availability is deliberately not probed — NNAPI is deprecated as
/// of Android 15, and ONNX Runtime offers no reliable pre-flight check for a GPU delegate anyway;
/// the CPU path is the one that always works.
/// </remarks>
public sealed class MauiDeviceCapabilityProbe : IDeviceCapabilityProbe
{
    private readonly IAiModelPathProvider _paths;

    public MauiDeviceCapabilityProbe(IAiModelPathProvider paths)
    {
        _paths = paths;
    }

    public DeviceCapabilities Probe() => new(
        TotalRamMb: ReadTotalRamMb(),
        AvailableStorageBytes: ReadAvailableStorageBytes(),
        ProcessorCount: Environment.ProcessorCount,
        Is64Bit: Environment.Is64BitProcess);

    private static int ReadTotalRamMb()
    {
#if ANDROID
        if (Platform.AppContext.GetSystemService(Context.ActivityService) is ActivityManager manager)
        {
            var info = new ActivityManager.MemoryInfo();
            manager.GetMemoryInfo(info);
            return (int)(info.TotalMem / (1024 * 1024));
        }

        return 0;
#else
        // Desktop and iOS: GC's view of physical memory is close enough to tier a device, and both
        // are development or secondary targets where the answer is "plenty" in practice.
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return total > 0 ? (int)(total / (1024 * 1024)) : 0;
#endif
    }

    private long ReadAvailableStorageBytes()
    {
        try
        {
            // Ask about the volume models are actually written to, which on Android is private app
            // storage and not necessarily the same volume as the system root.
            var directory = Path.GetDirectoryName(_paths.GetModelDirectory("probe")) ?? FileSystem.AppDataDirectory;
            return new DriveInfo(Path.GetPathRoot(directory) ?? directory).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Reporting zero is the safe failure: the catalogue then recommends nothing rather than
            // starting a download onto a volume we could not measure.
            return 0;
        }
    }
}
