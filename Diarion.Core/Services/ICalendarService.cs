using System;
using System.Collections.Generic;
using Diarion.Models;
using Diarion.ViewModels;

namespace Diarion.Services;

public interface ICalendarService
{
    List<CalendarDay> GenerateCalendarDays(DateTime selectedDate);
}