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
        var diaryServiceMock = new Mock<IAuxiliaryService>();
        diaryServiceMock
            .Setup(s => s.GetHappyMomentsAsync())
            .ReturnsAsync(new List<HappyMoment>
            {
                new() { SlotNumber = 1, Title = "Had a great coffee", Date = new DateTime(2025, 5, 10) }
            });

        var viewModel = new HappyMomentsViewModel(diaryServiceMock.Object);

        // Act
        await viewModel.LoadAsync();

        // Assert
        viewModel.MomentSlots.Should().HaveCount(2);
        viewModel.MomentSlots[1].MomentTitle.Should().Be("Had a great coffee");
        viewModel.MomentSlots[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SaveSelectedMomentAsync_WithSelectedEmptySlot_SavesMomentAndReloadsSlots()
    {
        // Arrange
        var storedMoments = new List<HappyMoment>();
        var diaryServiceMock = new Mock<IAuxiliaryService>();
        diaryServiceMock
            .Setup(s => s.GetHappyMomentsAsync())
            .ReturnsAsync(() => storedMoments.OrderByDescending(x => x.Date).ToList());

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
        viewModel.SelectSlotCommand.Execute(viewModel.MomentSlots[0]); // Select empty slot
        viewModel.NewMomentTitle = "Got a promotion";
        viewModel.NewMomentDate = new DateTime(2025, 5, 20);

        // Act
        await viewModel.SaveSelectedMomentCommand.ExecuteAsync(null);

        // Assert
        storedMoments.Should().ContainSingle();
        storedMoments[0].SlotNumber.Should().Be(1);
        storedMoments[0].Title.Should().Be("Got a promotion");
        viewModel.MomentSlots[1].MomentTitle.Should().Be("Got a promotion");
        viewModel.IsAddMomentFormVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSelectedMomentAsync_WithEmptyTitle_SetsValidationMessage()
    {
        // Arrange
        var diaryServiceMock = new Mock<IAuxiliaryService>();
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