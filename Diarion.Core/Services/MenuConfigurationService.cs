using System.Collections.Generic;
using Diarion.Models;
using Diarion.ViewModels;

namespace Diarion.Services;

public class MenuConfigurationService : IMenuConfigurationService
{
    public List<QuickMenuItem> GetDefaultMenuItems()
    {
        return new List<QuickMenuItem>
        {
            new QuickMenuItem 
            { 
                Id = "Reading", 
                StrokeColorKey = "Theme_Sage", 
                PathData = "M 3 4 H 7 V 20 H 3 Z M 3 8 H 7 M 10 6 H 14 V 20 H 10 Z M 10 10 H 14 M 17 5 H 21 V 20 H 17 Z M 17 9 H 21"
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
            }
        };
    }
}