using Diarion.Models.Ai;

namespace Diarion.Services.Ai;

/// <summary>Reads what this device can actually be asked to do.</summary>
public interface IDeviceCapabilityProbe
{
    DeviceCapabilities Probe();
}

/// <param name="TotalRamMb">Physical memory, not the per-process heap limit.</param>
/// <param name="AvailableStorageBytes">Free space where models would be written.</param>
/// <param name="ProcessorCount">Logical cores.</param>
/// <param name="Is64Bit">False on the few remaining 32-bit builds, which cannot map a large model.</param>
public readonly record struct DeviceCapabilities(
    int TotalRamMb,
    long AvailableStorageBytes,
    int ProcessorCount,
    bool Is64Bit)
{
    /// <summary>
    /// Which models this device should be offered.
    /// </summary>
    /// <remarks>
    /// Thresholds are about headroom while the app is foreground, not about the model file: a 4 GB
    /// phone has well under 4 GB to give, and being killed mid-answer is worse than not offering
    /// the feature. Core count gates the top tier because a 4B model on four small cores produces
    /// tokens slower than a person reads.
    /// </remarks>
    public DeviceTier Tier =>
        !Is64Bit || TotalRamMb < 4096 ? DeviceTier.Low
        : TotalRamMb >= 6144 && ProcessorCount >= 6 ? DeviceTier.High
        : DeviceTier.Mid;
}
