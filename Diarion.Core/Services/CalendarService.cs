using System;
using System.Collections.Generic;
using Diarion.Models;

namespace Diarion.Services;

public class CalendarService : ICalendarService
{
    public List<CalendarDay> GenerateCalendarDays(DateTime selectedDate)
    {
        var calendarDays = new List<CalendarDay>(42);

        var firstDayOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
        
        int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
        if (startDayOfWeek == 0) startDayOfWeek = 7; 

        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        
        for (int i = startDayOfWeek - 1; i > 0; i--)
        {
            calendarDays.Add(new CalendarDay 
            { 
                Day = daysInPrevMonth - i + 1, 
                IsCurrentMonth = false,
                Date = new DateTime(prevMonth.Year, prevMonth.Month, daysInPrevMonth - i + 1)
            });
        }

        var today = DateTime.Today;
        for (int i = 1; i <= daysInMonth; i++)
        {
            var currentDate = new DateTime(selectedDate.Year, selectedDate.Month, i);
            calendarDays.Add(new CalendarDay 
            { 
                Day = i, 
                IsCurrentMonth = true,
                IsToday = currentDate.Date == today,
                IsSelected = currentDate.Date == selectedDate.Date,
                Date = currentDate
            });
        }

        int totalNeeded = calendarDays.Count <= 35 ? 35 : 42;
        int remainingSlots = totalNeeded - calendarDays.Count;
        var nextMonth = firstDayOfMonth.AddMonths(1);
        for (int i = 1; i <= remainingSlots; i++)
        {
            calendarDays.Add(new CalendarDay 
            { 
                Day = i, 
                IsCurrentMonth = false,
                Date = new DateTime(nextMonth.Year, nextMonth.Month, i)
            });
        }

        return calendarDays;
    }
}