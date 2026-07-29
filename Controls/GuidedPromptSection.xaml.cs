using System.Windows.Input;

namespace Diarion.Controls;

public partial class GuidedPromptSection : ContentView
{
    /// <summary>
    /// Opens the prompt library. Supplied by the host page rather than the entry view model, which has
    /// no navigation service and should not grow one just for this affordance.
    /// </summary>
    public static readonly BindableProperty ManageCommandProperty =
        BindableProperty.Create(nameof(ManageCommand), typeof(ICommand), typeof(GuidedPromptSection));

    public ICommand? ManageCommand
    {
        get => (ICommand?)GetValue(ManageCommandProperty);
        set => SetValue(ManageCommandProperty, value);
    }

    public GuidedPromptSection()
    {
        InitializeComponent();
    }
}
