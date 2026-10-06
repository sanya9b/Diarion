using System.Globalization;
using Diarion.Models.Ai.Reports;
using Diarion.Services.Ai.Reports;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class SnapshotSerializerTests
{
    private static PeriodSnapshot Minimal() => new()
    {
        PeriodKind = "week",
        Start = "2026-08-03",
        End = "2026-08-09",
        DayCount = 7,
        Language = "uk",
        Currency = "UAH"
    };

    [Fact]
    public void Ukrainian_stays_readable()
    {
        var snapshot = Minimal();
        snapshot.Days.Add(new SnapshotDay { Date = "2026-08-04", Text = "Що за тиждень" });

        var json = SnapshotSerializer.ToJson(snapshot);

        // The default encoder would render this as Що..., which is unreadable on the very
        // screen where the user is asked to approve it.
        json.Should().Contain("Що за тиждень");
        json.Should().NotContain("\\u0429");
    }

    [Fact]
    public void Property_names_are_camel_case()
    {
        var json = SnapshotSerializer.ToJson(Minimal());

        json.Should().Contain("\"periodKind\": \"week\"");
        json.Should().Contain("\"dayCount\": 7");
        json.Should().NotContain("\"PeriodKind\"");
    }

    [Fact]
    public void An_empty_day_is_a_date_and_nothing_else()
    {
        var snapshot = Minimal();
        snapshot.Days.Add(new SnapshotDay { Date = "2026-08-04" });

        var json = SnapshotSerializer.ToJson(snapshot);

        json.Should().Contain("\"date\": \"2026-08-04\"");
        json.Should().NotContain("\"text\"");
        json.Should().NotContain("\"gratitude\"");
    }

    [Fact]
    public void Cycle_is_absent_rather_than_null_when_it_was_not_included()
    {
        SnapshotSerializer.ToJson(Minimal()).Should().NotContain("\"cycle\"");
    }

    [Fact]
    public void A_logged_night_and_a_missing_one_are_told_apart()
    {
        var snapshot = Minimal();
        snapshot.Sleep.Daily.Add(new SnapshotSleepDay { Date = "2026-08-03", Hours = 7.5, Quality = 4 });
        snapshot.Sleep.Daily.Add(new SnapshotSleepDay { Date = "2026-08-04" });

        var json = SnapshotSerializer.ToJson(snapshot);

        json.Should().Contain("\"hours\": 7.5");
        json.Should().NotContain("\"hours\": 0");
    }

    [Fact]
    public void The_same_snapshot_serializes_to_the_same_bytes()
    {
        var first = SnapshotSerializer.ToJson(Minimal());
        var second = SnapshotSerializer.ToJson(Minimal());

        second.Should().Be(first);
    }

    [Fact]
    public void Numbers_do_not_change_shape_with_the_culture()
    {
        var snapshot = Minimal();
        snapshot.Sleep.Daily.Add(new SnapshotSleepDay { Date = "2026-08-03", Hours = 7.5 });

        var original = CultureInfo.CurrentCulture;
        try
        {
            // A locale that writes 7,5. JSON has no opinion about commas, and neither may the payload.
            CultureInfo.CurrentCulture = new CultureInfo("uk-UA");
            SnapshotSerializer.ToJson(snapshot).Should().Contain("\"hours\": 7.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void MeasureCharacters_counts_what_would_actually_be_sent()
    {
        var snapshot = Minimal();

        SnapshotSerializer.MeasureCharacters(snapshot)
            .Should().Be(SnapshotSerializer.ToJson(snapshot).Length);
    }

    [Fact]
    public void A_long_week_measures_larger_than_a_quiet_one()
    {
        var quiet = Minimal();
        quiet.Days.Add(new SnapshotDay { Date = "2026-08-04" });

        var talkative = Minimal();
        talkative.Days.Add(new SnapshotDay { Date = "2026-08-04", Text = new string('я', 2000) });

        SnapshotSerializer.MeasureCharacters(talkative)
            .Should().BeGreaterThan(SnapshotSerializer.MeasureCharacters(quiet) + 1900);
    }
}
