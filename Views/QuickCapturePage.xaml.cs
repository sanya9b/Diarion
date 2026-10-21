using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class QuickCapturePage : ContentPage
{
    private readonly QuickCaptureViewModel _viewModel;
    private bool _loaded;

    public QuickCapturePage(QuickCaptureViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await _viewModel.LoadAsync();
    }

    /// <summary>Android's back button closes the sheet without saving, the same as the cross.</summary>
    protected override bool OnBackButtonPressed()
    {
        _viewModel.CancelCommand.Execute(null);
        return true;
    }
}
