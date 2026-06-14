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

public class HappyMomentsViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithSavedMoments_PopulatesSlots()
    {
        // Arrange
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetHappyMomentsAsync())
            .ReturnsAsync(new List<HappyMoment>
            {
                new() { SlotNumber = 1, Title = "A great day", Date = new DateTime(2025, 5, 10) }
            });

        var viewModel = new HappyMomentsViewModel(diaryServiceMock.Object);

        // Act
        await viewModel.LoadAsync();

        // Assert
        viewModel.MomentSlots.Should().HaveCount(12);
        viewModel.MomentSlots[0].MomentTitle.Should().Be("A great day");
        viewModel.MomentSlots[1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveSelectedMomentAsync_WithSelectedEmptySlot_SavesMomentAndReloadsSlots()
    {
        // Arrange
        var storedMoments = new List<HappyMoment>();
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetHappyMomentsAsync())
            .ReturnsAsync(() => storedMoments.OrderBy(x => x.SlotNumber).ToList());

        diaryServiceMock
            .Setup(s => s.SaveHappyMomentAsync(It.IsAny<HappyMoment>()))
            .Returns<HappyMoment>(moment =>
            {
                storedMoments.RemoveAll(x => x.SlotNumber == moment.SlotNumber);
                storedMoments.Add(moment);
                return Task.CompletedTask;
            });

        var viewModel = new HappyMomentsViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.MomentSlots[0]);
        viewModel.NewMomentTitle = "A trip to the mountains";
        viewModel.NewMomentDate = new DateTime(2025, 5, 20);

        // Act
        await viewModel.SaveSelectedMomentCommand.ExecuteAsync(null);

        // Assert
        storedMoments.Should().ContainSingle();
        storedMoments[0].SlotNumber.Should().Be(1);
        storedMoments[0].Title.Should().Be("A trip to the mountains");
        viewModel.MomentSlots[0].MomentTitle.Should().Be("A trip to the mountains");
        viewModel.IsAddMomentFormVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSelectedMomentAsync_WithEmptyTitle_SetsValidationMessage()
    {
        // Arrange
        var diaryServiceMock = new Mock<IDiaryService>();
        diaryServiceMock
            .Setup(s => s.GetHappyMomentsAsync())
            .ReturnsAsync(new List<HappyMoment>());

        var viewModel = new HappyMomentsViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();
        viewModel.SelectSlotCommand.Execute(viewModel.MomentSlots[0]);
        viewModel.NewMomentTitle = "  ";

        // Act
        await viewModel.SaveSelectedMomentCommand.ExecuteAsync(null);

        // Assert
        viewModel.HasValidationMessage.Should().BeTrue();
        diaryServiceMock.Verify(s => s.SaveHappyMomentAsync(It.IsAny<HappyMoment>()), Times.Never);
    }
}