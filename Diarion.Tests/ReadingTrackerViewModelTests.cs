using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class ReadingTrackerViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithSavedBooks_PopulatesSlots()
    {
        // Arrange
        var diaryServiceMock = new Mock<IAuxiliaryService>();
        diaryServiceMock
            .Setup(s => s.GetReadingTrackerBooksAsync())
            .ReturnsAsync(new List<ReadingTrackerBook>
            {
                new() { SlotNumber = 1, BookTitle = "1984", CompletedOn = new DateTime(2025, 5, 10) }
            });

        var viewModel = new ReadingTrackerViewModel(diaryServiceMock.Object);

        // Act
        await viewModel.LoadAsync();

        // Assert
        viewModel.Slots.Should().HaveCount(12);
        viewModel.Slots[0].BookTitle.Should().Be("1984");
        viewModel.Slots[1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveBookAsync_WithSelectedEmptySlot_SavesBookAndReloadsSlots()
    {
        // Arrange
        var storedBooks = new List<ReadingTrackerBook>();
        var diaryServiceMock = new Mock<IAuxiliaryService>();
        diaryServiceMock
            .Setup(s => s.GetReadingTrackerBooksAsync())
            .ReturnsAsync(() => storedBooks.OrderBy(x => x.SlotNumber).ToList());

        diaryServiceMock
            .Setup(s => s.SaveReadingTrackerBookAsync(It.IsAny<ReadingTrackerBook>()))
            .Returns<ReadingTrackerBook>(book =>
            {
                storedBooks.RemoveAll(x => x.SlotNumber == book.SlotNumber);
                storedBooks.Add(book);
                return Task.CompletedTask;
            });

        var viewModel = new ReadingTrackerViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]); // Select first slot
        viewModel.NewBookTitle = "Brave New World";
        viewModel.NewCompletionDate = new DateTime(2025, 5, 20);

        // Act
        await viewModel.SaveBookCommand.ExecuteAsync(null);

        // Assert
        storedBooks.Should().ContainSingle();
        storedBooks[0].SlotNumber.Should().Be(1);
        storedBooks[0].BookTitle.Should().Be("Brave New World");
        viewModel.Slots[0].BookTitle.Should().Be("Brave New World");
        viewModel.IsAddBookFormVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SaveBookAsync_WithEmptyTitle_SetsValidationMessage()
    {
        // Arrange
        var diaryServiceMock = new Mock<IAuxiliaryService>();
        diaryServiceMock
            .Setup(s => s.GetReadingTrackerBooksAsync())
            .ReturnsAsync(new List<ReadingTrackerBook>());

        var viewModel = new ReadingTrackerViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]);
        viewModel.NewBookTitle = "  ";

        // Act
        await viewModel.SaveBookCommand.ExecuteAsync(null);

        // Assert
        viewModel.HasValidationMessage.Should().BeTrue();
        diaryServiceMock.Verify(s => s.SaveReadingTrackerBookAsync(It.IsAny<ReadingTrackerBook>()), Times.Never);
    }
}