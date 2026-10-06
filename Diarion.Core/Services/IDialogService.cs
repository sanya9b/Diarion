using System.Threading.Tasks;

namespace Diarion.Services;

public interface IDialogService
{
    Task ShowAlertAsync(string title, string message, string cancel = "OK");
    Task<bool> ShowConfirmationAsync(string title, string message, string accept = "Yes", string cancel = "No");
    Task<string> ShowPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel");

    /// <summary>
    /// Asks the user to pick one of several actions. Returns the chosen label, or null if they backed out
    /// — which a two-button confirmation cannot express, and a destructive choice needs to be able to.
    /// </summary>
    Task<string?> ShowActionSheetAsync(string title, string cancel, params string[] options);
}
