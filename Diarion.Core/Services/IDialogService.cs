using System.Threading.Tasks;

namespace Diarion.Services;

public interface IDialogService
{
    Task ShowAlertAsync(string title, string message, string cancel = "OK");
    Task<bool> ShowConfirmationAsync(string title, string message, string accept = "Yes", string cancel = "No");
    Task<string> ShowPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel");
}
