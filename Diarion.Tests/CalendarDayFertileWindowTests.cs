using System.Collections.Generic;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The fertile-window marker is postponed, not removed, so what these pin down is the seam between
/// the two: the forecast keeps marking the day, the calendar keeps quiet about it, and the switch
/// between those is one flag. Written to stay meaningful whichever way that flag is set — a test
/// that only asserts <c>false</c> would have to be rewritten the day the marker comes back, which
/// is the day it is most worth having.
/// </summary>
public class CalendarDayFertileWindowTests
{
    [Fact]
    public void TheMarker_IsDrawnOnlyWhenItIsOffered()
    {
        var day = new CalendarDay { IsFertileWindow = true };

        day.ShowsFertileWindow.Should().Be(CycleDisplay.FertileWindowMarkerOffered);
    }

    [Fact]
    public void ADayOutsideTheWindow_NeverDrawsIt()
    {
        new CalendarDay { IsFertileWindow = false }.ShowsFertileWindow.Should().BeFalse();
    }

    [Fact]
    public void TheForecastFlag_StaysTrueWhileTheMarkerIsHidden()
    {
        // CalendarSectionViewModel assigns what the forecast returned, and it should go on doing
        // that. Hiding the icon by writing false into a property called IsFertileWindow would put
        // the lie in the model instead of the decision in one flag, and the next reader of the
        // calendar would have no way to tell the two apart.
        new CalendarDay { IsFertileWindow = true }.IsFertileWindow.Should().BeTrue();
    }

    [Fact]
    public void TurningTheDayIntoAFertileOne_AnnouncesTheMarkerToo()
    {
        // Without the NotifyPropertyChangedFor this passes silently today — nothing is bound to a
        // property that is always false — and fails the moment the flag is flipped back, as an icon
        // that only appears after the month is redrawn. That is a bug found in the wrong year.
        var day = new CalendarDay();
        var announced = new List<string?>();
        day.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        day.IsFertileWindow = true;

        announced.Should().Contain(nameof(CalendarDay.ShowsFertileWindow));
    }
}
