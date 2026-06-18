using Microsoft.Maui.Graphics;

namespace Diarion.Models;

public class EmotionChartItem
{
    public string Name { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public Color Color { get; set; } = Colors.Gray;
}

public class SleepBarChartItem
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; } // e.g. hours
}
