using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Widgets;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Database;
using Diarion.Services.Widgets;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The builder behind the home-screen widgets (spec 20, R2). The privacy tests are the point: a
/// snapshot at the two lower levels must not carry a word the user typed, whatever is in the database.
/// </summary>
public class WidgetSnapshotServiceTests : IDisposable
{
    private const string TaskMarker = "MARKER_TASK_dentist";
    private const string HabitMarker = "MARKER_HABIT_water";

    private readonly DatabaseContext _db;
    private readonly DiaryService _diary;
    private readonly TodoService _todos;
    private readonly HabitService _habits;
    private readonly ProfileService _profiles;
    private readonly WidgetSnapshotService _service;
    private readonly DateTime _today = DateTime.Today;

    public WidgetSnapshotServiceTests()
    {
        _db = new DatabaseContext(useInMemory: true);
        _profiles = new ProfileService(_db);
        _todos = new TodoService(_db, null);
        _habits = new HabitService(_db);
        _diary = new DiaryService(_db, _todos, _profiles);
        _service = new WidgetSnapshotService(_diary, _todos, _habits);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedDayAsync()
    {
        var habitId = Guid.NewGuid();
        await _habits.AddHabitDefinitionAsync(new HabitDefinition
        {
            Id = habitId,
            Name = HabitMarker,
            CreatedAt = _today.AddDays(-10)
        });
        var otherHabit = Guid.NewGuid();
        await _habits.AddHabitDefinitionAsync(new HabitDefinition
        {
            Id = otherHabit,
            Name = "second",
            CreatedAt = _today.AddDays(-10)
        });

        var entry = await _diary.GetEntryForDateAsync(_today);
        entry.Content = "Something written today";
        entry.Emotion = Emotion.Happy;
        entry.HabitsList.Add(new HabitItem { HabitId = habitId, Name = HabitMarker, IsCompleted = true });
        entry.HabitsList.Add(new HabitItem { HabitId = otherHabit, Name = "second", IsCompleted = false });
        await _diary.SaveEntryAsync(entry);

        await _todos.SaveTodoAsync(new TodoItem
        {
            TargetDate = _today,
            TaskDescription = TaskMarker,
            HasTime = true,
            TargetTime = new TimeSpan(14, 0, 0),
            Priority = TodoPriority.High
        });
        await _todos.SaveTodoAsync(new TodoItem
        {
            TargetDate = _today,
            TaskDescription = "done one",
            IsCompleted = true
        });
    }

    [Fact]
    public async Task ButtonOnly_carries_labels_and_nothing_else()
    {
        await SeedDayAsync();

        var snapshot = await _service.BuildAsync(_today, WidgetPrivacyLevel.ButtonOnly);

        snapshot.PrivacyLevel.Should().Be(WidgetPrivacyLevel.ButtonOnly);
        snapshot.Date.Should().Be(_today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        snapshot.Labels.Open.Should().NotBeNullOrWhiteSpace();
        snapshot.Streak.Should().BeNull();
        snapshot.Mood.Should().BeNull();
        snapshot.Tasks.Should().BeNull();
        snapshot.Habits.Should().BeNull();
    }

    [Fact]
    public async Task Counts_has_numbers_and_glyph_but_no_items()
    {
        await SeedDayAsync();

        var snapshot = await _service.BuildAsync(_today, WidgetPrivacyLevel.Counts);

        snapshot.Streak!.Days.Should().Be(1);
        snapshot.Mood!.Quadrant.Should().Be(MoodAggregate.DominantQuadrant(Emotion.Happy, null).ToString());
        snapshot.Tasks!.Total.Should().Be(2);
        snapshot.Tasks.Done.Should().Be(1);
        snapshot.Tasks.Items.Should().BeNull();
        snapshot.Habits!.Total.Should().Be(2);
        snapshot.Habits.Done.Should().Be(1);
        snapshot.Habits.Items.Should().BeNull();
    }

    [Fact]
    public async Task Lower_levels_never_contain_user_text()
    {
        // AC1: the file on disk is what matters, so the check runs over the serialized form.
        await SeedDayAsync();

        foreach (var level in new[] { WidgetPrivacyLevel.ButtonOnly, WidgetPrivacyLevel.Counts })
        {
            var json = WidgetSnapshotJson.Serialize(await _service.BuildAsync(_today, level));
            json.Should().NotContain(TaskMarker, $"level {level} must not leak task text");
            json.Should().NotContain(HabitMarker, $"level {level} must not leak habit names");
            json.Should().NotContain("Something written", $"level {level} must never carry diary text");
        }
    }

    [Fact]
    public async Task WithText_lists_tasks_open_first_and_habits_in_order()
    {
        await SeedDayAsync();

        var snapshot = await _service.BuildAsync(_today, WidgetPrivacyLevel.WithText);

        snapshot.Tasks!.Items.Should().HaveCount(2);
        snapshot.Tasks.Items![0].Title.Should().Be(TaskMarker);
        snapshot.Tasks.Items[0].Done.Should().BeFalse();
        snapshot.Tasks.Items[0].Time.Should().NotBeNull();
        snapshot.Tasks.Items[0].Priority.Should().Be("High");
        snapshot.Tasks.Items[1].Done.Should().BeTrue();
        snapshot.Tasks.Items[1].Time.Should().BeNull();

        snapshot.Habits!.Items.Should().HaveCount(2);
        snapshot.Habits.Items!.Should().ContainSingle(h => h.Name == HabitMarker && h.Done);

        var json = WidgetSnapshotJson.Serialize(snapshot);
        json.Should().NotContain("Something written", "diary text is never in the snapshot, at any level");
    }

    [Fact]
    public async Task WithText_caps_the_lists()
    {
        for (var i = 0; i < WidgetSnapshotService.MaxTaskItems + 3; i++)
        {
            await _todos.SaveTodoAsync(new TodoItem { TargetDate = _today, TaskDescription = $"task {i}" });
        }

        var snapshot = await _service.BuildAsync(_today, WidgetPrivacyLevel.WithText);

        snapshot.Tasks!.Total.Should().Be(WidgetSnapshotService.MaxTaskItems + 3);
        snapshot.Tasks.Items.Should().HaveCount(WidgetSnapshotService.MaxTaskItems);
    }

    [Fact]
    public async Task Empty_day_says_so_instead_of_zero_of_zero()
    {
        var snapshot = await _service.BuildAsync(_today, WidgetPrivacyLevel.Counts);

        snapshot.Streak!.Days.Should().Be(0);
        snapshot.Streak.Label.Should().Be(AppResources.WidgetStreakNone);
        snapshot.Mood!.Quadrant.Should().BeNull();
        snapshot.Mood.Label.Should().Be(AppResources.WidgetMoodEmpty);
        snapshot.Tasks!.Label.Should().Be(AppResources.WidgetTasksNone);
        snapshot.Habits!.Label.Should().Be(AppResources.WidgetHabitsNone);
    }

    [Theory]
    [InlineData(1, "WidgetStreakDayOne")]
    [InlineData(3, "WidgetStreakDayFew")]
    [InlineData(5, "WidgetStreakDayMany")]
    [InlineData(11, "WidgetStreakDayMany")]
    [InlineData(21, "WidgetStreakDayOne")]
    [InlineData(22, "WidgetStreakDayFew")]
    public async Task Streak_label_picks_the_plural_form(int days, string expectedKey)
    {
        var previous = AppResources.Culture;
        AppResources.Culture = new CultureInfo("uk");
        try
        {
            var profile = await _profiles.GetUserProfileAsync();
            profile.IsForgivingStreaksEnabled = false;
            await _profiles.SaveUserProfileAsync(profile);

            for (var i = 0; i < days; i++)
            {
                var entry = await _diary.GetEntryForDateAsync(_today.AddDays(-i));
                entry.Content = "written";
                await _diary.SaveEntryAsync(entry);
            }

            var snapshot = await _service.BuildAsync(_today, WidgetPrivacyLevel.Counts);

            var expected = string.Format(AppResources.ResourceManager.GetString(expectedKey, AppResources.Culture)!, days);
            snapshot.Streak!.Days.Should().Be(days);
            snapshot.Streak.Label.Should().Be(expected);
        }
        finally
        {
            AppResources.Culture = previous;
        }
    }

    [Fact]
    public async Task Labels_follow_the_resource_culture()
    {
        var previous = AppResources.Culture;
        try
        {
            AppResources.Culture = new CultureInfo("uk");
            var uk = await _service.BuildAsync(_today, WidgetPrivacyLevel.ButtonOnly);
            AppResources.Culture = new CultureInfo("en");
            var en = await _service.BuildAsync(_today, WidgetPrivacyLevel.ButtonOnly);

            uk.Locale.Should().Be("uk");
            en.Locale.Should().Be("en");
            uk.Labels.Open.Should().NotBe(en.Labels.Open);
        }
        finally
        {
            AppResources.Culture = previous;
        }
    }

    private sealed class PinkThemes : IWidgetThemeProvider
    {
        public WidgetTheme? Current() => new()
        {
            Name = "Pink", Background = "#FFF7F9", Text = "#5A2C3A", Secondary = "#A66B81", Track = "#ffd5d5",
            Accent = "#E05A88", Sage = "#FFB6C1", Amber = "#F080A8", Ocean = "#ff92b0"
        };
    }

    [Fact]
    public async Task Theme_rides_along_at_every_level_and_is_absent_without_a_provider()
    {
        var themed = new WidgetSnapshotService(_diary, _todos, _habits, new PinkThemes());

        var buttonOnly = await themed.BuildAsync(_today, WidgetPrivacyLevel.ButtonOnly);
        var counts = await themed.BuildAsync(_today, WidgetPrivacyLevel.Counts);

        buttonOnly.Theme!.Name.Should().Be("Pink", "a button-only widget is still the app's colour");
        counts.Theme!.Accent.Should().Be("#E05A88");
        WidgetSnapshotJson.Serialize(counts).Should().Contain("\"theme\":{\"name\":\"Pink\"");

        (await _service.BuildAsync(_today, WidgetPrivacyLevel.Counts)).Theme.Should().BeNull();
    }
}
