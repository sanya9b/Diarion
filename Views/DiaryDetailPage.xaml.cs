using Diarion.ViewModels;

namespace Diarion.Views;

public partial class DiaryDetailPage : ContentPage
{
    public DiaryDetailPage(DiaryDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
