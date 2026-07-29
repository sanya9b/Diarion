using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class PromptEditorPage : ContentPage
{
    public PromptEditorPage(PromptEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
