using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Behaviors;

public class DragAndDropReorderBehavior : Behavior<HorizontalStackLayout>
{
    private HorizontalStackLayout? _menuContainer;
    private VisualElement? _draggedView;
    private int _draggedIndex = -1;
    private readonly Dictionary<VisualElement, double> _targetTranslations = new();

    public static readonly BindableProperty ShiftDistanceProperty = BindableProperty.Create(
        nameof(ShiftDistance), typeof(double), typeof(DragAndDropReorderBehavior), 66.0);

    public double ShiftDistance
    {
        get => (double)GetValue(ShiftDistanceProperty);
        set => SetValue(ShiftDistanceProperty, value);
    }

    protected override void OnAttachedTo(HorizontalStackLayout bindable)
    {
        base.OnAttachedTo(bindable);
        _menuContainer = bindable;
        _menuContainer.ChildAdded += OnChildAdded;
        _menuContainer.ChildRemoved += OnChildRemoved;
        foreach (var child in _menuContainer.Children)
        {
            AttachGestures(child as VisualElement);
        }
    }

    protected override void OnDetachingFrom(HorizontalStackLayout bindable)
    {
        base.OnDetachingFrom(bindable);
        if (_menuContainer != null)
        {
            _menuContainer.ChildAdded -= OnChildAdded;
            _menuContainer.ChildRemoved -= OnChildRemoved;
            foreach (var child in _menuContainer.Children)
            {
                DetachGestures(child as VisualElement);
            }
            _menuContainer = null;
        }
    }

    private void OnChildAdded(object? sender, ElementEventArgs e)
    {
        AttachGestures(e.Element as VisualElement);
    }

    private void OnChildRemoved(object? sender, ElementEventArgs e)
    {
        DetachGestures(e.Element as VisualElement);
    }

    private void AttachGestures(VisualElement? element)
    {
        if (element is not View view) return;

        foreach (var gesture in view.GestureRecognizers)
        {
            if (gesture is DragGestureRecognizer dragRecognizer)
            {
                dragRecognizer.DragStarting += OnDragStarting;
                dragRecognizer.DropCompleted += OnDropCompleted;
            }
            else if (gesture is DropGestureRecognizer dropRecognizer)
            {
                dropRecognizer.DragOver += OnDragOver;
                dropRecognizer.Drop += OnDrop;
            }
        }
    }

    private void DetachGestures(VisualElement? element)
    {
        if (element is not View view) return;

        foreach (var gesture in view.GestureRecognizers)
        {
            if (gesture is DragGestureRecognizer dragRecognizer)
            {
                dragRecognizer.DragStarting -= OnDragStarting;
                dragRecognizer.DropCompleted -= OnDropCompleted;
            }
            else if (gesture is DropGestureRecognizer dropRecognizer)
            {
                dropRecognizer.DragOver -= OnDragOver;
                dropRecognizer.Drop -= OnDrop;
            }
        }
    }

    public void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        VisualElement? element = sender as VisualElement ?? (sender as Element)?.Parent as VisualElement;
        
        if (element != null && _menuContainer != null)
        {
            _draggedView = element;
            _targetTranslations.Clear();
            _draggedIndex = _menuContainer.Children.IndexOf(_draggedView);

            var visualContent = (_draggedView as ContentView)?.Content;
            if (visualContent != null)
            {
                _menuContainer.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
                {
                    if (_draggedView != null)
                    {
                        visualContent.Opacity = 0.01;
                    }
                });
            }
        }
    }

    public void OnDropCompleted(object? sender, DropCompletedEventArgs e)
    {
        ResetDragState();
    }

    public void OnDragOver(object? sender, DragEventArgs e)
    {
        VisualElement? hoveredView = sender as VisualElement ?? (sender as Element)?.Parent as VisualElement;
        
        if (hoveredView != null && _menuContainer != null && _draggedView != null)
        {
            int hoveredIndex = _menuContainer.Children.IndexOf(hoveredView);
            if (hoveredIndex == -1 || _draggedIndex == -1) return;

            for (int i = 0; i < _menuContainer.Children.Count; i++)
            {
                var child = _menuContainer.Children[i] as VisualElement;
                if (child == null || child == _draggedView) continue;

                double targetTranslationX = 0;

                if (hoveredIndex > _draggedIndex)
                {
                    if (i > _draggedIndex && i <= hoveredIndex)
                    {
                        targetTranslationX = -ShiftDistance;
                    }
                }
                else if (hoveredIndex < _draggedIndex)
                {
                    if (i >= hoveredIndex && i < _draggedIndex)
                    {
                        targetTranslationX = ShiftDistance;
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

    public void OnDrop(object? sender, DropEventArgs e)
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
        _targetTranslations.Clear();
    }
}