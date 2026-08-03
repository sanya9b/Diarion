using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class AiChatPage : ContentPage
{
    public AiChatPage(AiChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
