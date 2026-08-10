using System;
using System.Linq;
using Diarion.Services.Ai;
using Microsoft.Maui.Networking;

namespace Diarion.Services.Ai;

/// <summary>
/// <see cref="INetworkStatusService"/> over MAUI Connectivity.
/// </summary>
/// <remarks>
/// Needs <c>ACCESS_NETWORK_STATE</c> on Android, which the manifest already carries. Reading it can
/// still throw on a device that answers oddly, and a settings screen must not crash over a checkbox
/// — so every failure lands on <see cref="NetworkStatus.Unknown"/>, which blocks nothing.
/// </remarks>
public sealed class MauiNetworkStatusService : INetworkStatusService
{
    public MauiNetworkStatusService()
    {
        try
        {
            // Never unsubscribed: this is a singleton that lives as long as the app does.
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }
        catch (Exception)
        {
            // Without the event the rule still holds at the moment a download starts; it just
            // stops noticing a Wi-Fi drop midway. Better than failing to construct the container.
        }
    }

    public event EventHandler<NetworkStatus>? Changed;

    public NetworkStatus Current
    {
        get
        {
            try
            {
                var connectivity = Connectivity.Current;
                if (connectivity.NetworkAccess is not (NetworkAccess.Internet or NetworkAccess.ConstrainedInternet))
                {
                    return NetworkStatus.Unknown;
                }

                var profiles = connectivity.ConnectionProfiles.ToList();

                // Wi-Fi wins when both are up, which is the normal state of a phone at home: the
                // cellular radio stays registered while the route goes over Wi-Fi.
                if (profiles.Any(p => p is ConnectionProfile.WiFi or ConnectionProfile.Ethernet))
                {
                    return NetworkStatus.Unmetered;
                }

                return profiles.Contains(ConnectionProfile.Cellular)
                    ? NetworkStatus.Metered
                    : NetworkStatus.Unknown;
            }
            catch (Exception)
            {
                return NetworkStatus.Unknown;
            }
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Changed?.Invoke(this, Current);
}
