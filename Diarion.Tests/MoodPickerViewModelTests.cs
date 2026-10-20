using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The mood row on <see cref="DiaryEntryViewModel"/>: five faces, a bubble of precise states under the
/// chosen one, both writing the day or the selected hour exactly as the old five-button row did.
/// </summary>
public class MoodPickerViewModelTests
{
    [Fact]
    public void Faces_RunFromUnpleasantToPleasant_WithNeutralInTheMiddle()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());

        viewModel.Quadrants.Select(q => q.Quadrant).Should().Equal(
            EmotionQuadrant.UnpleasantCalm, EmotionQuadrant.UnpleasantEnergetic, EmotionQuadrant.Neutral,
            EmotionQuadrant.PleasantCalm, EmotionQuadrant.PleasantEnergetic);
        viewModel.Quadrants.Select(q => q.Column).Should().Equal(0, 1, 2, 3, 4);
        viewModel.Quadrants.Should().OnlyContain(q => !q.IsSelected);
        viewModel.HasRefinements.Should().BeFalse();
        viewModel.IsRefinementOpen.Should().BeFalse();
        viewModel.HasCurrentMood.Should().BeFalse();
        viewModel.CurrentMoodName.Should().BeEmpty();
        viewModel.SelectedColumn.Should().Be(2, "with nothing chosen the tail would point at the middle");
    }

    [Fact]
    public void SelectQuadrant_RecordsTheRepresentative_OpensTheBubble_AndNamesTheState()
    {
        var model = new DiaryEntry();
        var viewModel = new DiaryEntryViewModel(model);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);

        viewModel.Emotion.Should().Be(Emotion.Anxious);
        model.Emotion.Should().Be(Emotion.Anxious);
        viewModel.CurrentQuadrant.Should().Be(EmotionQuadrant.UnpleasantEnergetic);
        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantEnergetic);
        viewModel.SelectedColumn.Should().Be(1);
        viewModel.CurrentMoodName.Should().Be(Emotion.Anxious.ToLocalizedName());
        viewModel.IsRefinementOpen.Should().BeTrue();
        viewModel.Refinements.Select(c => c.Emotion).Should().Equal(EmotionCatalog.Emotions(EmotionQuadrant.UnpleasantEnergetic));
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Anxious);
    }

    [Fact]
    public void SelectQuadrant_TheChosenFaceAgain_TogglesTheBubble_NotTheMood()
    {
        var model = new DiaryEntry { Emotion = Emotion.Stressed };
        var viewModel = new DiaryEntryViewModel(model);
        viewModel.IsRefinementOpen.Should().BeFalse("a day that arrives logged does not open the bubble by itself");

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);
        viewModel.IsRefinementOpen.Should().BeTrue();
        viewModel.Emotion.Should().Be(Emotion.Stressed, "re-tapping the face must not downgrade a refined state");

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);
        viewModel.IsRefinementOpen.Should().BeFalse();
        viewModel.Emotion.Should().Be(Emotion.Stressed);
        model.Emotion.Should().Be(Emotion.Stressed);
    }

    [Fact]
    public void SelectEmotion_AChipInTheBubble_RefinesAndClosesIt_WithoutRebuildingTheChips()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);
        var chips = viewModel.Refinements.ToList();

        viewModel.SelectEmotionCommand.Execute(Emotion.Stressed);

        viewModel.Emotion.Should().Be(Emotion.Stressed);
        viewModel.CurrentMoodName.Should().Be(Emotion.Stressed.ToLocalizedName());
        viewModel.IsRefinementOpen.Should().BeFalse();
        viewModel.Refinements.Should().Equal(chips, "the same chip objects stay");
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Stressed);
        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantEnergetic);
    }

    [Fact]
    public void SelectQuadrant_AnotherFaceWhileOpen_SwapsTheChips_AndKeepsTheBubbleOut()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantCalm);

        viewModel.Emotion.Should().Be(Emotion.Calm);
        viewModel.IsRefinementOpen.Should().BeTrue();
        viewModel.SelectedColumn.Should().Be(3);
        viewModel.Refinements.Select(c => c.Emotion).Should().Equal(EmotionCatalog.Emotions(EmotionQuadrant.PleasantCalm));
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Calm);
    }

    [Fact]
    public void SelectQuadrant_Neutral_RecordsNeutral_AndHasNoBubble()
    {
        var model = new DiaryEntry { Emotion = Emotion.Happy };
        var viewModel = new DiaryEntryViewModel(model);
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantEnergetic);
        viewModel.IsRefinementOpen.Should().BeTrue();

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.Neutral);

        viewModel.Emotion.Should().Be(Emotion.Neutral);
        model.Emotion.Should().Be(Emotion.Neutral);
        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.Neutral);
        viewModel.SelectedColumn.Should().Be(2);
        viewModel.HasRefinements.Should().BeFalse();
        viewModel.IsRefinementOpen.Should().BeFalse("there is nothing to refine");

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.Neutral);
        viewModel.Emotion.Should().Be(Emotion.Neutral, "a face never clears");
        viewModel.IsRefinementOpen.Should().BeFalse();
    }

    [Fact]
    public void ClearMood_UnlogsTheDay_AndClosesTheBubble()
    {
        var model = new DiaryEntry { Emotion = Emotion.Grateful };
        var viewModel = new DiaryEntryViewModel(model);
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantCalm);

        viewModel.ClearMoodCommand.Execute(null);

        viewModel.Emotion.Should().Be(Emotion.None);
        model.Emotion.Should().Be(Emotion.None);
        viewModel.Quadrants.Should().OnlyContain(q => !q.IsSelected);
        viewModel.HasRefinements.Should().BeFalse();
        viewModel.IsRefinementOpen.Should().BeFalse();
        viewModel.HasCurrentMood.Should().BeFalse();
    }

    [Fact]
    public void SelectQuadrant_WithAnHourSelected_WritesThatHour()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Calm });
        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 14));

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantCalm);

        viewModel.HourlyMood.Single(h => h.Hour == 14).Mood.Should().Be(Emotion.Sad);
        viewModel.Emotion.Should().Be(Emotion.Calm, "the day-level summary stays the user's own");
        viewModel.Model.HourlyMood.Should().ContainSingle(h => h.Hour == 14 && h.Mood == Emotion.Sad);
        viewModel.CurrentQuadrant.Should().Be(EmotionQuadrant.UnpleasantCalm);
        viewModel.IsRefinementOpen.Should().BeTrue();
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Sad);

        viewModel.ClearMoodCommand.Execute(null);

        viewModel.HourlyMood.Single(h => h.Hour == 14).Mood.Should().Be(Emotion.None);
        viewModel.Model.HourlyMood.Should().BeEmpty();
        viewModel.Emotion.Should().Be(Emotion.Calm);
        viewModel.IsRefinementOpen.Should().BeFalse();
    }

    [Fact]
    public void SelectHour_ClosesTheBubble_AndMovesTheFacesToThatHoursMood()
    {
        var model = new DiaryEntry
        {
            Emotion = Emotion.Happy,
            HourlyMood = { new HourMood { Hour = 10, Mood = Emotion.Lonely } }
        };
        var viewModel = new DiaryEntryViewModel(model);
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantEnergetic);
        viewModel.IsRefinementOpen.Should().BeTrue();
        var slot = viewModel.HourlyMood.Single(h => h.Hour == 10);

        viewModel.SelectHourCommand.Execute(slot);

        viewModel.IsRefinementOpen.Should().BeFalse("the bubble belonged to the day, not to the hour");
        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantCalm);
        viewModel.CurrentMoodName.Should().Be(Emotion.Lonely.ToLocalizedName());
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Lonely);

        viewModel.SelectHourCommand.Execute(slot);

        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.PleasantEnergetic);
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Happy);
    }

    [Fact]
    public void SelectEmotion_TheSameChipTwiceOnAnHour_KeepsIt()
    {
        // Clearing is its own action; a chip only ever sets.
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 9));

        viewModel.SelectEmotionCommand.Execute(Emotion.Angry);
        viewModel.SelectEmotionCommand.Execute(Emotion.Angry);

        viewModel.HourlyMood.Single(h => h.Hour == 9).Mood.Should().Be(Emotion.Angry);
    }

    [Fact]
    public void Constructor_WithAMoodAlreadyLogged_PreselectsItsFaceAndChip_BubbleClosed()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Tired });

        viewModel.Quadrants.Single(q => q.IsSelected).Quadrant.Should().Be(EmotionQuadrant.UnpleasantCalm);
        viewModel.SelectedColumn.Should().Be(0);
        viewModel.CurrentMoodName.Should().Be(Emotion.Tired.ToLocalizedName());
        viewModel.HasRefinements.Should().BeTrue();
        viewModel.Refinements.Single(c => c.IsSelected).Emotion.Should().Be(Emotion.Tired);
        viewModel.IsRefinementOpen.Should().BeFalse();
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
            "the chips did not change, only which one is lit");
        raised.Should().Contain(nameof(DiaryEntryViewModel.CurrentQuadrant));
        raised.Should().Contain(nameof(DiaryEntryViewModel.IsRefinementOpen));
    }

    [Fact]
    public void FillKeys_FollowSelection_SoTheViewNeedsNoTriggers()
    {
        // Keys, not colours: each theme decides what a highlight looks like (the pink theme uses pinks).
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantCalm);
        viewModel.SelectEmotionCommand.Execute(Emotion.Grateful);

        var chosen = viewModel.Quadrants.Single(q => q.Quadrant == EmotionQuadrant.PleasantCalm);
        chosen.FillColorKey.Should().Be(chosen.ColorKey);
        chosen.ColorKey.Should().Be(EmotionQuadrant.PleasantCalm.ToThemeKey());
        viewModel.Quadrants.Where(q => q != chosen).Should().OnlyContain(q => q.FillColorKey == null);
        viewModel.CurrentColorKey.Should().Be(chosen.ColorKey);

        viewModel.Refinements.Single(c => c.Emotion == Emotion.Grateful).FillColorKey.Should().Be(chosen.ColorKey);
        viewModel.Refinements.Where(c => c.Emotion != Emotion.Grateful).Should().OnlyContain(c => c.FillColorKey == null);

        viewModel.ClearMoodCommand.Execute(null);

        chosen.FillColorKey.Should().BeNull();
        viewModel.CurrentColorKey.Should().BeNull();
    }

    [Fact]
    public void CloseRefinement_PutsTheBubbleAway_AndKeepsTheMood()
    {
        // A tap on the scrim or a scroll: the bubble goes, nothing else changes.
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantEnergetic);
        viewModel.IsRefinementOpen.Should().BeTrue();

        viewModel.CloseRefinementCommand.Execute(null);

        viewModel.IsRefinementOpen.Should().BeFalse();
        viewModel.Emotion.Should().Be(Emotion.Happy);
        viewModel.Refinements.Should().NotBeEmpty("closing is not forgetting; the chips are ready for the next tap");
    }

    [Fact]
    public void Anchors_ArePlainState_TheRowWritesAndThePopoversRead()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var faceBounds = new Microsoft.Maui.Graphics.Rect(120, 300, 64, 60);
        var chipBounds = new Microsoft.Maui.Graphics.Rect(280, 240, 90, 26);
        viewModel.RefineAnchor = faceBounds;
        viewModel.HourAnchor = chipBounds;

        viewModel.RefineAnchor.Should().Be(faceBounds);
        viewModel.HourAnchor.Should().Be(chipBounds);
        raised.Should().Contain(nameof(DiaryEntryViewModel.RefineAnchor));
        raised.Should().Contain(nameof(DiaryEntryViewModel.HourAnchor));
    }

    [Fact]
    public void OpenHourPicker_TogglesTheGrid_AndPutsTheRefinementAway()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.PleasantEnergetic);
        viewModel.IsRefinementOpen.Should().BeTrue();

        viewModel.OpenHourPickerCommand.Execute(null);

        viewModel.IsHourPickerOpen.Should().BeTrue();
        viewModel.IsRefinementOpen.Should().BeFalse("one bubble at a time");

        viewModel.OpenHourPickerCommand.Execute(null);
        viewModel.IsHourPickerOpen.Should().BeFalse();

        viewModel.OpenHourPickerCommand.Execute(null);
        viewModel.CloseHourPickerCommand.Execute(null);
        viewModel.IsHourPickerOpen.Should().BeFalse();
    }

    [Fact]
    public void SelectHour_FromThePicker_ClosesIt_AndTheChipReadsTheHour()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Happy });
        viewModel.OpenHourPickerCommand.Execute(null);

        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 14));

        viewModel.IsHourPickerOpen.Should().BeFalse("the row must be reachable to pick the hour's mood");
        viewModel.IsHourSelected.Should().BeTrue();
        viewModel.HourChipText.Should().Be("14:00");
        viewModel.IsHourChipFilled.Should().BeFalse("the hour has no mood yet");
        viewModel.HourChipFillColorKey.Should().BeNull();
        viewModel.HasCurrentMood.Should().BeFalse("the row now speaks for an empty hour, not for the day");

        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantEnergetic);

        viewModel.IsHourChipFilled.Should().BeTrue();
        viewModel.HourChipFillColorKey.Should().Be(EmotionQuadrant.UnpleasantEnergetic.ToThemeKey());
        viewModel.Emotion.Should().Be(Emotion.Happy, "the day stays what it was");
    }

    [Fact]
    public void ExitHourMode_ReturnsTheRowToTheDay_AndTheChipCountsLoggedHours()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry { Emotion = Emotion.Happy });
        viewModel.SelectHourCommand.Execute(viewModel.HourlyMood.Single(h => h.Hour == 14));
        viewModel.SelectQuadrantCommand.Execute(EmotionQuadrant.UnpleasantCalm);
        viewModel.IsRefinementOpen.Should().BeTrue();

        viewModel.ExitHourModeCommand.Execute(null);

        viewModel.IsHourSelected.Should().BeFalse();
        viewModel.HourlyMood.Should().OnlyContain(h => !h.IsSelected);
        viewModel.CurrentMood.Should().Be(Emotion.Happy);
        viewModel.IsRefinementOpen.Should().BeFalse("the bubble belonged to the hour");
        viewModel.LoggedHourCount.Should().Be(1);
        viewModel.HourChipText.Should().Be($"{Diarion.Resources.Localization.AppResources.MoodHourlyLabel} · 1");
        viewModel.IsHourChipFilled.Should().BeFalse();
        viewModel.Model.HourlyMood.Should().ContainSingle(h => h.Hour == 14 && h.Mood == Emotion.Sad, "leaving hour mode keeps the hour");
    }

    [Fact]
    public void HourChipText_IsTheBareLabel_WhileNoHourIsLogged()
    {
        var viewModel = new DiaryEntryViewModel(new DiaryEntry());

        viewModel.LoggedHourCount.Should().Be(0);
        viewModel.HourChipText.Should().Be(Diarion.Resources.Localization.AppResources.MoodHourlyLabel);
    }

    [Fact]
    public void HourSlot_TakesTheColourOfItsQuadrant()
    {
        new HourMoodViewModel(9, Emotion.Stressed).ColorKey.Should().Be(EmotionQuadrant.UnpleasantEnergetic.ToThemeKey());
        new HourMoodViewModel(9, Emotion.Neutral).ColorKey.Should().Be(EmotionQuadrant.Neutral.ToThemeKey());
        new HourMoodViewModel(9, Emotion.None).ColorKey.Should().BeNull();
    }
}
