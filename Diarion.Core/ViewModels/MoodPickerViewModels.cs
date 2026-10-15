using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Microsoft.Maui.Graphics;

namespace Diarion.ViewModels;

/// <summary>
/// One face of the mood row. <see cref="Column"/> places it left to right from unpleasant to pleasant;
/// the face shows an emoji and no words — the quadrant's name is for screen readers and legends only.
/// </summary>
/// <remarks>
/// The chosen state is exposed as a plain value (<see cref="FillColor"/>) rather than left to XAML
/// triggers: a DataTrigger on a Border that also carries press feedback does not reliably give the
/// colour back when it un-applies, and a binding has nothing to un-apply.
/// </remarks>
public partial class MoodQuadrantViewModel : ObservableObject
{
    public MoodQuadrantViewModel(EmotionQuadrant quadrant, int column)
    {
        Quadrant = quadrant;
        Column = column;
        Color = Color.FromArgb(quadrant.ToColorHex());
    }

    public EmotionQuadrant Quadrant { get; }

    public int Column { get; }

    /// <summary>Resolved on read, so a language switch shows the new names on the next bind.</summary>
    public string Name => Quadrant.ToLocalizedName();

    public string Emoji => Quadrant.ToEmoji();

    /// <summary>The quadrant's brand hue: the disc behind the emoji once chosen.</summary>
    public Color Color { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillColor))]
    private bool _isSelected;

    /// <summary>The disc's fill layer: the quadrant's hue while chosen, see-through otherwise.</summary>
    public Color FillColor => IsSelected ? Color : Colors.Transparent;
}

/// <summary>One chip in the refinement bubble: a precise emotion inside the chosen quadrant.</summary>
public partial class MoodChipViewModel : ObservableObject
{
    public MoodChipViewModel(Emotion emotion)
    {
        Emotion = emotion;
        Color = Color.FromArgb(emotion.ToColorHex());
    }

    public Emotion Emotion { get; }

    public string Name => Emotion.ToLocalizedName();

    /// <summary>Inherited from the quadrant, so every chip in a bubble fills with the same hue.</summary>
    public Color Color { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillColor))]
    private bool _isSelected;

    /// <summary>The chip's fill layer: the hue while chosen, see-through otherwise.</summary>
    public Color FillColor => IsSelected ? Color : Colors.Transparent;
}
