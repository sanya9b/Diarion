namespace Diarion.Models;

public enum PregnancyProbability
{
    Low,
    Medium,
    High
}

public class CycleDayInfo
{
    public bool IsTrackingEnabled { get; set; }
    public int DayOfCycle { get; set; }
    public bool IsPeriodDay { get; set; }
    public bool IsPredictedPeriodDay { get; set; }
    public bool IsFertileWindow { get; set; }
    public PregnancyProbability Probability { get; set; }
}