using System;
using System.Threading.Tasks;
using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// The app mark drawn live: the letter in the ink colour of the current theme, the bookmark in Coral,
/// no background — so it sits on any page in either theme and the icon and the app share one shape.
/// The onboarding entrance is the one place the mark moves: the letter fades in and the bookmark drops
/// into it. It plays once per instance, so a step the user comes back to does not replay it.
/// </summary>
public class BrandMark : GraphicsView, IDrawable
{
    public static readonly BindableProperty MarkColorProperty =
        BindableProperty.Create(nameof(MarkColor), typeof(Color), typeof(BrandMark), Colors.Black, propertyChanged: OnVisualChanged);

    public Color MarkColor
    {
        get => (Color)GetValue(MarkColorProperty);
        set => SetValue(MarkColorProperty, value);
    }

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(BrandMark), Color.FromArgb("#C26D53"), propertyChanged: OnVisualChanged);

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    /// <summary>The thin line under the letter, as on the icon.</summary>
    public static readonly BindableProperty ShowRuleProperty =
        BindableProperty.Create(nameof(ShowRule), typeof(bool), typeof(BrandMark), true, propertyChanged: OnVisualChanged);

    public bool ShowRule
    {
        get => (bool)GetValue(ShowRuleProperty);
        set => SetValue(ShowRuleProperty, value);
    }

    /// <summary>
    /// When true the mark starts invisible and plays its entrance as soon as it is loaded. Off by
    /// default so a mark placed anywhere else is simply there.
    /// </summary>
    public static readonly BindableProperty AnimatesEntranceProperty =
        BindableProperty.Create(nameof(AnimatesEntrance), typeof(bool), typeof(BrandMark), false, propertyChanged: OnAnimatesEntranceChanged);

    public bool AnimatesEntrance
    {
        get => (bool)GetValue(AnimatesEntranceProperty);
        set => SetValue(AnimatesEntranceProperty, value);
    }

    /// <summary>0 = bookmark lifted above its place and transparent, 1 = in place. Driven by the entrance.</summary>
    public static readonly BindableProperty BookmarkProgressProperty =
        BindableProperty.Create(nameof(BookmarkProgress), typeof(double), typeof(BrandMark), 1d, propertyChanged: OnVisualChanged);

    public double BookmarkProgress
    {
        get => (double)GetValue(BookmarkProgressProperty);
        set => SetValue(BookmarkProgressProperty, value);
    }

    // Onboarding is the one screen allowed past Motion.OverlayMs (ui-motion rules, §1), and only for
    // this moment. The drop is 36 icon units, about 12 dp at the 96 dp the welcome step uses.
    private const uint EntranceMs = 360;
    private const float DropDistance = 36f;

    private bool _entrancePlayed;

    public BrandMark()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;

        // Same guard as the charts: a GraphicsView realised before it has a size can miss its first draw.
        Loaded += OnLoaded;
        SizeChanged += (_, _) => Invalidate();
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((BrandMark)bindable).Invalidate();

    private static void OnAnimatesEntranceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var mark = (BrandMark)bindable;
        if ((bool)newValue && !mark._entrancePlayed)
        {
            // Hidden from the first frame, not from Loaded: otherwise the finished mark flashes once
            // before the entrance starts.
            mark.Opacity = 0;
            mark.BookmarkProgress = 0;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Invalidate();
        if (AnimatesEntrance)
            _ = PlayEntranceAsync();
    }

    /// <summary>
    /// Fade the letter in and drop the bookmark into place. Idempotent: the second call returns at
    /// once, so revisiting the welcome step shows the mark already settled.
    /// </summary>
    public async Task PlayEntranceAsync()
    {
        if (_entrancePlayed)
            return;
        _entrancePlayed = true;

        BookmarkProgress = 0;
        Opacity = 0;

        var settled = new TaskCompletionSource();
        new Animation(v => BookmarkProgress = v, 0, 1)
            .Commit(this, nameof(PlayEntranceAsync), length: EntranceMs, easing: Motion.EaseOut,
                finished: (_, _) => settled.TrySetResult());

        await Task.WhenAll(this.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut), settled.Task);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
        if (size <= 0)
            return;

        var scale = size / BrandGeometry.CanvasSize;

        canvas.SaveState();
        canvas.Antialias = true;
        canvas.Translate(dirtyRect.X + (dirtyRect.Width - size) / 2f, dirtyRect.Y + (dirtyRect.Height - size) / 2f);
        canvas.Scale(scale, scale);

        canvas.FillColor = MarkColor;
        canvas.FillPath(BrandGeometry.Letter, WindingMode.EvenOdd);

        if (ShowRule)
        {
            canvas.StrokeColor = MarkColor.WithAlpha(0.22f);
            canvas.StrokeSize = 2;
            canvas.DrawLine(BrandGeometry.RuleX1, BrandGeometry.RuleY, BrandGeometry.RuleX2, BrandGeometry.RuleY);
        }

        // Under reduce motion the drop collapses to zero and only the fade of the bookmark remains.
        var progress = (float)Math.Clamp(BookmarkProgress, 0, 1);
        var lift = (float)Motion.Offset(DropDistance) * (1 - progress);
        canvas.FillColor = AccentColor.WithAlpha(progress);
        canvas.FillPath(BrandGeometry.Bookmark(BrandGeometry.BookmarkX, BrandGeometry.BookmarkY - lift, BrandGeometry.BookmarkWidth));

        canvas.RestoreState();
    }
}
