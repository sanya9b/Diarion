using System;

namespace Diarion.Services.Ai;

/// <summary>
/// What kind of connection this device has right now, to the extent the operating system will say.
/// </summary>
/// <remarks>
/// Exists for one question: may a gigabyte of model files be spent on this connection. It reports
/// the <em>transport</em>, not the tariff, and those are not the same thing — a phone tethering
/// another phone looks exactly like home Wi-Fi from here, and an unlimited SIM looks exactly like a
/// metered one. Both errors are tolerable in the direction chosen: only <see cref="NetworkStatus
/// .Metered"/> ever stops a download, so a connection the OS will not classify is allowed through
/// and left to fail on its own if it cannot carry the bytes.
/// </remarks>
public interface INetworkStatusService
{
    NetworkStatus Current { get; }

    /// <summary>
    /// Raised when the connection changes underneath a running download — the case the Wi-Fi-only
    /// setting is really about, since a phone leaving the house switches to mobile data silently
    /// and a partly-downloaded model would happily finish itself on the user's data allowance.
    /// </summary>
    event EventHandler<NetworkStatus>? Changed;
}

public enum NetworkStatus
{
    /// <summary>
    /// Offline, or connected over something the platform declines to classify. Deliberately not a
    /// reason to block anything: a wrong answer here would refuse downloads that would have worked.
    /// </summary>
    Unknown,

    /// <summary>Wi-Fi or ethernet.</summary>
    Unmetered,

    /// <summary>Cellular. The only status the Wi-Fi-only preference acts on.</summary>
    Metered,
}
