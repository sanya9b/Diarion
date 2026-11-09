using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Resources.Localization;
using Diarion.Services.Recovery;

namespace Diarion.ViewModels;

/// <summary>
/// The first-launch offer (spec 21, D4): a copy of the diary was found in the user's iCloud before
/// onboarding could start. Two buttons, nothing automatic — restoring replaces whatever the fresh
/// install has, and the person may be setting up a second phone on purpose.
/// </summary>
public partial class RecoveryViewModel : ObservableObject
{
    private readonly ICloudRecoveryService _service;

    public RecoveryViewModel(ICloudRecoveryService service)
    {
        _service = service;
    }

    /// <summary>Called once, with true when the database was restored and false when the user skipped.</summary>
    public Action<bool>? Finished { get; set; }

    [ObservableProperty]
    private string _foundText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    /// <summary>0..1 while the file is on its way from iCloud.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public void Prepare(RecoveryCopyInfo copy)
    {
        FoundText = string.Format(
            CultureInfo.CurrentCulture,
            AppResources.RecoveryFoundTextFormat,
            CloudRecoveryViewModel.CopyDate(copy.ModifiedUtc));
        ErrorText = string.Empty;
        Progress = 0;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var progress = new Progress<double>(p => Progress = p);
            var outcome = await Task.Run(() => _service.RestoreAsync(progress));

            if (outcome == RecoveryRestoreOutcome.Restored)
            {
                Finished?.Invoke(true);
                return;
            }

            // Stays on the page: the person can try again when the network is back, or start fresh.
            ErrorText = CloudRecoveryViewModel.RestoreText(outcome);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recovery failed: {ex}");
            ErrorText = AppResources.RecoveryOutcomeFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartFresh()
    {
        if (IsBusy) return;
        Finished?.Invoke(false);
    }
}
