using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Microsoft.Maui.Graphics;

namespace Diarion.ViewModels;

/// <summary>
/// One quadrant tile of the circumplex picker. <see cref="Row"/> and <see cref="Column"/> place it on
/// the 2×2 plane the way the model is drawn: energetic above calm, unpleasant left of pleasant.
/// </summary>
/// <remarks>
/// The chosen state is exposed as plain values (<see cref="FillColor"/>, <see cref="DotOpacity"/>) rather
/// than left to XAML triggers: a DataTrigger on a Border that also carries press feedback does not
/// reliably give the colour back when it un-applies, and a binding has nothing to un-apply.
/// </remarks>
public partial class MoodQuadrantViewModel : ObservableObject
{
    public MoodQuadrantViewModel(EmotionQuadrant quadrant, int row, int column)
    {
        Quadrant = quadrant;
        Row = row;
        Column = column;
        Color = Color.FromArgb(quadrant.ToColorHex());
    }

    public EmotionQuadrant Quadrant { get; }

    public int Row { get; }

    public int Column { get; }

    /// <summary>Resolved on read, so a language switch shows the new names on the next bind.</summary>
    public string Name => Quadrant.ToLocalizedName();

    public string Emoji => Quadrant.ToEmoji();

    /// <summary>The quadrant's brand hue: the fill when chosen, the small dot when not.</summary>
    public Color Color { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillColor))]
    [NotifyPropertyChangedFor(nameof(DotOpacity))]
    private bool _isSelected;

    /// <summary>The tile's fill layer: the quadrant's hue while chosen, see-through otherwise.</summary>
    public Color FillColor => IsSelected ? Color : Colors.Transparent;

    /// <summary>The legend dot shows only while the fill does not already say the colour.</summary>
    public double DotOpacity => IsSelected ? 0 : 1;
}

/// <summary>One refinement chip: a precise emotion inside the chosen quadrant.</summary>
public partial class MoodChipViewModel : ObservableObject
{
    public MoodChipViewModel(Emotion emotion)
    {
        Emotion = emotion;
        Color = Color.FromArgb(emotion.ToColorHex());
    }

    public Emotion Emotion { get; }

    public string Name => Emotion.ToLocalizedName();

    /// <summary>Inherited from the quadrant, so every chip in a row fills with the same hue.</summary>
    public Color Color { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillColor))]
    private bool _isSelected;

    /// <summary>The chip's fill layer: the hue while chosen, see-through otherwise.</summary>
    public Color FillColor => IsSelected ? Color : Colors.Transparent;
}
