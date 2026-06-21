using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public partial class QuickMenuItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _strokeColorKey = string.Empty;

    [ObservableProperty]
    private string _pathData = string.Empty;

    public Microsoft.Maui.Controls.Shapes.Geometry? IconGeometry 
    { 
        get 
        {
            if (string.IsNullOrWhiteSpace(PathData)) return null;
            return (Microsoft.Maui.Controls.Shapes.Geometry)new Microsoft.Maui.Controls.Shapes.PathGeometryConverter().ConvertFromInvariantString(PathData)!;
        }
    }

    [ObservableProperty]
    private string _fillColorKey = string.Empty;

    [ObservableProperty]
    private bool _usesUniformAspect;

    [ObservableProperty]
    private ICommand? _command;

    [ObservableProperty]
    private int _order;
}
