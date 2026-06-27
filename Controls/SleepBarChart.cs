using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class SleepBarChart : GraphicsView, IDrawable
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.ViewModels.SleepBarChartItem>), typeof(SleepBarChart), null,
            propertyChanged: (bindable, oldValue, newValue) => ((SleepBarChart)bindable).Invalidate());

    public IEnumerable<Diarion.ViewModels.SleepBarChartItem>? Items
    {
        get => (IEnumerable<Diarion.ViewModels.SleepBarChartItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty MaxValueProperty =
        BindableProperty.Create(nameof(MaxValue), typeof(double), typeof(SleepBarChart), 12.0,
            propertyChanged: (bindable, oldValue, newValue) => ((SleepBarChart)bindable).Invalidate());

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public SleepBarChart()
    {
        Drawable = this;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        if (Items == null || !Items.Any())
        {
            return;
        }

        var items = Items.ToList();
        int count = items.Count;

        // Configuration
        float padding = 4f;
        float labelHeight = 20f;
        float graphHeight = dirtyRect.Height - labelHeight - padding * 2;
        float availableWidth = dirtyRect.Width - padding * 2;
        float spacing = availableWidth * 0.2f / count; // 20% of width is spacing
        float barWidth = (availableWidth * 0.8f) / count;

        float currentX = padding + spacing / 2;

        Color amber = Color.FromArgb("#C9985A");
        Color dust = Color.FromArgb("#D0D3D4");
        Color labelColor = Application.Current?.UserAppTheme == AppTheme.Dark ? Colors.White : Colors.Black;

        foreach (var item in items)
        {
            // Calculate height proportional to max value
            double val = item.Value > MaxValue ? MaxValue : item.Value;
            float barHeight = (float)((val / MaxValue) * graphHeight);
            if (barHeight < 4) barHeight = 4; // minimum height

            float y = padding + graphHeight - barHeight;

            // Draw Bar Background (optional)
            // canvas.FillColor = dust.WithAlpha(0.2f);
            // canvas.FillRoundedRectangle(currentX, padding, barWidth, graphHeight, 4);

            // Draw Bar
            canvas.FillColor = amber;
            canvas.FillRoundedRectangle(currentX, y, barWidth, barHeight, 4);

            // Draw Label
            canvas.FontColor = dust;
            canvas.FontSize = 10;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            
            var textRect = new RectF(currentX - spacing/2, padding + graphHeight + 4, barWidth + spacing, labelHeight);
            canvas.DrawString(item.Label, textRect, HorizontalAlignment.Center, VerticalAlignment.Top);

            currentX += barWidth + spacing;
        }
    }
}
