using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class HabitEditorPage : ContentPage
{
    public HabitEditorPage(HabitEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
