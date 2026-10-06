using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;

namespace Diarion.Tests;

/// <summary>
/// A connection the test drives by hand. Assigning <see cref="Current"/> announces the change the
/// way the platform's own event does, which is what a download watching for a Wi-Fi drop reacts to.
/// </summary>
internal sealed class FakeNetworkStatus : INetworkStatusService
{
    private NetworkStatus _current = NetworkStatus.Unmetered;

    public event EventHandler<NetworkStatus>? Changed;

    public NetworkStatus Current
    {
        get => _current;
        set
        {
            _current = value;
            Changed?.Invoke(this, value);
        }
    }
}

/// <summary>
/// A profile held in memory. Only <see cref="UserProfile.IsWifiOnlyModelDownload"/> is ever read by
/// the code under test; the rest is along for the ride.
/// </summary>
internal sealed class FakeProfileService : IProfileService
{
    public UserProfile Profile { get; } = new();

    public Task<UserProfile> GetUserProfileAsync() => Task.FromResult(Profile);

    public Task SaveUserProfileAsync(UserProfile profile) => Task.CompletedTask;

    public Task ClearAllDataAsync() => Task.CompletedTask;
}
