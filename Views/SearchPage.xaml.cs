using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class SearchPage : ContentPage
{
    public SearchPage(SearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
