using System;
using System.IO;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.App;
using Android.Content;
#elif IOS || MACCATALYST
using Foundation;
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
///
/// The two mobile platforms do not agree on what "4 GB" means, and the difference is not a bug to
/// be reconciled here. Android's <c>TotalMem</c> excludes memory the kernel reserved before the
/// system booted, so a nominal 4 GB phone reports 3.6-3.8 GB; iOS <c>PhysicalMemory</c> reports the
/// number on the spec sheet. That ~10% gap is why the tier thresholds sit below the round figures
/// rather than on them — see <see cref="DeviceCapabilities.Tier"/>.
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
#elif IOS || MACCATALYST
        // NSProcessInfo, not the GC. The GC's number on iOS is a heap budget, and once the bar for
        // the generative model came down to within a phone's reach, tiering on it meant a 4 GB
        // iPhone could silently land in Low and be offered nothing at all — not even the encoder,
        // whose own 512 MB bar fails against an under-reported total.
        return (int)(NSProcessInfo.ProcessInfo.PhysicalMemory / (1024 * 1024));
#else
        // Desktop: the GC's view of physical memory is close enough to tier a development machine,
        // where the answer is "plenty" in practice.
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
