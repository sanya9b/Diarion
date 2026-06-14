using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class ReadingTrackerViewModelTests
{
    [Fact]
    public async Task SelectSlotCommand_WithEmptySlot_ShowsAddBookForm()
    {
        // Arrange
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(x => x.GetReadingTrackerBooksAsync())
            .ReturnsAsync(new List<ReadingTrackerBook>());

        var viewModel = new ReadingTrackerViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();

        // Act
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]);

        // Assert
        viewModel.IsAddBookFormVisible.Should().BeTrue();
        viewModel.SelectedSlotTitle.Should().Be(string.Format(CultureInfo.CurrentCulture, AppResources.ReadingTrackerSlotFormat, 1));
    }

    [Fact]
    public async Task SaveBookAsync_WithSelectedEmptySlot_PersistsBookAndRefreshesSlots()
    {
        // Arrange
        var storedBooks = new List<ReadingTrackerBook>();
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(x => x.GetReadingTrackerBooksAsync())
            .ReturnsAsync(() => storedBooks.OrderBy(x => x.SlotNumber).ToList());
        diaryServiceMock
            .Setup(x => x.SaveReadingTrackerBookAsync(It.IsAny<ReadingTrackerBook>()))
            .Returns<ReadingTrackerBook>(book =>
            {
                storedBooks.Add(book);
                return Task.CompletedTask;
            });

        var viewModel = new ReadingTrackerViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]);
        viewModel.NewBookTitle = "Atomic Habits";
        viewModel.NewCompletionDate = new DateTime(2026, 6, 3);

        // Act
        await viewModel.SaveBookCommand.ExecuteAsync(null);

        // Assert
        storedBooks.Should().ContainSingle();
        viewModel.Slots[0].IsFilled.Should().BeTrue();
        viewModel.Slots[0].BookTitle.Should().Be("Atomic Habits");
        viewModel.IsAddBookFormVisible.Should().BeFalse();
    }
}