using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Selectors;

/// <summary>
/// Picks the row template for the unified finance feed. The three kinds carry different fields, so each
/// keeps its own compiled-binding template rather than one template hiding four collapsed subtrees per
/// recycled row.
/// </summary>
public class FinanceFeedTemplateSelector : DataTemplateSelector
{
    public DataTemplate TransactionTemplate { get; set; } = null!;
    public DataTemplate TransferTemplate { get; set; } = null!;
    public DataTemplate PlannedTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        TransferFeedItem => TransferTemplate,
        PlannedFeedItem => PlannedTemplate,
        _ => TransactionTemplate
    };
}
