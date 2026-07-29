using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Diarion.Services;

public interface ICycleLogService
{
    /// <summary>Every day marked as a period day, ascending. The whole history — it is a few dozen rows a year.</summary>
    Task<List<DateTime>> GetMarkedDatesAsync();

    /// <summary>Marks or unmarks a day; returns the resulting state. Future dates are ignored and return false.</summary>
    Task<bool> ToggleAsync(DateTime date);
}
