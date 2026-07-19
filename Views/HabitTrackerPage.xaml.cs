using System;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class HabitTrackerPage : ContentPage
{
    private readonly HabitTrackerViewModel _viewModel;
    private IDispatcherTimer? _liveTimer;

    public HabitTrackerPage(HabitTrackerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();

        // Tick the live clean-time counters once per second while the page is visible.
        _liveTimer ??= Dispatcher.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(1);
        _liveTimer.Tick -= OnLiveTick;
        _liveTimer.Tick += OnLiveTick;
        _liveTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _liveTimer?.Stop();
    }

    private void OnLiveTick(object? sender, EventArgs e) => _viewModel.RefreshLiveStats();

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}