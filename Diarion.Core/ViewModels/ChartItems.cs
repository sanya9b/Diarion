using Microsoft.Maui.Graphics;

namespace Diarion.ViewModels;

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

public class MoodCorrelationItem
{
    public string Description { get; set; } = string.Empty;
    public string Dots { get; set; } = string.Empty; // e.g. "●●●○○"
}

/// <summary>One bucket of the money trend: two bars sharing a slot, one up and one down.</summary>
public class DivergingBarChartItem
{
    public string Label { get; set; } = string.Empty;
    public double Income { get; set; }
    public double Expense { get; set; }

    /// <summary>Covers fewer days than a whole bucket, so it is drawn faded — a short bar would
    /// otherwise read as a drop in spending rather than as a clipped edge of the window.</summary>
    public bool IsPartial { get; set; }
}
