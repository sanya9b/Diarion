using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Diarion.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null);
    Task NavigateBackAsync();

    /// <summary>Opens the app flyout/menu (abstracts the Shell flyout so ViewModels stay testable).</summary>
    Task OpenFlyoutAsync();
}
