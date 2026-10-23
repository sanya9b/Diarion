using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;
using Diarion.Models.Reports;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Reports;

namespace Diarion.ViewModels.Statistics;

/// <summary>Which way a change went, for the one place colour is added on top of the arrow.</summary>
public enum ReviewTone
{
    Neutral,
    Better,
    Worse
}

/// <summary>
/// The "Review" tab: six tiles and a habit list, each number next to how it moved against the
/// period before. Every delta is spelled with an arrow and a magnitude; the tone is a second
/// encoding, never the only one, because the palette is not distinguishable by hue alone.
/// </summary>
public partial class PeriodReviewViewModel : ObservableObject
{
    private readonly IPeriodReviewService _reviews;
    private readonly IProfileService _profileService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEmpty))]
    private bool _isEmpty = true;

    public bool IsNotEmpty => !IsEmpty;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private string _previousPeriodText = string.Empty;

    [ObservableProperty]
    private string _daysWrittenValue = string.Empty;

    [ObservableProperty]
    private string _daysWrittenDelta = string.Empty;

    [ObservableProperty]
    private ReviewTone _daysWrittenTone;

    [ObservableProperty]
    private string _moodValue = string.Empty;

    [ObservableProperty]
    private string _moodDelta = string.Empty;

    [ObservableProperty]
    private ReviewTone _moodTone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTopEmotion))]
    private string _topEmotionText = string.Empty;

    public bool HasTopEmotion => !string.IsNullOrEmpty(TopEmotionText);

    [ObservableProperty]
    private string _sleepValue = string.Empty;

    [ObservableProperty]
    private string _sleepDelta = string.Empty;

    [ObservableProperty]
    private ReviewTone _sleepTone;

    [ObservableProperty]
    private string _habitsValue = string.Empty;

    [ObservableProperty]
    private string _habitsDelta = string.Empty;

    [ObservableProperty]
    private ReviewTone _habitsTone;

    [ObservableProperty]
    private string _tasksValue = string.Empty;

    [ObservableProperty]
    private string _tasksDelta = string.Empty;

    [ObservableProperty]
    private ReviewTone _tasksTone;

    [ObservableProperty]
    private string _expenseValue = string.Empty;

    [ObservableProperty]
    private string _expenseDelta = string.Empty;

    [ObservableProperty]
    private ReviewTone _expenseTone;

    public ObservableCollection<ReviewHabitRow> HabitRows { get; } = new();

    [ObservableProperty]
    private bool _hasHabitRows;

    public PeriodReviewViewModel(IPeriodReviewService reviews, IProfileService profileService)
    {
        _reviews = reviews;
        _profileService = profileService;
    }

    public async Task LoadDataAsync(StatsRange range)
    {
        IsBusy = true;
        try
        {
            var review = await _reviews.BuildAsync(range);
            var profile = await _profileService.GetUserProfileAsync();
            var currency = profile.GetEffectiveCurrencyCode();
            var culture = CultureInfo.CurrentCulture;

            PeriodText = RangeText(review.Range, culture);
            PreviousPeriodText = string.Format(culture, AppResources.ReviewVersusFormat, RangeText(review.PreviousRange, culture));
            IsEmpty = review.IsEmpty;

            // Days written is the one tile that always has a number: zero of seven is a fact.
            DaysWrittenValue = Of(review.DaysWritten.Current ?? 0, review.DayCount, culture);
            (DaysWrittenDelta, DaysWrittenTone) = Delta(review.DaysWritten, d => d.ToString("0", culture), higherIsBetter: true);

            (MoodValue, MoodDelta, MoodTone) = MoodTile(review.Mood);
            TopEmotionText = TopEmotion(review.TopEmotion, culture);

            (SleepValue, SleepDelta, SleepTone) = Tile(review.SleepHours, Hours, Hours, higherIsBetter: true);

            (HabitsValue, HabitsDelta, HabitsTone) = Tile(review.HabitCompletion, v => v.ToString("P0", culture), d => d.ToString("P0", culture), higherIsBetter: true);

            if (review.TasksTotal.HasCurrent)
            {
                TasksValue = Of(review.TasksCompleted.Current ?? 0, (int)Math.Round(review.TasksTotal.Current!.Value), culture);
                (TasksDelta, TasksTone) = Delta(review.TasksCompleted, d => d.ToString("0", culture), higherIsBetter: true);
            }
            else
            {
                (TasksValue, TasksDelta, TasksTone) = (AppResources.ReviewNoValue, AppResources.ReviewNoData, ReviewTone.Neutral);
            }

            (ExpenseValue, ExpenseDelta, ExpenseTone) = Tile(
                review.Expense,
                v => MoneyFormatter.Format((decimal)v, currency, culture),
                d => MoneyFormatter.Format((decimal)d, currency, culture),
                higherIsBetter: false);

            HabitRows.Clear();
            foreach (var habit in review.Habits)
            {
                var ratio = new ReviewMetric(habit.Ratio, habit.PreviousRatio);
                var (delta, tone) = Delta(ratio, d => d.ToString("P0", culture), higherIsBetter: true);
                HabitRows.Add(new ReviewHabitRow(
                    habit.Name,
                    Of(habit.CompletedDays, habit.ScheduledDays, culture),
                    delta,
                    tone));
            }

            HasHabitRows = HabitRows.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string RangeText(StatsRange range, CultureInfo culture) =>
        string.Concat(range.Start.ToString("d MMM", culture), " – ", range.End.ToString("d MMM", culture));

    private static string Of(double current, int total, CultureInfo culture) =>
        string.Format(culture, AppResources.ReviewOfFormat, current.ToString("0", culture), total);

    private static string TopEmotion(string name, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(name) || !Enum.TryParse<Emotion>(name, out var emotion) || emotion == Emotion.None)
        {
            return string.Empty;
        }

        return string.Format(culture, AppResources.ReviewTopEmotionFormat, emotion.ToLocalizedName());
    }

    /// <summary>"7 год 20 хв", or just the minutes under an hour — a delta of twenty minutes is not "0 год 20 хв".</summary>
    private static string Hours(double hours)
    {
        var total = (int)Math.Round(hours * 60);
        var h = total / 60;
        var m = total % 60;

        return h == 0
            ? $"{m} {AppResources.MinutesShort}"
            : $"{h} {AppResources.HoursShort} {m:00} {AppResources.MinutesShort}";
    }

    /// <summary>Below this the average mood has not moved in any way a person would notice.</summary>
    public const double MoodNoticeableChange = 0.1;

    /// <summary>From here a change is plainly "better", not "a little better".</summary>
    public const double MoodClearChange = 0.5;

    /// <summary>
    /// The period's average valence (−2 unpleasant … +2 pleasant) in a word. A signed decimal such as
    /// "+0.8" meant nothing to anyone who had not read the circumplex paper; "Good" does.
    /// </summary>
    public static string MoodWord(double valence) => valence switch
    {
        < -1.2 => AppResources.ReviewMoodRough,
        < -0.4 => AppResources.ReviewMoodLow,
        <= 0.4 => AppResources.ReviewMoodSteady,
        <= 1.2 => AppResources.ReviewMoodGood,
        _ => AppResources.ReviewMoodGreat
    };

    /// <summary>The mood tile: a word for the period and the change in words, the arrow kept.</summary>
    public static (string Value, string Delta, ReviewTone Tone) MoodTile(ReviewMetric metric)
    {
        if (!metric.HasCurrent)
        {
            return (AppResources.ReviewNoValue, AppResources.ReviewNoData, ReviewTone.Neutral);
        }

        var value = MoodWord(metric.Current!.Value);
        if (metric.Delta is not double delta)
        {
            return (value, AppResources.ReviewNoPrevious, ReviewTone.Neutral);
        }

        var size = Math.Abs(delta);
        if (size < MoodNoticeableChange)
        {
            return (value, AppResources.ReviewNoChange, ReviewTone.Neutral);
        }

        var slight = size < MoodClearChange;
        return delta > 0
            ? (value, slight ? AppResources.ReviewMoodSlightlyBetter : AppResources.ReviewMoodBetter, ReviewTone.Better)
            : (value, slight ? AppResources.ReviewMoodSlightlyWorse : AppResources.ReviewMoodWorse, ReviewTone.Worse);
    }

    private static (string Value, string Delta, ReviewTone Tone) Tile(
        ReviewMetric metric,
        Func<double, string> value,
        Func<double, string> magnitude,
        bool higherIsBetter)
    {
        if (!metric.HasCurrent)
        {
            return (AppResources.ReviewNoValue, AppResources.ReviewNoData, ReviewTone.Neutral);
        }

        var (delta, tone) = Delta(metric, magnitude, higherIsBetter);
        return (value(metric.Current!.Value), delta, tone);
    }

    /// <summary>
    /// The change as text. "No change" is decided on the printed magnitude rather than on the raw
    /// difference, so a mood that moved by three thousandths is not shown as "▲ 0.0".
    /// </summary>
    internal static (string Text, ReviewTone Tone) Delta(ReviewMetric metric, Func<double, string> magnitude, bool higherIsBetter)
    {
        if (!metric.HasCurrent)
        {
            return (AppResources.ReviewNoData, ReviewTone.Neutral);
        }

        if (metric.Delta is not double delta)
        {
            return (AppResources.ReviewNoPrevious, ReviewTone.Neutral);
        }

        var size = magnitude(Math.Abs(delta));
        if (size == magnitude(0))
        {
            return (AppResources.ReviewNoChange, ReviewTone.Neutral);
        }

        var up = delta > 0;
        var arrow = up ? "▲" : "▼";
        var tone = up == higherIsBetter ? ReviewTone.Better : ReviewTone.Worse;
        return ($"{arrow} {size}", tone);
    }
}

/// <summary>One habit in the review list, already phrased.</summary>
public sealed class ReviewHabitRow
{
    public ReviewHabitRow(string name, string valueText, string deltaText, ReviewTone tone)
    {
        Name = name;
        ValueText = valueText;
        DeltaText = deltaText;
        Tone = tone;
    }

    public string Name { get; }

    public string ValueText { get; }

    public string DeltaText { get; }

    public ReviewTone Tone { get; }
}
