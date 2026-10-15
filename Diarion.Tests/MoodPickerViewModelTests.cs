using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Diarion.Models;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The two-step picker on <see cref="DiaryEntryViewModel"/>: a quadrant tile first, an optional chip
/// second, both writing the day or the selected hour exactly as the old five-button row did.
/// </summary>
public class MoodPickerViewModelTests
{
    [Fact]
    public void Quadrants_CoverTheFourCellsOfThePlane()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());

        // Energetic above calm, unpleasant left of pleasant.
        viewModel.Quadrants.Select(q => q.Quadrant).Should().Equal(
            EmotionQuadrant.UnpleasantEnergetic, EmotionQuadrant.PleasantEnergetic,
            EmotionQuadrant.UnpleasantCalm, EmotionQuadrant.PleasantCalm);
        viewModel.Quadrants.Select(q => (q.Row, q.Column)).Should().OnlyHaveUniqueItems();
        viewModel.Quadrants.Should().OnlyContain(q => (q.Row == 0 || q.Row == 1) && (q.Column == 0 || q.Column == 1));
        viewModel.Quadrants.Should().OnlyContain(q => !q.IsSelected);
        viewModel.HasRefinements.Should().BeFalse();
    }

    [Fact]
    public void SelectQuadrant_WithNoHourSelected_RecordsTheRepresentativeAndOffersItsChips()
    {
        var model = new DiaryEntry();
        var viewModel = new DiaryEntryViewModel(model);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);

        viewModel.Emotion.Should().Be(Emotion.Anxious);
        model.Emotion.Should().Be(Emotion.Anxious);
        viewModel.CurrentQuadrant.Should().Be(EmotionQuadrant.UnpleasantEnergetic);
        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantEnergetic);
        viewModel.HasRefinements.Should().BeTrue();
        viewModel.Refinements.Select(c => c.Emotion).Should().Equal(EmotionCatalog.Emotions(EmotionQuadrant.UnpleasantEnergetic));
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Anxious);
    }

    [Fact]
    public void SelectQuadrant_TheChosenQuadrantAgain_ClearsTheDay()
    {
        var model = new DiaryEntry { Emotion = Emotion.Stressed };
        var viewModel = new DiaryEntryViewModel(model);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);

        viewModel.Emotion.Should().Be(Emotion.None, "the second tap on the same tile is the only way to un-log a day");
        model.Emotion.Should().Be(Emotion.None);
        viewModel.Quadrants.Should().OnlyContain(q => !q.IsSelected);
        viewModel.HasRefinements.Should().BeFalse();
        viewModel.Refinements.Should().BeEmpty();
    }

    [Fact]
    public void SelectEmotion_AChipInsideTheQuadrant_RefinesWithoutRebuildingTheRow()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);
        var chips = viewModel.Refinements.ToList();

        viewModel.SelectEmotionCommand.Execute(Emotion.Stressed);

        viewModel.Emotion.Should().Be(Emotion.Stressed);
        viewModel.Refinements.Should().Equal(chips, "the same chip objects stay on screen");
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Stressed);
        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantEnergetic);
    }

    [Fact]
    public void SelectQuadrant_AnotherQuadrant_SwapsTheChips()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantCalm);

        viewModel.Emotion.Should().Be(Emotion.Calm);
        viewModel.Refinements.Select(c => c.Emotion).Should().Equal(EmotionCatalog.Emotions(EmotionQuadrant.PleasantCalm));
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Calm);
    }

    [Fact]
    public void SelectQuadrant_Neutral_RecordsNeutralAndOffersNoChips()
    {
        var model = new DiaryEntry { Emotion = Emotion.Happy };
        var viewModel = new DiaryEntryViewModel(model);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.Neutral);

        viewModel.Emotion.Should().Be(Emotion.Neutral);
        model.Emotion.Should().Be(Emotion.Neutral);
        viewModel.IsNeutralSelected.Should().BeTrue();
        viewModel.Quadrants.Should().OnlyContain(q => !q.IsSelected);
        viewModel.HasRefinements.Should().BeFalse();

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.Neutral);

        viewModel.Emotion.Should().Be(Emotion.None, "Neutral toggles off like any other tile");
        viewModel.IsNeutralSelected.Should().BeFalse();
    }

    [Fact]
    public void SelectQuadrant_WithAnHourSelected_WritesThatHour_AndAgainClearsIt()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Calm });
        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 14));

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantCalm);

        viewModel.HourlyMood.Single(h => h.Hour == 14).Mood.Should().Be(Emotion.Sad);
        viewModel.Emotion.Should().Be(Emotion.Calm, "the day-level summary stays the user's own");
        viewModel.Model.HourlyMood.Should().ContainSingle(h => h.Hour == 14 && h.Mood == Emotion.Sad);
        viewModel.CurrentQuadrant.Should().Be(EmotionQuadrant.UnpleasantCalm);
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Sad);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantCalm);

        viewModel.HourlyMood.Single(h => h.Hour == 14).Mood.Should().Be(Emotion.None);
        viewModel.Model.HourlyMood.Should().BeEmpty();
        viewModel.HasRefinements.Should().BeFalse();
        viewModel.Emotion.Should().Be(Emotion.Calm);
    }

    [Fact]
    public void SelectEmotion_TheSameChipTwiceOnAnHour_KeepsIt()
    {
        // Clearing moved to the tile; a chip only ever sets. Otherwise the pre-selected representative
        // chip would clear the hour the moment someone tapped it to confirm.
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 9));

        viewModel.SelectEmotionCommand.Execute(Emotion.Angry);
        viewModel.SelectEmotionCommand.Execute(Emotion.Angry);

        viewModel.HourlyMood.Single(h => h.Hour == 9).Mood.Should().Be(Emotion.Angry);
    }

    [Fact]
    public void Constructor_WithAMoodAlreadyLogged_PreselectsItsQuadrantAndChip()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Tired });

        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantCalm);
        viewModel.HasRefinements.Should().BeTrue();
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Tired);
    }

    [Fact]
    public void SelectHour_MovesTheTilesAndChipsToThatHoursMood_AndBack()
    {
        var model = new DiaryEntry
        {
            Emotion = Emotion.Happy,
            HourlyMood = { new HourMood { Hour = 10, Mood = Emotion.Lonely } }
        };
        var viewModel = new DiaryEntryViewModel(model);
        var slot = viewModel.HourlyMood.Single(h => h.Hour == 10);

        viewModel.SelectHourCommand.Execute(slot);

        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantCalm);
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Lonely);

        viewModel.SelectHourCommand.Execute(slot);

        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.PleasantEnergetic);
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Happy);
    }

    [Fact]
    public void HasRefinements_IsRaisedWhenTheQuadrantChanges_NotWhenAChipIsPicked()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantEnergetic);
        raised.Count(p => p == nameof(DiaryEntryViewModel.HasRefinements)).Should().Be(1);

        viewModel.SelectEmotionCommand.Execute(Emotion.Proud);
        raised.Count(p => p == nameof(DiaryEntryViewModel.HasRefinements)).Should().Be(1,
            "the row did not come or go, so the view has nothing to animate");
        raised.Should().Contain(nameof(DiaryEntryViewModel.CurrentQuadrant));
    }

    [Fact]
    public void FillColours_FollowSelection_SoTheViewNeedsNoTriggers()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantCalm);
        viewModel.SelectEmotionCommand.Execute(Emotion.Grateful);

        var chosen = viewModel.Quadrants.Single(q => q.Quadrant == EmotionQuadrant.PleasantCalm);
        chosen.FillColor.Should().Be(chosen.Color);
        chosen.DotOpacity.Should().Be(0);
        viewModel.Quadrants.Where(q => q != chosen).Should().OnlyContain(q =>
            q.FillColor == Microsoft.Maui.Graphics.Colors.Transparent && q.DotOpacity == 1);

        viewModel.Refinements.Single(c => c.Emotion == Emotion.Grateful).FillColor.Should().Be(chosen.Color);
        viewModel.Refinements.Where(c => c.Emotion != Emotion.Grateful).Should().OnlyContain(c =>
            c.FillColor == Microsoft.Maui.Graphics.Colors.Transparent);
        viewModel.NeutralFillColor.Should().Be(Microsoft.Maui.Graphics.Colors.Transparent);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.Neutral);

        viewModel.NeutralFillColor.Should().NotBe(Microsoft.Maui.Graphics.Colors.Transparent);
        chosen.FillColor.Should().Be(Microsoft.Maui.Graphics.Colors.Transparent);
    }

    [Fact]
    public void HourSlot_TakesTheColourOfItsQuadrant()
    {
        new HourMoodViewModel(9, Emotion.Stressed).ColorHex.Should().Be(EmotionQuadrant.UnpleasantEnergetic.ToColorHex());
        new HourMoodViewModel(9, Emotion.Neutral).ColorHex.Should().Be(EmotionQuadrant.Neutral.ToColorHex());
        new HourMoodViewModel(9, Emotion.None).ColorHex.Should().Be("#00000000");
    }
}
