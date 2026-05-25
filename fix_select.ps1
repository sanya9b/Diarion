$text = [System.IO.File]::ReadAllText('Diarion.Core/ViewModels/MainViewModel.cs', [System.Text.UTF8Encoding]::new($false))

$old = "    [RelayCommand]
    public void SelectDate(CalendarDay selectedDay)
    {
        if (selectedDay == null) return;
        
        foreach (var day in CalendarDays)
        {
            day.IsSelected = false;
        }
        selectedDay.IsSelected = true;
        
        SelectedDateDayName = selectedDay.Date.ToString(\"dddd\", CultureInfo.CurrentCulture);
        FilterEntriesByDate(selectedDay.Date);
    }"

$new = "    [RelayCommand]
    public void SelectDate(CalendarDay selectedDay)
    {
        if (selectedDay == null) return;
        
        if (!selectedDay.IsCurrentMonth)
        {
            // If the user clicked a day from the previous/next month, 
            // switch the calendar to that month instead of just selecting it.
            _currentCalendarDate = new DateTime(selectedDay.Date.Year, selectedDay.Date.Month, 1);
            GenerateCalendar(_currentCalendarDate);
            
            // Re-select the day in the newly generated calendar
            var newSelectedDay = CalendarDays.FirstOrDefault(d => d.Date.Date == selectedDay.Date.Date);
            if (newSelectedDay != null)
            {
                foreach (var day in CalendarDays) day.IsSelected = false;
                newSelectedDay.IsSelected = true;
            }
        }
        else
        {
            foreach (var day in CalendarDays)
            {
                day.IsSelected = false;
            }
            selectedDay.IsSelected = true;
        }
        
        SelectedDateDayName = selectedDay.Date.ToString(\"dddd\", CultureInfo.CurrentCulture);
        FilterEntriesByDate(selectedDay.Date);
    }"

$text = $text.Replace($old, $new)
[System.IO.File]::WriteAllText('Diarion.Core/ViewModels/MainViewModel.cs', $text, [System.Text.UTF8Encoding]::new($false))
