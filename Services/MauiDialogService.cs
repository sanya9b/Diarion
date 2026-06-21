using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Diarion.Services;

public class MauiDialogService : IDialogService
{
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

    public Task ShowAlertAsync(string title, string message, string cancel = "OK")
    {
        var page = GetActivePage();
        return page is null ? Task.CompletedTask : page.DisplayAlertAsync(title, message, cancel);
    }

    public Task<bool> ShowConfirmationAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        var page = GetActivePage();
        return page is null ? Task.FromResult(false) : page.DisplayAlertAsync(title, message, accept, cancel);
    }

    public Task<string> ShowPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        var page = GetActivePage();
        return page is null ? Task.FromResult(string.Empty) : page.DisplayPromptAsync(title, message, accept, cancel);
    }
}
