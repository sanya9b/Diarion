using System.Linq;
using Diarion.Services;
using FluentAssertions;
using Microsoft.Maui.Controls.Shapes;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The quick menu tiles draw their icons from path strings. A typo in one does not throw anywhere a
/// user would notice: the tile simply comes up empty. Same for the stroke key, which QuickMenuView
/// resolves through triggers on exactly four accents.
/// </summary>
public class MenuConfigurationServiceTests
{
    [Fact]
    public void EveryTile_HasAnIconThatParsesIntoAFigure()
    {
        foreach (var item in new MenuConfigurationService().GetDefaultMenuItems())
        {
            var geometry = item.IconGeometry as PathGeometry;

            geometry.Should().NotBeNull(because: $"tile {item.Id} needs an icon");
            geometry!.Figures.Should().NotBeEmpty(because: $"tile {item.Id} would be drawn empty");
        }
    }

    [Fact]
    public void EveryTile_UsesAnAccentTheTileCanResolve()
    {
        var resolvable = new[] { "Theme_Sage", "Theme_Berry", "Theme_Amber", "Theme_Coral" };

        new MenuConfigurationService().GetDefaultMenuItems()
            .Select(item => item.StrokeColorKey)
            .Should().OnlyContain(key => resolvable.Contains(key));
    }
}
