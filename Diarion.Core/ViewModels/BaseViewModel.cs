using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnBusyStateChanged();

    /// <summary>Hook for derived VMs to react to IsBusy changes (e.g. recompute composite loading flags).</summary>
    protected virtual void OnBusyStateChanged() { }
}
