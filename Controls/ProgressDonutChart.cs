using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class ProgressDonutChart : GraphicsView, IDrawable
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressDonutChart), 0.0,
            propertyChanged: (bindable, oldValue, newValue) => ((ProgressDonutChart)bindable).Invalidate());

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly BindableProperty ProgressColorProperty =
        BindableProperty.Create(nameof(ProgressColor), typeof(Color), typeof(ProgressDonutChart), Colors.Green,
            propertyChanged: (bindable, oldValue, newValue) => ((ProgressDonutChart)bindable).Invalidate());

    public Color ProgressColor
    {
        get => (Color)GetValue(ProgressColorProperty);
        set => SetValue(ProgressColorProperty, value);
    }

    public static readonly BindableProperty CenterTextProperty =
        BindableProperty.Create(nameof(CenterText), typeof(string), typeof(ProgressDonutChart), string.Empty,
            propertyChanged: (bindable, oldValue, newValue) => ((ProgressDonutChart)bindable).Invalidate());

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public ProgressDonutChart()
    {
        Drawable = this;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var center = new PointF(dirtyRect.Center.X, dirtyRect.Center.Y);
        var radius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2 - 10;
        var thickness = radius * 0.35f;

        var drawRadius = radius - thickness / 2;
        var x = center.X - drawRadius;
        var y = center.Y - drawRadius;
        var size = drawRadius * 2;

        // Draw track
        canvas.StrokeColor = Color.FromArgb("#338FA083");
        canvas.StrokeSize = thickness;
        canvas.DrawArc(x, y, size, size, 0, 360, true, false);

        // Draw progress
        float sweepAngle = (float)(Progress * 360);
        if (sweepAngle > 0)
        {
            float startAngle = 90; 
            float endAngle = startAngle - sweepAngle;

            canvas.StrokeColor = ProgressColor;
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawArc(x, y, size, size, startAngle, endAngle, true, false);
        }

        if (!string.IsNullOrEmpty(CenterText))
        {
            canvas.FontColor = Application.Current?.UserAppTheme == AppTheme.Dark ? Colors.White : Colors.Black;
            canvas.FontSize = radius * 0.5f;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(CenterText, dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
