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

/// <summary>
/// One expense row in a budget's category detail. Carries its amount already formatted, in the same
/// currency and with the same sign placement as the feed, instead of a raw number the page would
/// format on its own and differently.
/// </summary>
public class CategoryExpenseItem
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

/// <summary>An account chip in the selector strip and the add-transaction picker.</summary>
public partial class AccountItemViewModel : ObservableObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public bool IsArchived { get; set; }

    /// <summary>
    /// The colour the account was given in its form, as a colour the chip can paint. Until now the
    /// swatch was chosen and never seen again anywhere on the page.
    /// </summary>
    public Microsoft.Maui.Graphics.Color Color =>
        string.IsNullOrEmpty(ColorHex)
            ? Microsoft.Maui.Graphics.Colors.Transparent
            : Microsoft.Maui.Graphics.Color.FromArgb(ColorHex);

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
