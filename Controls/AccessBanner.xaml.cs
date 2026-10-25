using Diarion.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

namespace Diarion.Controls;

/// <summary>
/// Resolves its own view model rather than borrowing the page's: the banner is about access, not
/// about the day on screen, and keeping it out of MainViewModel keeps that class from growing a
/// subscription concern it has no business with.
/// </summary>
public partial class AccessBanner : ContentView
{
    public AccessBanner()
    {
        InitializeComponent();
        BindingContext = IPlatformApplication.Current?.Services.GetService<AccessBannerViewModel>();
    }
}
