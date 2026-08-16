using Microsoft.Maui.Controls;

namespace Diarion.Controls;

/// <summary>
/// The note editor's formatting bar. All of it is in the XAML: the commands belong to
/// <see cref="ViewModels.NoteDetailViewModel"/>, which the bar inherits as its binding context.
/// </summary>
public partial class NoteFormatBar : ContentView
{
    public NoteFormatBar()
    {
        InitializeComponent();
    }
}
