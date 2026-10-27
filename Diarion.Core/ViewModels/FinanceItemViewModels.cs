using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.ViewModels;

/// <summary>A budget's computed progress row on the finance page.</summary>
public class BudgetItemViewModel
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string AmountText { get; set; } = string.Empty;      // "spent / limit"
    public string RemainingText { get; set; } = string.Empty;
    public string ProgressPercentText { get; set; } = string.Empty;
    public double Progress { get; set; }
    public bool IsOverspent { get; set; }
}

/// <summary>An account chip in the selector strip and the add-transaction picker.</summary>
public partial class AccountItemViewModel : ObservableObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public bool IsArchived { get; set; }

    [ObservableProperty]
    private string _balanceText = string.Empty;

    /// <summary>Selected in the account-filter strip.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Selected in the add-transaction account picker.</summary>
    [ObservableProperty]
    private bool _isPickerSelected;
}

/// <summary>
/// One choice in a swatch strip (account icon or colour). Carries its own selection flag because the
/// page highlights selection with DataTriggers, which need a bool on the bound item.
/// </summary>
public partial class SelectableOptionViewModel : ObservableObject
{
    public string Value { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
