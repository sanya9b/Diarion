using System.Linq;
using System.Reflection;
using Diarion.Models;
using Diarion.Resources.Icons;
using FluentAssertions;
using Microsoft.Maui.Controls.Shapes;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The drawn icons that replaced colour emoji. A typo in a path string does not fail the build: XAML
/// takes the geometry through x:Static, so a broken one would only show up as an empty space on screen.
/// </summary>
public class AppIconsTests
{
    public static TheoryData<string> IconNames()
    {
        var data = new TheoryData<string>();
        foreach (var p in typeof(AppIcons).GetProperties(BindingFlags.Public | BindingFlags.Static)
                     .Where(p => p.PropertyType == typeof(Geometry)))
            data.Add(p.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(IconNames))]
    public void EveryIcon_ParsesIntoAtLeastOneFigure(string name)
    {
        var geometry = (Geometry)typeof(AppIcons).GetProperty(name)!.GetValue(null)!;

        geometry.Should().BeOfType<PathGeometry>()
            .Which.Figures.Should().NotBeEmpty($"{name} must draw something");
    }

    [Fact]
    public void EachRead_HandsOutAFreshGeometry()
    {
        // One Geometry shared by two Paths is the kind of cross-element sharing MAUI handles badly.
        AppIcons.Calendar.Should().NotBeSameAs(AppIcons.Calendar);
    }

    [Fact]
    public void EveryStatisticsTab_CarriesADrawnIcon()
    {
        var vm = StatisticsRangeTests.BuildViewModel(out _);

        vm.Tabs.Should().NotBeEmpty();
        vm.Tabs.Should().OnlyContain(t => t.IconGeometry is PathGeometry);
    }

    [Theory]
    [InlineData(Season.Winter, AppIcons.SnowflakePath)]
    [InlineData(Season.Spring, AppIcons.SproutPath)]
    [InlineData(Season.Summer, AppIcons.SunPath)]
    [InlineData(Season.Autumn, AppIcons.LeafPath)]
    public void EverySeason_HasItsOwnSign(Season season, string path)
    {
        var expected = (PathGeometry)AppIcons.Parse(path);
        var actual = AppIcons.ForSeason(season).Should().BeOfType<PathGeometry>().Subject;
        actual.Figures.Count.Should().Be(expected.Figures.Count);
        actual.Figures[0].StartPoint.Should().Be(expected.Figures[0].StartPoint);
    }
}
