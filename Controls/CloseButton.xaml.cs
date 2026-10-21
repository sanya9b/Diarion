using System;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace Diarion.Controls;

/// <summary>
/// The close button of a modal page. Runs <see cref="Command"/> when set and raises <see cref="Tapped"/>
/// either way, so pages that close from a view model and pages that close from code-behind both use it.
/// </summary>
public partial class CloseButton : ContentView
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(CloseButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(CloseButton));

    public CloseButton()
    {
        InitializeComponent();
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public event EventHandler<TappedEventArgs>? Tapped;

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
        Tapped?.Invoke(this, e);
    }
}
