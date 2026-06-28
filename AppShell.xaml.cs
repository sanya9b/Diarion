using System.Globalization;
using Diarion.Diagnostics;
using Microsoft.Maui.Controls;

namespace Diarion;

public partial class AppShell : Shell
{
    public AppShell()
    {
        using var _ = StartupTrace.Measure("AppShell..ctor");
        
        InitializeComponent();
        StartupTrace.Mark("AppShell.InitializeComponent complete");
        
        Routing.RegisterRoute("DiaryDetail", typeof(Views.DiaryDetailPage));
        Routing.RegisterRoute("TodoDetail", typeof(Views.TodoDetailPage));
        Routing.RegisterRoute("HabitTracker", typeof(Views.HabitTrackerPage));
        Routing.RegisterRoute("ReadingTracker", typeof(Views.ReadingTrackerPage));
        Routing.RegisterRoute("HappyMoments", typeof(Views.HappyMomentsPage));
        Routing.RegisterRoute("GoodDeeds", typeof(Views.GoodDeedsPage));
        Routing.RegisterRoute("Wishlist", typeof(Views.WishlistPage));
        Routing.RegisterRoute("Finance", typeof(Views.FinancePage));
    }
}
