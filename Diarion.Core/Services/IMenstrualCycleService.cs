using System;
using Diarion.Models;

namespace Diarion.Services;

public interface IMenstrualCycleService
{
    CycleDayInfo GetCycleInfoForDate(DateTime date, UserProfile profile);
}