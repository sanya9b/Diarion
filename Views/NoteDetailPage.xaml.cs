using System;
using Diarion.Controls;
using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class NoteDetailPage : ContentPage
{
    /// <summary>
    /// The formatting bar that rides on the keyboard. Only ever set on iOS, where it stands in for the
    /// bar laid out at the bottom of the page; everywhere else it is null and the lines ignore it.
    /// </summary>
    public static readonly BindableProperty AccessoryBarProperty =
        BindableProperty.Create(nameof(AccessoryBar), typeof(View), typeof(NoteDetailPage), null);

    public NoteDetailPage(NoteDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        // The same control as the one in the page, bound to the same commands; it is simply not in
        // the page's tree, because the keyboard owns it. One per page, shared by every line.
        if (OperatingSystem.IsIOS())
        {
            AccessoryBar = new NoteFormatBar { BindingContext = viewModel };
        }
    }

    public View? AccessoryBar
    {
        get => (View?)GetValue(AccessoryBarProperty);
        set => SetValue(AccessoryBarProperty, value);
    }
}
