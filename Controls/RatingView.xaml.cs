using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;

namespace Diarion.Controls;

public partial class RatingStar : ObservableObject
{
    [ObservableProperty]
    private int _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isFilled;

    public bool IsEmpty => !IsFilled;
}

public partial class RatingView : ContentView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(int), typeof(RatingView), 0, BindingMode.TwoWay, propertyChanged: OnValueChanged);

    public static readonly BindableProperty MaxValueProperty =
        BindableProperty.Create(nameof(MaxValue), typeof(int), typeof(RatingView), 10, propertyChanged: OnMaxValueChanged);

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int MaxValue
    {
        get => (int)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public ObservableCollection<RatingStar> Stars { get; } = new();

    public ICommand SelectRatingCommand { get; }

    public RatingView()
    {
        SelectRatingCommand = new Command<int>(OnRatingSelected);
        InitializeComponent();
        GenerateStars();
    }

    private void OnRatingSelected(int rating)
    {
        Value = rating;
    }

    private static void OnValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is RatingView view)
        {
            view.UpdateStars();
        }
    }

    private static void OnMaxValueChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is RatingView view)
        {
            view.GenerateStars();
        }
    }

    private void GenerateStars()
    {
        Stars.Clear();
        for (int i = 1; i <= MaxValue; i++)
        {
            Stars.Add(new RatingStar { Value = i, IsFilled = i <= Value });
        }
    }

    private void UpdateStars()
    {
        if (Stars.Count != MaxValue)
        {
            GenerateStars();
            return;
        }

        for (int i = 0; i < MaxValue; i++)
        {
            Stars[i].IsFilled = (i + 1) <= Value;
        }
    }
}
