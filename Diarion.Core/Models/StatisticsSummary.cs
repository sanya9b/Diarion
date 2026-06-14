using System;
using System.Collections.Generic;

namespace Diarion.Models;

public class SleepStatistics
{
    public TimeSpan AverageSleepDuration { get; set; }
    public double AverageSleepQuality { get; set; }
}

public class MoodStatistics
{
    public Dictionary<Emotion, int> EmotionCounts { get; set; } = new();
    public Emotion TopEmotion { get; set; } = Emotion.None;
}
