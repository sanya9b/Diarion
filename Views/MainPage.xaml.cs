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

    private VisualElement? _draggedView;
    private int _draggedIndex = -1;
    private HorizontalStackLayout? _menuContainer;
    private readonly Dictionary<VisualElement, double> _targetTranslations = new();

    private void OnMenuItemDragStarting(object? sender, DragStartingEventArgs e)
    {
        VisualElement? element = sender as VisualElement ?? (sender as Element)?.Parent as VisualElement;
        
        if (element != null)
        {
            _draggedView = element;
            _menuContainer = element.Parent as HorizontalStackLayout;
            _targetTranslations.Clear();
            
            if (_menuContainer != null)
            {
                _draggedIndex = _menuContainer.Children.IndexOf(_draggedView);
            }

            // Зробити невидимою саму іконку (її "тінь"), залишаючи місце для перетягування.
            // Затримка у 50мс потрібна, щоб операційна система встигла "сфотографувати" іконку 
            // для створення напівпрозорого привида (drag shadow), який тягнеться за пальцем.
            var visualContent = (_draggedView as ContentView)?.Content;
            if (visualContent != null)
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
                {
                    // Перевіряємо, чи перетягування ще триває
                    if (_draggedView != null)
                    {
                        visualContent.Opacity = 0.01;
                    }
                });
            }
        }
    }

    private void OnMenuItemDropCompleted(object? sender, DropCompletedEventArgs e)
    {
        ResetDragState();
    }

    private void OnMenuItemDragOver(object? sender, DragEventArgs e)
    {
        VisualElement? hoveredView = sender as VisualElement ?? (sender as Element)?.Parent as VisualElement;
        
        if (hoveredView != null && _menuContainer != null && _draggedView != null)
        {
            int hoveredIndex = _menuContainer.Children.IndexOf(hoveredView);
            if (hoveredIndex == -1 || _draggedIndex == -1) return;

            // Distance is Width (50) + MarginRight (8) + Spacing (8) = 66
            double shiftDistance = 66;

            for (int i = 0; i < _menuContainer.Children.Count; i++)
            {
                var child = _menuContainer.Children[i] as VisualElement;
                if (child == null || child == _draggedView) continue;

                double targetTranslationX = 0;

                if (hoveredIndex > _draggedIndex)
                {
                    // Dragged left-to-right
                    if (i > _draggedIndex && i <= hoveredIndex)
                    {
                        targetTranslationX = -shiftDistance;
                    }
                }
                else if (hoveredIndex < _draggedIndex)
                {
                    // Dragged right-to-left
                    if (i >= hoveredIndex && i < _draggedIndex)
                    {
                        targetTranslationX = shiftDistance;
                    }
                }

                var visualContent = (child as ContentView)?.Content ?? child;

                if (!_targetTranslations.TryGetValue(child, out double currentTarget) || currentTarget != targetTranslationX)
                {
                    _targetTranslations[child] = targetTranslationX;
                    _ = visualContent.TranslateToAsync(targetTranslationX, 0, 250, Easing.CubicOut);
                }
            }
        }
    }

    private void OnMenuItemDragLeave(object? sender, DragEventArgs e)
    {
        // Translations reset handled gracefully
    }

    private void OnMenuItemDrop(object? sender, DropEventArgs e)
    {
        if (_menuContainer != null)
        {
            foreach (var child in _menuContainer.Children)
            {
                if (child is ContentView cv && cv.Content != null)
                {
                    cv.Content.CancelAnimations();
                    cv.Content.TranslationX = 0;
                }
            }
        }
        ResetDragState();
    }

    private void ResetDragState()
    {
        if (_draggedView != null)
        {
            var visualContent = (_draggedView as ContentView)?.Content ?? _draggedView;
            visualContent.CancelAnimations();
            visualContent.Opacity = 1.0;
            visualContent.Scale = 1.0;
            visualContent.TranslationX = 0;
        }

        if (_menuContainer != null)
        {
            foreach (var child in _menuContainer.Children)
            {
                if (child is ContentView cv && cv.Content != null)
                {
                    cv.Content.CancelAnimations();
                    cv.Content.TranslationX = 0;
                }
            }
        }

        _draggedView = null;
        _draggedIndex = -1;
        _menuContainer = null;
        _targetTranslations.Clear();
    }
}
