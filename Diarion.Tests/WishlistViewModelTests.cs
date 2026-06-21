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

public class WishlistViewModelTests
{
    [Fact]
    public async Task LoadAsync_WithSavedEntries_PopulatesEntries()
    {
        // Arrange
        var diaryServiceMock = new Mock<IWishlistService>();
        diaryServiceMock
            .Setup(s => s.GetWishlistEntriesAsync())
            .ReturnsAsync(new List<WishlistEntry>
            {
                new() { WantText = "New laptop", Date = new DateTime(2025, 6, 1) },
                new() { WishText = "Be happy", Date = new DateTime(2025, 6, 2) }
            });

        var viewModel = new WishlistViewModel(diaryServiceMock.Object);

        // Act
        await viewModel.LoadAsync();

        // Assert
        viewModel.Entries.Should().HaveCount(2);
        viewModel.Entries[0].WantText.Should().Be("New laptop");
        viewModel.Entries[1].WishText.Should().Be("Be happy");
    }

    [Fact]
    public async Task SaveEntryAsync_WithValidData_SavesEntryAndReloads()
    {
        // Arrange
        var storedEntries = new List<WishlistEntry>();
        var diaryServiceMock = new Mock<IWishlistService>();
        diaryServiceMock
            .Setup(s => s.GetWishlistEntriesAsync())
            .ReturnsAsync(() => storedEntries.OrderByDescending(x => x.Date).ToList());

        diaryServiceMock
            .Setup(s => s.SaveWishlistEntryAsync(It.IsAny<WishlistEntry>()))
            .Returns<WishlistEntry>(entry =>
            {
                storedEntries.Add(entry);
                return Task.CompletedTask;
            });

        var viewModel = new WishlistViewModel(diaryServiceMock.Object);
        await viewModel.LoadAsync();

        viewModel.NewWantText = "  A pet dog  ";
        viewModel.NewDate = new DateTime(2025, 5, 20);

        // Act
        await viewModel.SaveEntryCommand.ExecuteAsync(null);

        // Assert
        storedEntries.Should().ContainSingle();
        storedEntries[0].WantText.Should().Be("A pet dog");
        
        viewModel.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveEntryAsync_WithEmptyData_DoesNotSave()
    {
        // Arrange
        var diaryServiceMock = new Mock<IWishlistService>();
        var viewModel = new WishlistViewModel(diaryServiceMock.Object);
        
        viewModel.NewWantText = "  ";

        // Act
        await viewModel.SaveEntryCommand.ExecuteAsync(null);

        // Assert
        diaryServiceMock.Verify(s => s.SaveWishlistEntryAsync(It.IsAny<WishlistEntry>()), Times.Never);
    }
}
