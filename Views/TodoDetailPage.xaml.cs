using Diarion.ViewModels;

namespace Diarion.Views;

public partial class TodoDetailPage : ContentPage
{
    public TodoDetailPage(TodoDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}