using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.ViewModels;

/// <summary>One dot of the onboarding progress strip.</summary>
/// <remarks>
/// A type for a single bool looks like overkill next to a "3 / 5" label, and it is — until the label
/// has to be translated and the dots do not.
/// </remarks>
public partial class OnboardingStepDot : ObservableObject
{
    [ObservableProperty] private bool _isActive;
}
