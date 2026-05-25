using System;
using System.ComponentModel;
using Diarion.Diagnostics;
using Diarion.ViewModels;

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
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
                await DiaryContainer.FadeToAsync(0, 150, Easing.CubicOut);
                DiaryContainer.IsVisible = false;

                PlannerContainer.Opacity = 0;
                PlannerContainer.IsVisible = true;
                await PlannerContainer.FadeToAsync(1, 150, Easing.CubicIn);
            }
            else
            {
                // Анімація: зникає Planner, з'являється Diary
                await PlannerContainer.FadeToAsync(0, 150, Easing.CubicOut);
                PlannerContainer.IsVisible = false;

                DiaryContainer.Opacity = 0;
                DiaryContainer.IsVisible = true;
                await DiaryContainer.FadeToAsync(1, 150, Easing.CubicIn);
            }
        }
    }

    private async void OnFabTapped(object? sender, TappedEventArgs e)
    {
        if (sender is View view)
        {
            await view.ScaleToAsync(0.9, 100, Easing.CubicOut);
            await view.ScaleToAsync(1.0, 100, Easing.CubicIn);
        }
    }

    protected override void OnAppearing()
    {
        using var _ = StartupTrace.Measure("MainPage.OnAppearing");
        base.OnAppearing();
        StartupTrace.Mark($"MainPage.OnAppearing hasRenderedOnce={_hasRenderedOnce}");

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
    }
}
