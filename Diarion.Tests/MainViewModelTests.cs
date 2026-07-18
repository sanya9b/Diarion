using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Core.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class MainViewModelTests
{
    private readonly Mock<IDiaryService> _diaryServiceMock;
    private readonly Mock<ITodoService> _todoServiceMock;
    private readonly Mock<IHabitService> _habitServiceMock;
    private readonly Mock<IProfileService> _profileServiceMock;
    private readonly Mock<IMenstrualCycleService> _menstrualCycleServiceMock;
    private readonly Mock<INavigationService> _navigationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<ICalendarService> _calendarServiceMock;
    private readonly Mock<IMenuConfigurationService> _menuConfigServiceMock;

    private readonly CalendarSectionViewModel _calendarSection;
    private readonly PlannerSectionViewModel _plannerSection;
    private readonly QuickMenuViewModel _quickMenuSection;
    private readonly HabitsSectionViewModel _habitsSection;

    public MainViewModelTests()
    {
        _diaryServiceMock = new Mock<IDiaryService>();
        _todoServiceMock = new Mock<ITodoService>();
        _habitServiceMock = new Mock<IHabitService>();
        _profileServiceMock = new Mock<IProfileService>();
        _menstrualCycleServiceMock = new Mock<IMenstrualCycleService>();
        _navigationServiceMock = new Mock<INavigationService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _calendarServiceMock = new Mock<ICalendarService>();
        _menuConfigServiceMock = new Mock<IMenuConfigurationService>();

        _todoServiceMock
            .Setup(s => s.GetTodosForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new List<TodoItem>());

        _profileServiceMock
            .Setup(s => s.GetUserProfileAsync())
            .ReturnsAsync(new UserProfile());

        _diaryServiceMock
            .Setup(s => s.GetEntryForDateAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(new DiaryEntry());

        _menstrualCycleServiceMock
            .Setup(s => s.GetCycleInfoForDate(It.IsAny<DateTime>(), It.IsAny<UserProfile>()))
            .Returns(new CycleDayInfo());

        _calendarServiceMock
            .Setup(s => s.GenerateCalendarDays(It.IsAny<DateTime>()))
            .Returns(new List<CalendarDay>());

        _menuConfigServiceMock
            .Setup(s => s.GetDefaultMenuItems())
            .Returns(new List<QuickMenuItem>());

        _calendarSection = new CalendarSectionViewModel(
            _calendarServiceMock.Object,
            _menstrualCycleServiceMock.Object,
            _profileServiceMock.Object,
            _todoServiceMock.Object);

        _plannerSection = new PlannerSectionViewModel(
            _todoServiceMock.Object,
            _navigationServiceMock.Object);

        _quickMenuSection = new QuickMenuViewModel(
            _menuConfigServiceMock.Object,
            _profileServiceMock.Object,
            _navigationServiceMock.Object);

        _habitsSection = new HabitsSectionViewModel(
            _habitServiceMock.Object,
            _dialogServiceMock.Object,
            _calendarSection);
    }

    private MainViewModel CreateViewModel()
    {
        return new MainViewModel(
            _diaryServiceMock.Object,
            new Mock<IDiaryHabitSyncService>().Object,
            _navigationServiceMock.Object,
            _dialogServiceMock.Object,
            new Mock<IHealthDataService>().Object,
            _profileServiceMock.Object,
            _calendarSection,
            _plannerSection,
            _quickMenuSection,
            _habitsSection,
            new Mock<CycleStatusViewModel>(_menstrualCycleServiceMock.Object, _profileServiceMock.Object).Object);
    }

    [Fact]
    public void SwitchModes_ChangesPropertiesCorrectly()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act - Switch to Planner
        viewModel.SwitchToPlannerModeCommand.Execute(null);

        // Assert
        viewModel.IsPlannerMode.Should().BeTrue();
        viewModel.IsDiaryMode.Should().BeFalse();

        // Act - Switch back to Diary
        viewModel.SwitchToDiaryModeCommand.Execute(null);

        // Assert
        viewModel.IsPlannerMode.Should().BeFalse();
        viewModel.IsDiaryMode.Should().BeTrue();
    }

    [Fact]
    public async Task FlushAutoSaveAsync_PersistsPendingEditsOfCurrentEntry()
    {
        // Arrange
        var saved = new List<DiaryEntry>();
        _diaryServiceMock
            .Setup(s => s.SaveEntryAsync(It.IsAny<DiaryEntry>()))
            .Callback<DiaryEntry>(e => saved.Add(e))
            .Returns(Task.CompletedTask);

        var viewModel = CreateViewModel();
        var entry = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 1, 10) });
        viewModel.CurrentEntry = entry;

        // Act - editing the entry schedules a debounced autosave; flush persists it immediately.
        entry.SleepQuality = 7;
        await viewModel.FlushAutoSaveAsync();

        // Assert
        saved.Should().ContainSingle();
        saved[0].SleepQuality.Should().Be(7);
    }

    [Fact]
    public async Task PendingSave_TargetsTheEditedEntry_NotTheEntrySwitchedToAfterwards()
    {
        // Regression for the autosave data-loss bug: editing day A then switching to day B
        // within the debounce window must still persist day A's edits (the debounced save
        // captured entry A), not silently re-save day B.
        var saved = new List<DiaryEntry>();
        _diaryServiceMock
            .Setup(s => s.SaveEntryAsync(It.IsAny<DiaryEntry>()))
            .Callback<DiaryEntry>(e => saved.Add(e))
            .Returns(Task.CompletedTask);

        var viewModel = CreateViewModel();
        var entryA = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 1, 10) });
        var entryB = new DiaryEntryViewModel(new DiaryEntry { Date = new DateTime(2026, 1, 11) });

        // Open day A and edit it -> schedules a debounced save bound to entry A.
        viewModel.CurrentEntry = entryA;
        entryA.SleepQuality = 5;

        // Switch the current entry to day B before the debounce fires.
        viewModel.CurrentEntry = entryB;

        // Flush now: the pending save must persist day A's edit, not day B.
        await viewModel.FlushAutoSaveAsync();

        // Assert
        saved.Should().ContainSingle();
        saved[0].Date.Date.Should().Be(new DateTime(2026, 1, 10));
        saved[0].SleepQuality.Should().Be(5);
    }
}