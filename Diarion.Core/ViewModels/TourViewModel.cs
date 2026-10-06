using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// A screen's guide: a spotlight that moves from one element to the next, saying what each is for.
/// Stage two of onboarding — the first-run screens now ask questions, and the answers to "where is
/// anything" are given here, on the screens themselves.
/// </summary>
/// <remarks>
/// This view model owns the running order and nothing about the screen. It names its target with a
/// <see cref="TourStepId"/>; measuring where that element actually sits, and cutting the hole in the
/// scrim, belongs to the page. That split is why the whole sequence is testable without a UI.
/// <para>
/// One instance per guided page, each started with its own <see cref="TourId"/>. Nothing is shared
/// between them but the service that hands out the steps.
/// </para>
/// </remarks>
public partial class TourViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly ITourService _tourService;

    private IReadOnlyList<TourStep> _steps = Array.Empty<TourStep>();

    /// <summary>Which guide is running. Remembered from <see cref="StartAsync"/> so that finishing
    /// marks the right one seen — by then the caller is long gone.</summary>
    private TourId _tour;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>The step being shown, or <c>null</c> while the tour is not running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Body), nameof(IsLastStep), nameof(HasMoreSteps), nameof(PrimaryButtonText))]
    private TourStep? _currentStep;

    /// <summary>Progress strip. Rebuilt on each start, because the number of steps depends on the profile.</summary>
    public ObservableCollection<OnboardingStepDot> StepDots { get; } = new();

    public string Title => CurrentStep?.Title ?? string.Empty;
    public string Body => CurrentStep?.Body ?? string.Empty;

    public bool IsLastStep => CurrentStep is not null && ReferenceEquals(CurrentStep, LastStep);
    public bool HasMoreSteps => CurrentStep is not null && !IsLastStep;

    /// <summary>
    /// One button carries the whole sequence, renaming itself on the last step. Two buttons swapped
    /// by visibility used to share the same cell, and on Windows the click that hid "next" was then
    /// caught by the "done" that appeared under the pointer — one tap ended the guide a step early.
    /// </summary>
    public string PrimaryButtonText =>
        IsLastStep ? AppResources.TourDoneButton : AppResources.OnboardingNextButton;

    private TourStep? LastStep => _steps.Count == 0 ? null : _steps[_steps.Count - 1];

    public TourViewModel(IProfileService profileService, ITourService tourService)
    {
        _profileService = profileService;
        _tourService = tourService;
    }

    /// <summary>
    /// Starts one screen's guide if this profile has not seen it, and reports whether it did.
    /// </summary>
    /// <param name="tour">Which screen's guide to run.</param>
    /// <param name="force">
    /// Ignore the seen marker — the "show the guides again" entry in settings, and the only way to
    /// watch one twice.
    /// </param>
    public async Task<bool> StartAsync(TourId tour, bool force = false)
    {
        if (IsActive) return false;

        var profile = await _profileService.GetUserProfileAsync();
        if (!force && !_tourService.ShouldOffer(tour, profile)) return false;

        _tour = tour;
        _steps = _tourService.GetSteps(tour, profile);

        if (_steps.Count == 0)
        {
            // Nothing left to point at. Marking it seen anyway stops the check running on every
            // single visit to the screen for a profile that will never qualify.
            await MarkSeenAsync(profile);
            return false;
        }

        StepDots.Clear();
        for (var i = 0; i < _steps.Count; i++)
        {
            StepDots.Add(new OnboardingStepDot { IsActive = i == 0 });
        }

        CurrentStep = _steps[0];
        IsActive = true;
        return true;
    }

    /// <summary>Advances, or finishes if this was the last step.</summary>
    [RelayCommand]
    private Task NextAsync()
    {
        if (!IsActive || CurrentStep is null) return Task.CompletedTask;

        var next = IndexOfCurrent() + 1;
        if (next >= _steps.Count) return FinishAsync();

        CurrentStep = _steps[next];
        for (var i = 0; i < StepDots.Count; i++)
        {
            StepDots[i].IsActive = i == next;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Leaves early. Recorded exactly like finishing: someone who dismissed the guide has answered
    /// the question, and asking again tomorrow would be arguing with them.
    /// </summary>
    [RelayCommand]
    public Task SkipAsync() => FinishAsync();

    [RelayCommand]
    public async Task FinishAsync()
    {
        IsActive = false;
        CurrentStep = null;
        _steps = Array.Empty<TourStep>();
        StepDots.Clear();

        await MarkSeenAsync(await _profileService.GetUserProfileAsync());
    }

    private async Task MarkSeenAsync(UserProfile profile)
    {
        if (!_tourService.ShouldOffer(_tour, profile)) return;

        _tourService.MarkSeen(_tour, profile);
        await _profileService.SaveUserProfileAsync(profile);
    }

    private int IndexOfCurrent()
    {
        for (var i = 0; i < _steps.Count; i++)
        {
            if (ReferenceEquals(_steps[i], CurrentStep)) return i;
        }

        return -1;
    }
}
