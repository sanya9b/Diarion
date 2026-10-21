using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Store;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The quick entry writes into today's ordinary row. What these tests guard is that it writes
/// exactly what the day screen would have — the same hour slot, the same field, the same tick —
/// and nothing when nothing was touched, because the sheet is opened idly from a reminder as
/// often as on purpose.
/// </summary>
public class QuickCaptureViewModelTests
{
    private static readonly DateTime Today = new(2026, 10, 14);

    private readonly Mock<IDiaryService> _diary = new();
    private readonly Mock<IDiaryHabitSyncService> _sync = new();
    private readonly Mock<INavigationService> _navigation = new();
    private DiaryEntry _entry = new() { Date = Today };

    public QuickCaptureViewModelTests()
    {
        _diary.Setup(d => d.GetEntryForDateAsync(It.IsAny<DateTime>())).ReturnsAsync(() => _entry);
        _diary.Setup(d => d.SaveEntryAsync(It.IsAny<DiaryEntry>())).Returns(Task.CompletedTask);
        _sync.Setup(s => s.SyncHabitsForEntryAsync(It.IsAny<DiaryEntry>())).Returns(Task.CompletedTask);
    }

    /// <summary>A clock stopped at one local time; the view model reads local time through TimeProvider.</summary>
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTime local) => _now = new DateTimeOffset(local, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private QuickCaptureViewModel Create(DateTime now) =>
        new(_diary.Object, _sync.Object, _navigation.Object, new FixedClock(now));

    private static async Task<QuickCaptureViewModel> LoadedAsync(QuickCaptureViewModel vm)
    {
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task Load_picks_the_current_hour_and_shows_what_it_already_holds()
    {
        _entry.HourlyMood.Add(new HourMood { Hour = 14, Mood = Emotion.Calm });

        var vm = await LoadedAsync(Create(Today.AddHours(14).AddMinutes(20)));

        vm.Date.Should().Be(Today);
        vm.Hour.Should().Be(14);
        vm.SelectedEmotion.Should().Be(Emotion.Calm);
        vm.Emotions.Single(e => e.Emotion == Emotion.Calm).IsSelected.Should().BeTrue();
        vm.CanSave.Should().BeFalse("nothing has changed yet");
        _diary.Verify(d => d.GetEntryForDateAsync(Today), Times.Once);
    }

    [Theory]
    [InlineData(5, 7)]
    [InlineData(7, 7)]
    [InlineData(23, 23)]
    public async Task Load_clamps_the_hour_onto_the_scale(int hourOfDay, int expected)
    {
        var vm = await LoadedAsync(Create(Today.AddHours(hourOfDay).AddMinutes(30)));

        vm.Hour.Should().Be(expected);
    }

    [Fact]
    public async Task Save_writes_the_hour_and_fills_a_blank_day_mood()
    {
        var vm = await LoadedAsync(Create(Today.AddHours(10)));
        var received = new List<DateTime>();
        var token = new object();
        WeakReferenceMessenger.Default.Register<QuickCaptureSavedMessage>(token, (_, m) => received.Add(m.Date));

        try
        {
            vm.SelectEmotionCommand.Execute(Emotion.Happy);
            vm.CanSave.Should().BeTrue();

            await vm.SaveCommand.ExecuteAsync(null);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<QuickCaptureSavedMessage>(token);
        }

        _entry.HourlyMood.Should().ContainSingle(h => h.Hour == 10 && h.Mood == Emotion.Happy);
        _entry.Emotion.Should().Be(Emotion.Happy, "the day had no mood of its own yet");
        _diary.Verify(d => d.SaveEntryAsync(_entry), Times.Once);
        received.Should().Equal(Today);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
    }

    [Fact]
    public async Task Save_leaves_a_day_mood_the_user_already_set_alone()
    {
        _entry.Emotion = Emotion.Sad;
        var vm = await LoadedAsync(Create(Today.AddHours(10)));

        vm.SelectEmotionCommand.Execute(Emotion.Happy);
        await vm.SaveCommand.ExecuteAsync(null);

        _entry.Emotion.Should().Be(Emotion.Sad, "the day-level mood is the user's own summary");
        _entry.HourlyMood.Should().ContainSingle(h => h.Hour == 10 && h.Mood == Emotion.Happy);
    }

    [Fact]
    public async Task Save_replaces_the_hour_rather_than_adding_a_second_reading_for_it()
    {
        _entry.HourlyMood.Add(new HourMood { Hour = 10, Mood = Emotion.Calm });
        var vm = await LoadedAsync(Create(Today.AddHours(10)));

        vm.SelectEmotionCommand.Execute(Emotion.Anxious);
        await vm.SaveCommand.ExecuteAsync(null);

        _entry.HourlyMood.Should().ContainSingle(h => h.Hour == 10).Which.Mood.Should().Be(Emotion.Anxious);
    }

    [Fact]
    public async Task The_fill_follows_the_selection_both_ways()
    {
        // The tiles and chips colour an inner layer from this value rather than through a trigger
        // on the tappable Border, because such a trigger applies and never reverts. So the value
        // itself has to go back to transparent, and say so, when the choice moves on.
        var vm = await LoadedAsync(Create(Today.AddHours(10)));
        var calm = vm.Emotions.Single(e => e.Emotion == Emotion.Calm);
        var happy = vm.Emotions.Single(e => e.Emotion == Emotion.Happy);
        var raised = new List<string>();
        calm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        vm.SelectEmotionCommand.Execute(Emotion.Calm);
        calm.FillColorHex.Should().Be(QuickEmotionChoice.SelectedFill);
        happy.FillColorHex.Should().Be(QuickEmotionChoice.NoFill);

        vm.SelectEmotionCommand.Execute(Emotion.Happy);
        calm.FillColorHex.Should().Be(QuickEmotionChoice.NoFill);
        happy.FillColorHex.Should().Be(QuickEmotionChoice.SelectedFill);
        raised.Should().Contain(nameof(QuickEmotionChoice.FillColorHex));

        var chip = new QuickHabitChip(Guid.NewGuid(), "Вода", wasCompleted: false);
        chip.FillColorHex.Should().Be(QuickEmotionChoice.NoFill);
        chip.IsCompleted = true;
        chip.FillColorHex.Should().Be(QuickEmotionChoice.SelectedFill);
        chip.IsCompleted = false;
        chip.FillColorHex.Should().Be(QuickEmotionChoice.NoFill);
    }

    [Fact]
    public async Task Clearing_the_hours_mood_removes_it()
    {
        _entry.HourlyMood.Add(new HourMood { Hour = 14, Mood = Emotion.Calm });
        var vm = await LoadedAsync(Create(Today.AddHours(14)));

        // Tapping the selected emotion again clears it, the way the day screen's row does.
        vm.SelectEmotionCommand.Execute(Emotion.Calm);
        vm.SelectedEmotion.Should().Be(Emotion.None);
        vm.CanSave.Should().BeTrue("removing a reading is a change too");

        await vm.SaveCommand.ExecuteAsync(null);

        _entry.HourlyMood.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_appends_the_line_to_for_myself_on_its_own_line()
    {
        _entry.SoulFood = "ранкова кава";
        var vm = await LoadedAsync(Create(Today.AddHours(19)));

        vm.Text = "  прогулянка ";
        await vm.SaveCommand.ExecuteAsync(null);

        _entry.SoulFood.Should().Be("ранкова кава" + Environment.NewLine + "прогулянка");
        _entry.HourlyMood.Should().BeEmpty("no mood was chosen, so none is written");
        _entry.Emotion.Should().Be(Emotion.None);
    }

    [Fact]
    public async Task Save_starts_for_myself_when_it_was_blank()
    {
        var vm = await LoadedAsync(Create(Today.AddHours(19)));

        vm.Text = "прогулянка";
        await vm.SaveCommand.ExecuteAsync(null);

        _entry.SoulFood.Should().Be("прогулянка");
    }

    [Fact]
    public async Task Save_ticks_the_habit_that_was_tapped()
    {
        var water = Guid.NewGuid();
        var reading = Guid.NewGuid();
        _entry.HabitsList.Add(new HabitItem { HabitId = water, Name = "Вода", IsCompleted = false });
        _entry.HabitsList.Add(new HabitItem { HabitId = reading, Name = "Читання", IsCompleted = true });
        var vm = await LoadedAsync(Create(Today.AddHours(12)));

        vm.HasHabits.Should().BeTrue();
        vm.Habits.Should().HaveCount(2);
        vm.CanSave.Should().BeFalse();

        vm.ToggleHabitCommand.Execute(vm.Habits.Single(h => h.HabitId == water));
        vm.CanSave.Should().BeTrue();

        await vm.SaveCommand.ExecuteAsync(null);

        _entry.HabitsList.Single(h => h.HabitId == water).IsCompleted.Should().BeTrue();
        _entry.HabitsList.Single(h => h.HabitId == reading).IsCompleted.Should().BeTrue("it was not touched");
        _sync.Verify(s => s.SyncHabitsForEntryAsync(_entry), Times.Once, "the chips are the day's habits, synced the same way the home screen syncs them");
    }

    [Fact]
    public async Task Toggling_a_habit_back_is_no_change_again()
    {
        var water = Guid.NewGuid();
        _entry.HabitsList.Add(new HabitItem { HabitId = water, Name = "Вода", IsCompleted = false });
        var vm = await LoadedAsync(Create(Today.AddHours(12)));
        var chip = vm.Habits.Single();

        vm.ToggleHabitCommand.Execute(chip);
        vm.ToggleHabitCommand.Execute(chip);

        vm.CanSave.Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_changed_means_nothing_saved()
    {
        // Opened from the reminder, looked at, closed: the one case the sheet must never turn into
        // a blank row in the database.
        var vm = await LoadedAsync(Create(Today.AddHours(20)));

        vm.SaveCommand.CanExecute(null).Should().BeFalse();
        await vm.SaveCommand.ExecuteAsync(null);

        _diary.Verify(d => d.SaveEntryAsync(It.IsAny<DiaryEntry>()), Times.Never);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Never);
    }

    [Fact]
    public async Task Whitespace_alone_is_not_a_line()
    {
        var vm = await LoadedAsync(Create(Today.AddHours(20)));

        vm.Text = "   ";

        vm.CanSave.Should().BeFalse();
    }

    [Fact]
    public async Task Read_only_mode_keeps_the_sheet_open_with_everything_still_in_it()
    {
        _diary.Setup(d => d.SaveEntryAsync(It.IsAny<DiaryEntry>())).ThrowsAsync(new ReadOnlyAccessException());
        var vm = await LoadedAsync(Create(Today.AddHours(10)));
        var received = 0;
        var token = new object();
        WeakReferenceMessenger.Default.Register<QuickCaptureSavedMessage>(token, (_, _) => received++);

        try
        {
            vm.SelectEmotionCommand.Execute(Emotion.Happy);
            vm.Text = "рядок";
            await vm.SaveCommand.ExecuteAsync(null);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<QuickCaptureSavedMessage>(token);
        }

        received.Should().Be(0);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Never);
        vm.IsBusy.Should().BeFalse();
        vm.Text.Should().Be("рядок");
        vm.SelectedEmotion.Should().Be(Emotion.Happy);
    }

    [Fact]
    public async Task Cancel_closes_without_writing()
    {
        var vm = await LoadedAsync(Create(Today.AddHours(10)));
        vm.SelectEmotionCommand.Execute(Emotion.Happy);

        await vm.CancelCommand.ExecuteAsync(null);

        _diary.Verify(d => d.SaveEntryAsync(It.IsAny<DiaryEntry>()), Times.Never);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
    }

    [Fact]
    public async Task Open_full_entry_points_the_home_screen_at_today_and_closes()
    {
        var vm = await LoadedAsync(Create(Today.AddHours(10)));
        DateTime? asked = null;
        var token = new object();
        WeakReferenceMessenger.Default.Register<NavigateToDateMessage>(token, (_, m) => asked = m.Date);

        try
        {
            await vm.OpenFullEntryCommand.ExecuteAsync(null);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<NavigateToDateMessage>(token);
        }

        asked.Should().Be(Today);
        _navigation.Verify(n => n.NavigateBackAsync(), Times.Once);
        _diary.Verify(d => d.SaveEntryAsync(It.IsAny<DiaryEntry>()), Times.Never);
    }
}
