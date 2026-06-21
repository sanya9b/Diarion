using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Diarion.Services;

public class MauiNavigationService : INavigationService
{
    public Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        if (parameters != null)
        {
            return Shell.Current.GoToAsync(route, parameters);
        }
        return Shell.Current.GoToAsync(route);
    }

    public async Task NavigateBackAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var page = GetActivePage();
        if (page?.Navigation?.NavigationStack.Count > 1)
        {
            await page.Navigation.PopAsync();
        }
    }

    private static Page? GetActivePage()
    {
        if (Shell.Current?.CurrentPage is Page currentPage)
        {
            return currentPage;
        }

        if (Application.Current?.Windows.Count > 0)
        {
            return Application.Current.Windows[0].Page;
        }

        return null;
    }
}
