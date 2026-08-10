using System.Collections.Generic;
using Diarion.Models;
using Diarion.ViewModels;

namespace Diarion.Services;

public class MenuConfigurationService : IMenuConfigurationService
{
    /// <summary>The one id that is conditional, so the filter and the catalogue cannot drift apart.</summary>
    public const string AiChatId = "AiChat";

    public List<QuickMenuItem> GetDefaultMenuItems()
    {
        return new List<QuickMenuItem>
        {
            new QuickMenuItem
            {
                Id = "Search",
                // Must be one of Sage/Berry/Amber/Coral: QuickMenuView.xaml resolves the stroke
                // through DataTriggers on exactly those four, and any other key leaves the icon
                // with no stroke at all — drawn, but invisible.
                StrokeColorKey = "Theme_Sage",
                UsesUniformAspect = true,
                // Two semicircular arcs rather than one full circle, matching Wishlist: an arc
                // command that returns to its own start point is degenerate and draws nothing.
                PathData = "M 28,16 A 12,12 0 0 1 28,40 A 12,12 0 0 1 28,16 M 37,37 L 48,48"
            },
            new QuickMenuItem 
            { 
                Id = "Notes", 
                StrokeColorKey = "Theme_Amber", 
                UsesUniformAspect = true,
                PathData = "M 16 10 H 34 V 40 H 16 Z M 20 16 H 30 M 20 22 H 30 M 20 28 H 30"
            },
            new QuickMenuItem 
            { 
                Id = "Reading", 
                StrokeColorKey = "Theme_Sage", 
                UsesUniformAspect = true,
                PathData = "M 19,36 H 50 C 50.5,36 51,36.5 51,37 V 44 C 51,44.5 50.5,45 50,45 H 19 C 17.5,45 16,43.5 16,42 V 38 C 16,36.5 17.5,36 19,36 Z M 22,36 V 45 M 45,28 H 15 C 14.5,28 14,28.5 14,29 V 36 C 14,36.5 14.5,37 15,37 H 45 C 46.5,37 48,35.5 48,34 V 30 C 48,28.5 46.5,28 45,28 Z M 42,28 V 37 M 20,20 H 46 C 46.5,20 47,20.5 47,21 V 28 C 47,28.5 46.5,29 46,29 H 20 C 18.5,29 17,27.5 17,26 V 22 C 17,20.5 18.5,20 20,20 Z M 23,20 V 29 M 47,39 H 51 M 47,42 H 51 M 18,31 H 14 M 18,34 H 14 M 43,23 H 47 M 43,26 H 47 M 29,20 V 32 L 32,29 L 35,32 V 20 Z"
            },
            new QuickMenuItem 
            { 
                Id = "Moments", 
                StrokeColorKey = "Theme_Berry", 
                PathData = "M 19 14 C 20.49 12.54 22 10.79 22 8.5 A 5.5 5.5 0 0 0 16.5 3 C 14.74 3 13.5 3.5 12 5 C 10.5 3.5 9.26 3 7.5 3 A 5.5 5.5 0 0 0 2 8.5 C 2 10.79 3.51 12.54 5 14 L 12 21.35 Z"
            },
            new QuickMenuItem 
            { 
                Id = "Deeds", 
                StrokeColorKey = "Theme_Amber", 
                FillColorKey = "Theme_Amber",
                PathData = "M 19 14 C 20.49 12.54 22 10.79 22 8.5 A 5.5 5.5 0 0 0 16.5 3 C 14.74 3 13.5 3.5 12 5 C 10.5 3.5 9.26 3 7.5 3 A 5.5 5.5 0 0 0 2 8.5 C 2 10.79 3.51 12.54 5 14 L 12 21.35 Z"
            },
            new QuickMenuItem 
            { 
                Id = "Habits", 
                StrokeColorKey = "Theme_Coral", 
                PathData = "M 7.9 20 A 9 9 0 1 0 4 16.1 L 2 22 Z M 9 12 L 11 14 L 15 10"
            },
            new QuickMenuItem 
            { 
                Id = "Wishlist", 
                StrokeColorKey = "Theme_Berry", 
                UsesUniformAspect = true,
                PathData = "M 32,18 A 14,14 0 0 1 32,46 A 14,14 0 0 1 32,18 M 32,26 A 6,6 0 0 1 32,38 A 6,6 0 0 1 32,26 M 32,10 V 15 M 32,49 V 54 M 10,32 H 15 M 49,32 H 54"
            },
            new QuickMenuItem 
            { 
                Id = "Finance", 
                StrokeColorKey = "Theme_Sage", 
                UsesUniformAspect = true,
                PathData = "M 18 26 C 18 21 21 21 22 21 H 42 C 43 21 46 21 46 26 V 42 C 46 47 43 47 42 47 H 22 C 21 47 18 47 18 42 V 26 Z M 18 26 H 46 M 38 31 H 46 V 39 H 38 C 35 39 35 31 38 31 Z M 41.5 33.5 A 1.5 1.5 0 1 1 41.5 36.5 A 1.5 1.5 0 1 1 41.5 33.5 Z"
            },
            new QuickMenuItem
            {
                // Listed here like the rest, but shown only when a generative model is installed —
                // QuickMenuViewModel filters it. A permanent tile would promise an answer that a
                // low-tier phone is never offered a model for.
                Id = AiChatId,
                StrokeColorKey = "Theme_Coral",
                UsesUniformAspect = true,
                // Two sparkles, not a speech bubble: Habits is already a coral speech bubble two
                // tiles away, and at 24 px the pair was indistinguishable.
                PathData = "M 28,14 C 29.5,22 34,26.5 42,28 C 34,29.5 29.5,34 28,42 C 26.5,34 22,29.5 14,28 C 22,26.5 26.5,22 28,14 Z M 44,37 C 44.8,41 47,43.2 51,44 C 47,44.8 44.8,47 44,51 C 43.2,47 41,44.8 37,44 C 41,43.2 43.2,41 44,37 Z"
            }
        };
    }
}