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

public class GoodDeedsViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithSavedDeeds_PopulatesSlots()
    {
        // Arrange
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetGoodDeedsAsync())
            .ReturnsAsync(new List<GoodDeed>
            {
                new() { SlotNumber = 1, Title = "Helped a neighbor", Date = new DateTime(2025, 5, 10) }
            });

        var viewModel = new GoodDeedsViewModel(diaryServiceMock.Object);

        // Act
        await viewModel.LoadAsync();

        // Assert
        viewModel.DeedSlots.Should().HaveCount(2);
        viewModel.DeedSlots[1].DeedTitle.Should().Be("Helped a neighbor");
        viewModel.DeedSlots[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveSelectedDeedAsync_WithSelectedEmptySlot_SavesDeedAndReloadsSlots()
    {
        // Arrange
        var storedDeeds = new List<GoodDeed>();
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetGoodDeedsAsync())
            .ReturnsAsync(() => storedDeeds.OrderByDescending(x => x.Date).ToList());

        diaryServiceMock
            .Setup(s => s.SaveGoodDeedAsync(It.IsAny<GoodDeed>()))
            .Returns<GoodDeed>(deed =>
            {
                storedDeeds.RemoveAll(x => x.SlotNumber == deed.SlotNumber);
                storedDeeds.Add(deed);
                return Task.CompletedTask;
            });

        var viewModel = new GoodDeedsViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.DeedSlots[0]); // Select empty slot
        viewModel.NewDeedTitle = "Donated to charity";
        viewModel.NewDeedDate = new DateTime(2025, 5, 20);

        // Act
        await viewModel.SaveSelectedDeedCommand.ExecuteAsync(null);

        // Assert
        storedDeeds.Should().ContainSingle();
        storedDeeds[0].SlotNumber.Should().Be(1);
        storedDeeds[0].Title.Should().Be("Donated to charity");
        viewModel.DeedSlots[1].DeedTitle.Should().Be("Donated to charity");
        viewModel.IsAddDeedFormVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSelectedDeedAsync_WithEmptyTitle_SetsValidationMessage()
    {
        // Arrange
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetGoodDeedsAsync())
            .ReturnsAsync(new List<GoodDeed>());

        var viewModel = new GoodDeedsViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.DeedSlots[0]);
        viewModel.NewDeedTitle = "  ";

        // Act
        await viewModel.SaveSelectedDeedCommand.ExecuteAsync(null);

        // Assert
        viewModel.HasValidationMessage.Should().BeTrue();
        diaryServiceMock.Verify(s => s.SaveGoodDeedAsync(It.IsAny<GoodDeed>()), Times.Never);
    }
}