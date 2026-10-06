using System.Collections.Generic;
using Diarion.Models;
using Diarion.ViewModels;

namespace Diarion.Services;

/// <summary>The list of things onboarding lets a new user switch off, and the one place it is applied.</summary>
public interface IOnboardingModuleService
{
    /// <summary>The catalogue, localised at call time and freshly selected (everything on).</summary>
    IReadOnlyList<OnboardingModule> GetModules();

    /// <summary>
    /// Writes the choices into the profile: block flags directly, and the chosen tiles to the front of
    /// <see cref="UserProfile.QuickMenuOrder"/>. Does not save — the caller owns the profile.
    /// </summary>
    void Apply(UserProfile profile, IReadOnlyList<OnboardingModule> modules);
}
