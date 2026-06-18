using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class EmotionDonutChart : GraphicsView, IDrawable
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.Models.EmotionChartItem>), typeof(EmotionDonutChart), null,
            propertyChanged: (bindable, oldValue, newValue) => ((EmotionDonutChart)bindable).Invalidate());

    public IEnumerable<Diarion.Models.EmotionChartItem>? Items
    {
        get => (IEnumerable<Diarion.Models.EmotionChartItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty CenterTextProperty =
        BindableProperty.Create(nameof(CenterText), typeof(string), typeof(EmotionDonutChart), string.Empty,
            propertyChanged: (bindable, oldValue, newValue) => ((EmotionDonutChart)bindable).Invalidate());

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public EmotionDonutChart()
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

        if (Items == null || !Items.Any())
        {
            canvas.StrokeColor = Color.FromArgb("#338FA083");
            canvas.StrokeSize = thickness;
            canvas.DrawArc(x, y, size, size, 0, 360, true, false);
            return;
        }

        float startAngle = 90; // Start at 12 o'clock in MAUI

        foreach (var item in Items)
        {
            float sweepAngle = (float)(item.Percentage * 360);
            if (sweepAngle <= 0) continue;

            float endAngle = startAngle - sweepAngle; // Negative because MAUI angle goes counter-clockwise for positive

            canvas.StrokeColor = item.Color;
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = LineCap.Round;
            
            // DrawArc signature: x, y, width, height, startAngle, endAngle, clockwise, closed
            // Since we use negative sweep, we set clockwise=true to draw in the correct direction.
            // Wait, in MAUI clockwise means drawing from start to end in clockwise direction.
            // So if start=90, end=90-sweep, it draws clockwise.
            canvas.DrawArc(x, y, size, size, startAngle, endAngle, true, false);
            
            startAngle = endAngle;
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
