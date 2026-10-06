using Diarion.Models;
using Diarion.Services;
using Moq;

namespace Diarion.Tests;

/// <summary>
/// A profile stub for view models that only need one to resolve the display currency. Shared so that
/// adding a profile dependency somewhere does not scatter the same four lines through the suite.
/// </summary>
internal static class TestProfiles
{
    public static IProfileService Service(UserProfile? profile = null)
    {
        var mock = new Mock<IProfileService>();
        mock.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(profile ?? new UserProfile());
        return mock.Object;
    }
}
