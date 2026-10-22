using Diarion.Models;
using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;

namespace Diarion.Controls;

/// <summary>
/// One mood face in the chosen <see cref="MoodIconSet"/> (spec 18): the phone's own emoji as text, or a
/// drawn picture (<see cref="MoodArt"/>). While <see cref="IsActive"/> — the chosen face, or the copy of it
/// over an open panel — a drawn picture plays <see cref="Motion.GlyphLoops"/> breaths and rests. Purpose:
/// delight on the choice; the row is touched a few times a day, so it moves when chosen, not forever.
/// Under reduce motion it does not move at all.
/// </summary>
public class MoodGlyph : Grid
{
    private const string LoopAnimation = "MoodGlyphLoop";

    public static readonly BindableProperty QuadrantProperty = BindableProperty.Create(
        nameof(Quadrant), typeof(EmotionQuadrant), typeof(MoodGlyph), EmotionQuadrant.None,
        propertyChanged: (b, _, _) => ((MoodGlyph)b).Refresh(replay: true));

    public static readonly BindableProperty IconSetProperty = BindableProperty.Create(
        nameof(IconSet), typeof(MoodIconSet), typeof(MoodGlyph), MoodIconSet.Emoji,
        propertyChanged: (b, _, _) => ((MoodGlyph)b).Refresh(replay: true));

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(MoodGlyph), false,
        propertyChanged: (b, _, _) => ((MoodGlyph)b).Play());

    /// <summary>The emoji's font size; the drawn sets simply fill the view.</summary>
    public static readonly BindableProperty EmojiSizeProperty = BindableProperty.Create(
        nameof(EmojiSize), typeof(double), typeof(MoodGlyph), 28.0,
        propertyChanged: (b, _, _) => ((MoodGlyph)b).Refresh(replay: false));

    private readonly Label _emoji = new()
    {
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center,
        InputTransparent = true
    };

    private readonly MoodArt _art = new();

    public MoodGlyph()
    {
        InputTransparent = true;
        Children.Add(_emoji);
        Children.Add(_art);
        Refresh(replay: false);

        Loaded += (_, _) => Play();
        Unloaded += (_, _) => Stop();
    }

    public EmotionQuadrant Quadrant
    {
        get => (EmotionQuadrant)GetValue(QuadrantProperty);
        set => SetValue(QuadrantProperty, value);
    }

    public MoodIconSet IconSet
    {
        get => (MoodIconSet)GetValue(IconSetProperty);
        set => SetValue(IconSetProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double EmojiSize
    {
        get => (double)GetValue(EmojiSizeProperty);
        set => SetValue(EmojiSizeProperty, value);
    }

    private void Refresh(bool replay)
    {
        var drawn = IconSet != MoodIconSet.Emoji;

        _emoji.IsVisible = !drawn;
        _emoji.Text = drawn ? string.Empty : Quadrant.ToEmoji();
        _emoji.FontSize = EmojiSize;

        _art.IsVisible = drawn;
        _art.Set = IconSet;
        _art.Quadrant = Quadrant;

        // A new picture under a chosen face (another set, or the panel's copy moving to another face)
        // greets the user the way a fresh choice does.
        if (replay) Play();
    }

    private void Play()
    {
        Stop();
        if (!IsActive || !IsLoaded || IconSet == MoodIconSet.Emoji || Motion.ReduceMotion) return;

        var loops = Motion.GlyphLoops;
        new Animation(v => _art.Time = v, 0, loops)
            .Commit(_art, LoopAnimation, 16, (uint)(Motion.GlyphLoopMs * loops), Easing.Linear, (_, _) => _art.Time = null);
    }

    private void Stop()
    {
        _art.AbortAnimation(LoopAnimation);
        _art.Time = null;
    }
}
