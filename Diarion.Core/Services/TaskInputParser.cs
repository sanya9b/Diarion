using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>What a line of typed text turned out to be asking for.</summary>
public sealed class ParsedTaskInput
{
    /// <summary>The text with every recognized phrase cut out.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The original text, so the UI can put it back if the reading was wrong.</summary>
    public string OriginalText { get; init; } = string.Empty;

    public RecurrenceRule? Recurrence { get; init; }

    /// <summary>A one-off day. Never set together with <see cref="Recurrence"/>.</summary>
    public DateTime? Date { get; init; }

    public TimeSpan? TimeOfDay { get; init; }

    /// <summary>Where a spoken stretch of the day ends — "з 13:00 до 16:00". Never set without <see cref="TimeOfDay"/>.</summary>
    public TimeSpan? EndTimeOfDay { get; init; }

    /// <summary>The phrases consumed, in the order they appeared, for showing the user what was read.</summary>
    public IReadOnlyList<string> Matched { get; init; } = Array.Empty<string>();

    public bool FoundAnything => Recurrence != null || Date != null || TimeOfDay != null;
}

/// <summary>
/// Reads a task out of a line of ordinary typing: "щовівторка о 18:00 теніс" is a weekly rule, a time and
/// a task called "теніс".
///
/// Both language grammars run on every input rather than one being chosen by culture. Their vocabularies
/// do not overlap — no Ukrainian phrase here is also an English one — so running both costs nothing and
/// means a user typing in the other language is still understood.
///
/// Pure: today is passed in, nothing is read or written, and the result is data. The ViewModel decides
/// what to do with it, which is what lets every trap below be pinned by a test.
/// </summary>
public static class TaskInputParser
{
    // Accusative — "у четвер". Index is (int)DayOfWeek.
    private static readonly string[][] WeekdayAccusative =
    {
        new[] { "неділю", "sunday" },
        new[] { "понеділок", "monday" },
        new[] { "вівторок", "tuesday" },
        new[] { "середу", "wednesday" },
        new[] { "четвер", "thursday" },
        new[] { "п'ятницю", "friday" },
        new[] { "суботу", "saturday" }
    };

    // Genitive and locative-plural — "кожного четверга", "по четвергах", and the що- forms.
    private static readonly string[][] WeekdayRecurring =
    {
        new[] { "неділі", "неділях", "sundays", "sunday" },
        new[] { "понеділка", "понеділках", "mondays", "monday" },
        new[] { "вівторка", "вівторках", "tuesdays", "tuesday" },
        new[] { "середи", "середах", "wednesdays", "wednesday" },
        new[] { "четверга", "четвергах", "thursdays", "thursday" },
        new[] { "п'ятниці", "п'ятницях", "fridays", "friday" },
        new[] { "суботи", "суботах", "saturdays", "saturday" }
    };

    private static readonly string[] UkMonthsGenitive =
    {
        "січня", "лютого", "березня", "квітня", "травня", "червня",
        "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"
    };

    private static readonly string[] EnMonths =
    {
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december"
    };

    public static ParsedTaskInput Parse(string? input, DateTime today)
    {
        var original = input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return new ParsedTaskInput { Description = original, OriginalText = original };
        }

        // Normalized copy, kept the same length as the original so every index found here also indexes
        // the original. Consumed ranges are blanked with spaces, which both prevents a second match and
        // keeps that alignment.
        var work = new StringBuilder(Normalize(original));
        var spans = new List<(int Start, int Length)>();

        var recurrence = TryRecurrence(work, spans, today);
        var date = TryAbsoluteDate(work, spans, today);

        // A bare weekday is only a schedule when nothing more specific was said. Next to an explicit date
        // — "у четвер 26 серпня" — it is the user confirming the day, not asking for every Thursday, so it
        // is consumed and thrown away.
        var weekday = TryBareWeekday(work, spans, today);
        if (recurrence == null && date == null) date = weekday;

        // The range first: it owns both of its clock times, and the single-time patterns below would
        // otherwise take the "13:00" out of "з 13:00 до 16:00" and leave "з до 16:00" in the task's name.
        var (time, endTime) = TryTimeRange(work, spans, out var rangeRefused);
        if (time == null && !rangeRefused) time = TryTime(work, spans);

        return new ParsedTaskInput
        {
            Description = Cut(original, spans),
            OriginalText = original,
            Recurrence = recurrence,
            Date = recurrence == null ? date : null,
            TimeOfDay = time,
            EndTimeOfDay = time == null ? null : endTime,
            Matched = spans.OrderBy(s => s.Start)
                           .Select(s => original.Substring(s.Start, s.Length).Trim())
                           .Where(s => s.Length > 0)
                           .ToList()
        };
    }

    private static string Normalize(string text)
    {
        var lowered = text.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < lowered.Length; i++)
        {
            // One apostrophe, so "п'ятниця" matches however the keyboard produced it. All single chars,
            // so the length — and therefore every index into the original — is untouched.
            if (lowered[i] is '’' or 'ʼ' or '`' or '´') lowered[i] = '\'';
        }
        return new string(lowered);
    }

    private static bool Take(StringBuilder work, Regex pattern, List<(int, int)> spans, out Match match)
    {
        match = pattern.Match(work.ToString());
        if (!match.Success) return false;
        Consume(work, spans, match.Index, match.Length);
        return true;
    }

    private static void Consume(StringBuilder work, List<(int, int)> spans, int start, int length)
    {
        for (var i = start; i < start + length; i++) work[i] = ' ';
        spans.Add((start, length));
    }

    private static string Cut(string original, List<(int Start, int Length)> spans)
    {
        var kept = new StringBuilder(original);
        foreach (var (start, length) in spans.OrderByDescending(s => s.Start))
        {
            kept.Remove(start, length);
        }

        // Collapse what the removal left behind. Cutting two phrases out of "щовівторка і щочетверга"
        // strands the "і" that joined them, so connectors go too — but only at the edges, where they can
        // only be leftovers rather than part of what the user wrote.
        var text = Regex.Replace(kept.ToString(), @"\s+", " ").Trim();
        var edges = new Regex(@"^(?:[,\-–—]|\bі\b|\bта\b|\band\b)\s*|\s*(?:[,\-–—]|\bі\b|\bта\b|\band\b)$");
        string previous;
        do
        {
            previous = text;
            text = edges.Replace(text, string.Empty).Trim();
        } while (text != previous);

        return text;
    }

    private static RecurrenceRule? TryRecurrence(StringBuilder work, List<(int, int)> spans, DateTime today)
    {
        // Every N days first: "кожні" would otherwise be read as the marker of a weekday list.
        var interval = new Regex(@"кожн[іи]\s+(\d{1,3})\s+(?:дн[іія]в?|день)|every\s+(\d{1,3})\s+days?");
        if (Take(work, interval, spans, out var m))
        {
            var n = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
            return new RecurrenceRule { Kind = RecurrenceKind.IntervalDays, EveryN = Math.Max(1, n) };
        }

        var monthly = new Regex(@"щомісяця\s+(\d{1,2})(?:-?го)?|кожного\s+місяця\s+(\d{1,2})(?:-?го)?|(?:every\s+month|monthly)\s+on\s+the\s+(\d{1,2})(?:st|nd|rd|th)?");
        if (Take(work, monthly, spans, out m))
        {
            var raw = new[] { m.Groups[1], m.Groups[2], m.Groups[3] }.First(g => g.Success).Value;
            return new RecurrenceRule
            {
                Kind = RecurrenceKind.MonthlyByDay,
                DayOfMonth = Math.Clamp(int.Parse(raw, CultureInfo.InvariantCulture), 1, 31)
            };
        }

        var days = CollectRecurringWeekdays(work, spans);
        if (days.Count > 0)
        {
            return new RecurrenceRule { Kind = RecurrenceKind.Weekly, DaysOfWeek = days };
        }

        var daily = new Regex(@"щоденно|щодня|кожного\s+дня|кожен\s+день|every\s?day|daily");
        if (Take(work, daily, spans, out _))
        {
            return new RecurrenceRule { Kind = RecurrenceKind.Daily };
        }

        // "щотижня" with no day named means the day the task already sits on.
        var weekly = new Regex(@"щотижня|кожного\s+тижня|every\s+week|weekly");
        if (Take(work, weekly, spans, out _))
        {
            return new RecurrenceRule
            {
                Kind = RecurrenceKind.Weekly,
                DaysOfWeek = new List<int> { (int)today.DayOfWeek }
            };
        }

        return null;
    }

    /// <summary>
    /// Every weekday named in a repeating context: the attached що- forms, and any list following a marker
    /// like "по" or "every". Lists matter — "щовівторка і щочетверга" is one rule with two days, and read
    /// as two rules it would be two tasks a week apart.
    /// </summary>
    private static List<int> CollectRecurringWeekdays(StringBuilder work, List<(int, int)> spans)
    {
        var found = new SortedSet<int>();

        // The attached form: "щовівторка". Ukrainian only — there is no "щоtuesday".
        for (var day = 0; day < 7; day++)
        {
            foreach (var form in WeekdayRecurring[day].Where(f => !IsLatin(f)))
            {
                var prefixed = new Regex(@"що" + Regex.Escape(form) + @"\b");
                while (Take(work, prefixed, spans, out _)) found.Add(day);
            }
        }

        // English plurals stand alone: "tuesdays" needs no marker in front of it.
        for (var day = 0; day < 7; day++)
        {
            var plural = WeekdayRecurring[day].FirstOrDefault(f => IsLatin(f) && f.EndsWith("s"));
            if (plural == null) continue;
            var re = new Regex(@"\b" + Regex.Escape(plural) + @"\b");
            while (Take(work, re, spans, out _)) found.Add(day);
        }

        var marker = new Regex(@"\b(?:по|кожного|кожної|кожен|кожну|every|each)\b");
        var tail = new Regex(@"\G\s*(?:,|і|та|and)?\s*(" + AnyWeekdayForm() + @")\b");
        var searchFrom = 0;
        while (true)
        {
            var text = work.ToString();
            if (searchFrom > text.Length) break;

            var m = marker.Match(text, searchFrom);
            if (!m.Success) break;

            var consumedTo = m.Index + m.Length;
            var daysHere = new List<int>();

            while (true)
            {
                var next = tail.Match(text, consumedTo);
                if (!next.Success) break;

                daysHere.Add(WeekdayOf(next.Groups[1].Value));
                consumedTo = next.Index + next.Length;
            }

            if (daysHere.Count == 0)
            {
                // A marker with no weekday after it: "по дорозі", or the "кожен" of "кожен день". Stepped
                // over rather than blanked — blanking it would eat the first half of a phrase that the
                // daily and monthly patterns still have to be able to read.
                searchFrom = m.Index + m.Length;
                continue;
            }

            Consume(work, spans, m.Index, consumedTo - m.Index);
            foreach (var d in daysHere) found.Add(d);
            searchFrom = 0;
        }

        return found.ToList();
    }

    private static bool IsLatin(string s) => s.All(c => c is >= 'a' and <= 'z');

    private static string AnyWeekdayForm()
    {
        var forms = new List<string>();
        for (var day = 0; day < 7; day++)
        {
            forms.AddRange(WeekdayRecurring[day]);
            forms.AddRange(WeekdayAccusative[day]);
        }
        // Longest first, so "вівторках" is not cut short by "вівторка".
        return string.Join("|", forms.Distinct().OrderByDescending(f => f.Length).Select(Regex.Escape));
    }

    private static int WeekdayOf(string form)
    {
        for (var day = 0; day < 7; day++)
        {
            if (WeekdayRecurring[day].Contains(form) || WeekdayAccusative[day].Contains(form)) return day;
        }
        return 0;
    }

    private static DateTime? TryAbsoluteDate(StringBuilder work, List<(int, int)> spans, DateTime today)
    {
        var relative = new Regex(@"післязавтра|day\s+after\s+tomorrow|завтра|tomorrow|сьогодні|today");
        if (Take(work, relative, spans, out var m))
        {
            return m.Value switch
            {
                "післязавтра" or "day after tomorrow" => today.AddDays(2),
                "завтра" or "tomorrow" => today.AddDays(1),
                _ => today
            };
        }

        var ukMonths = string.Join("|", UkMonthsGenitive);
        var enMonths = string.Join("|", EnMonths);
        var named = new Regex($@"(\d{{1,2}})\s+({ukMonths}|{enMonths})\b|\b({enMonths})\s+(\d{{1,2}})\b");
        if (Take(work, named, spans, out m))
        {
            var dayText = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[4].Value;
            var monthText = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            var month = Array.IndexOf(UkMonthsGenitive, monthText);
            if (month < 0) month = Array.IndexOf(EnMonths, monthText);
            return BuildDate(int.Parse(dayText, CultureInfo.InvariantCulture), month + 1, today);
        }

        var numeric = new Regex(@"\b(\d{1,2})[./](\d{1,2})(?:[./](\d{2,4}))?\b");
        if (Take(work, numeric, spans, out m))
        {
            var day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12) return null;

            if (m.Groups[3].Success)
            {
                var year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (year < 100) year += 2000;
                return SafeDate(year, month, day);
            }
            return BuildDate(day, month, today);
        }

        return null;
    }

    /// <summary>A day and month with no year means the next time it comes round, not one already gone.</summary>
    private static DateTime? BuildDate(int day, int month, DateTime today)
    {
        var candidate = SafeDate(today.Year, month, day);
        if (candidate == null) return null;
        return candidate < today.Date ? SafeDate(today.Year + 1, month, day) : candidate;
    }

    private static DateTime? SafeDate(int year, int month, int day)
    {
        if (month is < 1 or > 12) return null;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day);
    }

    private static DateTime? TryBareWeekday(StringBuilder work, List<(int, int)> spans, DateTime today)
    {
        var forms = new List<string>();
        for (var day = 0; day < 7; day++) forms.AddRange(WeekdayAccusative[day]);
        var pattern = string.Join("|", forms.OrderByDescending(f => f.Length).Select(Regex.Escape));

        var re = new Regex($@"(?:\b(?:[ув]|on)\s+)?\b({pattern})\b");
        if (!Take(work, re, spans, out var m)) return null;

        var target = WeekdayOf(m.Groups[1].Value);
        var ahead = ((target - (int)today.DayOfWeek) + 7) % 7;
        // Today already has a name; saying it out loud means the next one.
        if (ahead == 0) ahead = 7;
        return today.Date.AddDays(ahead);
    }

    /// <summary>
    /// A stretch of the day said out loud: "з 13:00 до 16:00", "13:00-16:00", "from 1pm to 4pm".
    ///
    /// A dash is only read as a range when both sides carry minutes or both carry am/pm, because a bare
    /// dash between two numbers is far more often not a time at all — "купити 2-3 яблука", "5-10 хвилин".
    /// The word "до" is evidence in its own right, so it may join bare hours: "з 13 до 16".
    /// </summary>
    /// <param name="refused">
    /// True when the text is plainly a range but not one that can be drawn — "з 16:00 до 13:00". The caller
    /// stops there instead of falling through to the single-time patterns, which would take the "16:00" and
    /// leave "до 13:00" sitting in the task's name. A phrase read halfway is harder to spot, and to undo,
    /// than one left alone.
    /// </param>
    private static (TimeSpan? Start, TimeSpan? End) TryTimeRange(StringBuilder work, List<(int, int)> spans, out bool refused)
    {
        refused = false;
        var lead = @"(?:\b(?:з|від|from)\s+)?";

        // Both sides with minutes; the separator may be a dash, because the colons already say these are
        // clock times.
        var clock = new Regex(lead + @"\b(\d{1,2}):(\d{2})\s*(?:[-–—]|\b(?:до|to)\b)\s*(\d{1,2}):(\d{2})\b");
        if (TakeRange(work, spans, clock,
                m => SafeTime(Int(m, 1), Int(m, 2)),
                m => SafeTime(Int(m, 3), Int(m, 4)), ref refused, out var range))
        {
            return range;
        }

        var amPm = new Regex(@"(?:\bfrom\s+)?\b(\d{1,2})\s?(am|pm)\s*(?:[-–—]|\bto\b)\s*(\d{1,2})\s?(am|pm)\b");
        if (TakeRange(work, spans, amPm,
                m => SafeTime(Hour12(Int(m, 1), m.Groups[2].Value), 0),
                m => SafeTime(Hour12(Int(m, 3), m.Groups[4].Value), 0), ref refused, out range))
        {
            return range;
        }

        // Bare hours, and therefore only when the word "до" or "to" joins them. The lookahead is the same
        // guard the bare single hour carries: "від 5 до 10 хвилин" is a quantity, not an afternoon.
        var spelled = new Regex(lead + @"\b(\d{1,2})(?::(\d{2}))?\s*\b(?:до|to)\b\s*(\d{1,2})(?::(\d{2}))?\b(?!\s*(?:хв|хвилин|min|стор|раз|грн|км|кг|%))");
        if (TakeRange(work, spans, spelled,
                m => SafeTime(Int(m, 1), m.Groups[2].Success ? Int(m, 2) : 0),
                m => SafeTime(Int(m, 3), m.Groups[4].Success ? Int(m, 4) : 0), ref refused, out range))
        {
            return range;
        }

        return (null, null);
    }

    /// <summary>
    /// Matches, checks that the two ends make a real stretch of time, and only then consumes the phrase.
    /// A range read backwards — "з 16:00 до 13:00" — is left in the text rather than half-applied: the
    /// task keeps the words the user typed, which is more use to them than a block covering no hours.
    /// </summary>
    private static bool TakeRange(
        StringBuilder work,
        List<(int, int)> spans,
        Regex pattern,
        Func<Match, TimeSpan?> readStart,
        Func<Match, TimeSpan?> readEnd,
        ref bool refused,
        out (TimeSpan? Start, TimeSpan? End) range)
    {
        range = (null, null);

        var match = pattern.Match(work.ToString());
        if (!match.Success) return false;

        var start = readStart(match);
        var end = readEnd(match);
        if (start == null || end == null || end <= start)
        {
            refused = true;
            return false;
        }

        Consume(work, spans, match.Index, match.Length);
        range = (start, end);
        return true;
    }

    private static int Int(Match m, int group)
        => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    private static int Hour12(int hour, string suffix)
        => hour % 12 + (suffix == "pm" ? 12 : 0);

    private static TimeSpan? TryTime(StringBuilder work, List<(int, int)> spans)
    {
        var withMinutes = new Regex(@"(?:\b(?:о|на|at)\s+)?\b(\d{1,2}):(\d{2})\b");
        if (Take(work, withMinutes, spans, out var m))
        {
            return SafeTime(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        var amPm = new Regex(@"\b(?:at\s+)?(\d{1,2})\s?(am|pm)\b");
        if (Take(work, amPm, spans, out m))
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) % 12;
            if (m.Groups[2].Value == "pm") hour += 12;
            return SafeTime(hour, 0);
        }

        // A bare hour needs its marker: "о 18" is a time, "18" on its own is part of the task.
        var bare = new Regex(@"\b(?:о|at)\s+(\d{1,2})\b(?!\s*(?:хв|min))");
        if (Take(work, bare, spans, out m))
        {
            return SafeTime(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0);
        }

        return null;
    }

    private static TimeSpan? SafeTime(int hour, int minute)
        => hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? new TimeSpan(hour, minute, 0) : null;
}
