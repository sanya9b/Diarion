using System;
using System.Collections.Generic;
using Diarion.Models;

namespace Diarion.Services;

public interface ICalendarService
{
    List<CalendarDay> GenerateCalendarDays(DateTime selectedDate);
}