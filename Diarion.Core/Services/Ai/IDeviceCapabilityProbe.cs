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
    ///
    /// The Mid line sits at 3.5 GB rather than 4 because the platforms disagree about what a 4 GB
    /// device reports: iOS answers 4096 and Android answers 3.6-3.8 GB for the same hardware. On
    /// the round number an iPhone 11 Pro Max qualified on exact equality and its Android
    /// counterpart did not qualify at all, which is a property of two measurement APIs rather than
    /// of the phones.
    /// </remarks>
    public DeviceTier Tier =>
        !Is64Bit || TotalRamMb < 3584 ? DeviceTier.Low
        : TotalRamMb >= 6144 && ProcessorCount >= 6 ? DeviceTier.High
        : DeviceTier.Mid;
}
