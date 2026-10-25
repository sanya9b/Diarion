using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;

namespace Diarion.ViewModels;

/// <summary>
/// One face of the mood row. <see cref="Column"/> places it left to right from unpleasant to pleasant;
/// the face shows a picture from the chosen <see cref="MoodIconSet"/> and no words — the quadrant's
/// name is for screen readers and legends only.
/// </summary>
/// <remarks>
/// The chosen state is exposed as a plain value (<see cref="FillColorKey"/>) rather than left to XAML
/// triggers: a DataTrigger on a Border that also carries press feedback does not reliably give the
/// colour back when it un-applies, and a binding has nothing to un-apply. It is a theme key rather than
/// a colour so each theme decides what a highlight looks like — the pink theme paints highlights in
/// pinks, the brand themes use the quadrant's accent. Charts keep the fixed brand hues.
/// </remarks>
public partial class MoodQuadrantViewModel : ObservableObject
{
    public MoodQuadrantViewModel(EmotionQuadrant quadrant, int column)
    {
        Quadrant = quadrant;
        Column = column;
        ColorKey = quadrant.ToThemeKey();
    }

    public EmotionQuadrant Quadrant { get; }

    public int Column { get; }

    /// <summary>Resolved on read, so a language switch shows the new names on the next bind.</summary>
    public string Name => Quadrant.ToLocalizedName();

    public string Emoji => Quadrant.ToEmoji();

    /// <summary>Which picture the face is drawn in; set by the day for all five faces at once.</summary>
    [ObservableProperty]
    private MoodIconSet _iconSet;

    /// <summary>The theme key of the quadrant's highlight hue.</summary>
    public string ColorKey { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillColorKey))]
    private bool _isSelected;

    /// <summary>The disc's fill layer: the quadrant's hue while chosen, null (see-through) otherwise.</summary>
    public string? FillColorKey => IsSelected ? ColorKey : null;
}

/// <summary>One chip in the refinement bubble: a precise emotion inside the chosen quadrant.</summary>
public partial class MoodChipViewModel : ObservableObject
{
    public MoodChipViewModel(Emotion emotion)
    {
        Emotion = emotion;
        ColorKey = emotion.ToQuadrant().ToThemeKey();
    }

    public Emotion Emotion { get; }

    public string Name => Emotion.ToLocalizedName();

    /// <summary>Inherited from the quadrant, so every chip in a bubble fills with the same hue.</summary>
    public string ColorKey { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillColorKey))]
    private bool _isSelected;

    /// <summary>The chip's fill layer: the hue while chosen, null (see-through) otherwise.</summary>
    public string? FillColorKey => IsSelected ? ColorKey : null;
}
