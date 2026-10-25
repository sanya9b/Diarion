using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>The "Mood icons" row in settings: one tap stores the set and tells the open day.</summary>
public class MoodIconSetViewModelTests
{
    private readonly Mock<IMoodIconSetService> _service = new();

    [Fact]
    public void StartsOnTheStoredSet()
    {
        _service.Setup(s => s.Current).Returns(MoodIconSet.Weather);

        var viewModel = new MoodIconSetViewModel(_service.Object);

        viewModel.Current.Should().Be(MoodIconSet.Weather);
        viewModel.SelectedIndex.Should().Be(1);
        viewModel.CurrentName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Select_StoresTheSet_AndTellsTheOpenDay()
    {
        var viewModel = new MoodIconSetViewModel(_service.Object);
        MoodIconSet? announced = null;
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<MoodIconSetChangedMessage>(recipient, (_, m) => announced = m.Set);

        try
        {
            viewModel.SelectCommand.Execute(MoodIconSet.Plants);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<MoodIconSetChangedMessage>(recipient);
        }

        _service.Verify(s => s.Set(MoodIconSet.Plants), Times.Once);
        viewModel.Current.Should().Be(MoodIconSet.Plants);
        viewModel.SelectedIndex.Should().Be(2);
        announced.Should().Be(MoodIconSet.Plants);
    }

    [Fact]
    public void Select_TheSetAlreadyInUse_WritesNothing()
    {
        var viewModel = new MoodIconSetViewModel(_service.Object);

        viewModel.SelectCommand.Execute(MoodIconSet.Emoji);

        _service.Verify(s => s.Set(It.IsAny<MoodIconSet>()), Times.Never);
    }

    [Fact]
    public void EachSet_HasItsOwnName()
    {
        var names = new[] { MoodIconSet.Emoji, MoodIconSet.Weather, MoodIconSet.Plants }
            .Select(set =>
            {
                _service.Setup(s => s.Current).Returns(set);
                return new MoodIconSetViewModel(_service.Object).CurrentName;
            })
            .ToList();

        names.Should().OnlyHaveUniqueItems().And.OnlyContain(name => !string.IsNullOrEmpty(name));
    }
}
