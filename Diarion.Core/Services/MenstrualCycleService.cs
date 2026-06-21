using System;
using Diarion.Models;

namespace Diarion.Services;

public class MenstrualCycleService : IMenstrualCycleService
{
    public CycleDayInfo GetCycleInfoForDate(DateTime date, UserProfile profile)
    {
        var info = new CycleDayInfo
        {
            IsTrackingEnabled = false,
            Probability = PregnancyProbability.Low
        };

        if (profile == null || !profile.IsMenstrualTrackingEnabled || !profile.LastPeriodStartDate.HasValue)
        {
            return info;
        }

        info.IsTrackingEnabled = true;

        DateTime lastPeriod = profile.LastPeriodStartDate.Value.Date;
        int cycleLength = profile.GetNormalizedCycleLength();
        int periodLength = profile.GetNormalizedPeriodLength();

        int diff = (date.Date - lastPeriod).Days;
        
        int cycleDayIndex = diff % cycleLength;
        if (cycleDayIndex < 0) cycleDayIndex += cycleLength;
        
        int currentCycleDay = cycleDayIndex + 1; // 1-based
        info.DayOfCycle = currentCycleDay;

        if (cycleDayIndex < periodLength)
        {
            if (date.Date <= DateTime.Today)
                info.IsPeriodDay = true;
            else
                info.IsPredictedPeriodDay = true;
        }

        int ovulationDay = cycleLength - 14;
        int fertileStart = ovulationDay - 5;
        int fertileEnd = ovulationDay + 1;

        if (currentCycleDay >= fertileStart && currentCycleDay <= fertileEnd)
        {
            info.IsFertileWindow = true;
            info.Probability = PregnancyProbability.High;
        }
        else if (currentCycleDay >= fertileStart - 2 && currentCycleDay <= fertileEnd + 2)
        {
            info.Probability = PregnancyProbability.Medium;
        }

        return info;
    }
}