using System;
using System.ComponentModel;
using Diarion.Diagnostics;
using Diarion.ViewModels;

namespace Diarion.Views;

public static class ViewExtensions
{
    public static Task<bool> HeightRequestTo(this VisualElement view, double height, uint length = 250, Easing? easing = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        var animation = new Animation(v => view.HeightRequest = v, view.Height, height, easing);
        animation.Commit(view, "HeightRequestTo", 16, length, finished: (v, c) => tcs.SetResult(c));
        return tcs.Task;
    }
}

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
        if (e.PropertyName == nameof(MainViewModel.IsCalendarExpanded))
        {
            if (_viewModel.IsCalendarExpanded)
            {
                // Розгортаємо весь календар (включаючи шапку)
                CalendarHeader.IsVisible = true;
                CalendarGrid.IsVisible = true;

                int rows = _viewModel.CalendarDays.Count / 7;
                if (rows < 5) rows = 5;
                int targetHeight = (rows * 60) + 70; // Зменшив висоту шапки/відступів ще на 20px

                await Task.WhenAll(
                    CalendarHeader.FadeToAsync(1, 200, Easing.CubicOut),
                    CalendarGrid.FadeToAsync(1, 200, Easing.CubicOut),
                    CalendarSection.HeightRequestTo(targetHeight, 250, Easing.CubicOut)
                );
                CalendarSection.HeightRequest = -1; // Auto size
            }
            else
            {
                // Згортаємо ВЕСЬ блок календаря до 0
                await Task.WhenAll(
                    CalendarHeader.FadeToAsync(0, 150, Easing.CubicIn),
                    CalendarGrid.FadeToAsync(0, 150, Easing.CubicIn),
                    CalendarSection.HeightRequestTo(0, 200, Easing.CubicIn) 
                );
                CalendarHeader.IsVisible = false;
                CalendarGrid.IsVisible = false;
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsPlannerMode))
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
