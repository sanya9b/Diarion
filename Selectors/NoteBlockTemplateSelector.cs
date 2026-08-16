using Diarion.Models.Markdown;
using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Selectors;

/// <summary>
/// Picks how one line of a note is drawn. The kinds differ by what sits beside the text — a tick box, a
/// number, a bullet, nothing — and by the font, so each keeps its own template rather than one template
/// switching a marker column on and off.
/// </summary>
/// <remarks>
/// A selector runs once, when the item is realised. That is why <see cref="NoteBlockViewModel.Kind"/>
/// never changes on a live block: the editor swaps the block for a new one instead, which brings the
/// item back through here.
/// </remarks>
public class NoteBlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate ParagraphTemplate { get; set; } = null!;
    public DataTemplate Heading1Template { get; set; } = null!;
    public DataTemplate Heading2Template { get; set; } = null!;
    public DataTemplate Heading3Template { get; set; } = null!;
    public DataTemplate BulletTemplate { get; set; } = null!;
    public DataTemplate NumberedTemplate { get; set; } = null!;
    public DataTemplate ChecklistTemplate { get; set; } = null!;
    public DataTemplate QuoteTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not NoteBlockViewModel block) return ParagraphTemplate;

        return block.Kind switch
        {
            MarkdownBlockKind.Heading1 => Heading1Template,
            MarkdownBlockKind.Heading2 => Heading2Template,
            MarkdownBlockKind.Heading3 => Heading3Template,
            MarkdownBlockKind.Bullet => BulletTemplate,
            MarkdownBlockKind.Numbered => NumberedTemplate,
            MarkdownBlockKind.Checklist => ChecklistTemplate,
            MarkdownBlockKind.Quote => QuoteTemplate,
            _ => ParagraphTemplate
        };
    }
}
