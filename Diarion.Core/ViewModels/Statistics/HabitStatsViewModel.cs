using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels.Statistics;

/// <summary>One habit's card on the Habits tab: name, recency-weighted strength, current streak, and the
/// completed dates that feed its contribution heatmap.</summary>
public class HabitCardViewModel
{
    public string Name { get; set; } = string.Empty;
    public string ScheduleText { get; set; } = string.Empty;
    public string StrengthText { get; set; } = "0%";
    public string StreakText { get; set; } = "0";
    public IReadOnlyList<DateTime> CompletedDates { get; set; } = Array.Empty<DateTime>();
    public DateTime RangeStart { get; set; }
    public DateTime RangeEnd { get; set; }
}

public partial class HabitStatsViewModel : ObservableObject
{
    private readonly IHabitService _habitService;

    // Strength is a 30-day-half-life EMA; ~180 days of history is enough for it to fully converge.
    private readonly IProfileService _profileService;

    private const int StrengthLookbackDays = 180;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    public ObservableCollection<HabitCardViewModel> Habits { get; } = new();

    public HabitStatsViewModel(IHabitService habitService, IProfileService profileService)
    {
        _habitService = habitService;
        _profileService = profileService;
    }

    public async Task LoadDataAsync(StatsRange range)
    {
        IsBusy = true;
        try
        {
            range = range.Normalized();
            var today = DateTime.Today;

            // The heatmap follows the selected period, but strength and streak stay anchored to today
            // on purpose: they answer "where is this habit now", which is a fact about the present and
            // not a property of whatever window happens to be on screen. Reading a chosen March would
            // otherwise report a streak that ended in March as if it were still running.
            var rangeStart = range.Start;
            var strengthStart = today.AddDays(-(StrengthLookbackDays - 1));
            var fetchStart = rangeStart < strengthStart ? rangeStart : strengthStart;

            var histories = await _habitService.GetHabitCompletionsAsync(fetchStart, today);
            var grace = (await _profileService.GetUserProfileAsync())?.GetEffectiveStreakGrace() ?? 0;

            Habits.Clear();
            foreach (var h in histories)
            {
                var from = h.CreatedAt > strengthStart ? h.CreatedAt : strengthStart;
                var strength = HabitStrengthCalculator.Strength(h.CompletedDates, from, today, h.Schedule, h.Target);
                var streak = HabitStrengthCalculator.CurrentStreak(h.CompletedDates, today, h.Schedule, h.Target, grace);

                // A quota streak counts weeks, not days — mark it so "🔥 5" isn't misread.
                var isWeekly = h.Target != null;
                var streakText = isWeekly
                    ? streak.ToString(CultureInfo.CurrentCulture) + " " + Diarion.Resources.Localization.AppResources.HabitStreakWeeksSuffix
                    : streak.ToString(CultureInfo.CurrentCulture);

                Habits.Add(new HabitCardViewModel
                {
                    Name = h.Name,
                    ScheduleText = HabitScheduleFormatter.Describe(h.Schedule, h.Target),
                    StrengthText = strength.ToString("0", CultureInfo.CurrentCulture) + "%",
                    StreakText = streakText,
                    CompletedDates = h.CompletedDates.ToList(),
                    RangeStart = rangeStart,
                    RangeEnd = range.End
                });
            }

            IsEmpty = Habits.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
