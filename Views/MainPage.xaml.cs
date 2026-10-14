using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Diagnostics;
using Diarion.Models;
using Diarion.ViewModels;
using Diarion.Resources.Styles;

namespace Diarion.Views;

public partial class MainPage : ContentPage
{
    private static readonly TimeSpan InitialLoadDelay = TimeSpan.FromMilliseconds(450);

    private readonly MainViewModel _viewModel;
    private bool _hasRenderedOnce;

    public MainPage(MainViewModel viewModel)
    {
        using var _ = StartupTrace.Measure("MainPage..ctor");
        InitializeComponent();
        StartupTrace.Mark("MainPage.InitializeComponent complete");
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        TourOverlay.AnchorResolver = AnchorFor;
        TourOverlay.PrepareStepAsync = PrepareTourStepAsync;
        TourOverlay.Tour = _viewModel.Tour;
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsContentReady))
        {
            // The guide points at blocks that are not on screen until the profile has loaded, so it
            // waits for the same flag the content itself waits for.
            await TryStartTourAsync();
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsPlannerMode))
        {
            if (_viewModel.IsPlannerMode)
            {
                if (PlannerContainer.Content == null)
                {
                    using var _ = StartupTrace.Measure("MainPage.LoadPlannerView");
                    PlannerContainer.Content = new PlannerView();
                }

                // Анімація: зникає Diary, з'являється Planner
                await DiaryContainer.FadeToAsync(0, Motion.FastMs, Motion.EaseOut);
                DiaryContainer.IsVisible = false;

                PlannerContainer.Opacity = 0;
                PlannerContainer.IsVisible = true;
                await PlannerContainer.FadeToAsync(1, Motion.FastMs, Motion.EaseOut);
            }
            else
            {
                // Анімація: зникає Planner, з'являється Diary
                await PlannerContainer.FadeToAsync(0, Motion.FastMs, Motion.EaseOut);
                PlannerContainer.IsVisible = false;

                DiaryContainer.Opacity = 0;
                DiaryContainer.IsVisible = true;
                await DiaryContainer.FadeToAsync(1, Motion.FastMs, Motion.EaseOut);
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = _viewModel.FlushAutoSaveAsync();
    }

    protected override async void OnAppearing()
    {
        using var _ = StartupTrace.Measure("MainPage.OnAppearing");
        base.OnAppearing();

#if ANDROID || IOS || MACCATALYST
        if (await Plugin.LocalNotification.LocalNotificationCenter.Current.AreNotificationsEnabled() == false)
        {
            await Plugin.LocalNotification.LocalNotificationCenter.Current.RequestNotificationPermission();
        }
#endif

        StartupTrace.Mark($"MainPage.OnAppearing hasRenderedOnce={_hasRenderedOnce}");

        // Both things the strip depends on are changed elsewhere and land here: the generative model
        // is installed in settings, so this is the moment the chat tile can appear — or disappear, if
        // the model was deleted or AI switched off while we were away — and the tile order is written
        // by the onboarding module picker, which is dismissed onto this screen.
        await _viewModel.QuickMenuSection.RefreshAsync();

        // A paired day may have been written on another screen while we were away.
        InsightPath.Refresh();

        if (!_hasRenderedOnce)
        {
            _hasRenderedOnce = true;
            Dispatcher.DispatchDelayed(InitialLoadDelay, () =>
            {
                StartupTrace.Mark("MainPage delayed LoadEntriesCommand dispatch");
                _viewModel.LoadEntriesCommand.Execute(null);
            });
            return;
        }

        StartupTrace.Mark("MainPage immediate LoadEntriesCommand dispatch");
        _viewModel.LoadEntriesCommand.Execute(null);

        // Returning from settings after "show the guide again", and the ordinary case of the
        // onboarding modal being dismissed onto this screen.
        await TryStartTourAsync();
    }

    // --- Home-screen guide (onboarding stage 2) ---------------------------------------------------
    // TourOverlayView does the drawing; this page answers the only two questions it cannot: which
    // element an id means, and what has to happen before that element can be measured.

    private async Task TryStartTourAsync()
    {
        if (_viewModel.Tour.IsActive) return;
        if (!_viewModel.IsContentReady) return;

        // Onboarding is a modal over this page, and it finishes by loading the profile — which flips
        // IsContentReady while the modal is still up. Starting there would run the whole guide behind
        // it, unseen and marked as seen. OnAppearing fires again when the modal pops.
        if (Navigation.ModalStack.Count > 0) return;

        await _viewModel.Tour.StartAsync(TourId.Home);
    }

    private async Task PrepareTourStepAsync(TourStep step)
    {
        if (step.Id != TourStepId.DiaryBlocks) return;

        // The blocks are the one target that can be scrolled out of view, and the guide runs over
        // whichever tab was left open.
        if (_viewModel.IsPlannerMode)
        {
            _viewModel.SwitchToDiaryModeCommand.Execute(null);
        }

        await DiaryContainer.ScrollToAsync(0, 0, animated: false);
    }

    private VisualElement? AnchorFor(TourStepId id) => id switch
    {
        TourStepId.QuickMenu => QuickMenuAnchor,
        TourStepId.DayStrip => DayStripAnchor,
        TourStepId.Tabs => TabsAnchor,
        // The first block that survived the module picker, not the whole stack: the stack is taller
        // than the screen, and a hole that size highlights nothing.
        TourStepId.DiaryBlocks => BlocksAnchor.Children.OfType<VisualElement>().FirstOrDefault(child => child.IsVisible),
        _ => null
    };
}
