using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Diarion.Controls.Handlers;

/// <summary>
/// The handler behind <see cref="RichTextBlock"/>: the part every platform shares, which is the list of
/// what can change. Each platform file supplies the <c>Map*</c> methods named here against its own
/// text control — see the <c>.Apple</c>, <c>.Android</c> and <c>.Windows</c> siblings.
/// </summary>
/// <remarks>
/// The look properties all map to one method on purpose: font size, weight, colours and the struck
/// state combine into the attributes every run is drawn with, so any one of them changing means the
/// whole line is redrawn from <see cref="RichTextBlock.Body"/>.
/// </remarks>
public partial class RichTextBlockHandler
{
    public static readonly IPropertyMapper<RichTextBlock, RichTextBlockHandler> Mapper =
        new PropertyMapper<RichTextBlock, RichTextBlockHandler>(ViewHandler.ViewMapper)
        {
            [nameof(RichTextBlock.Body)] = MapBody,
            [nameof(RichTextBlock.TypingStyle)] = MapTypingStyle,
            [nameof(RichTextBlock.FontSize)] = MapLook,
            [nameof(RichTextBlock.FontAttributes)] = MapLook,
            [nameof(RichTextBlock.TextColor)] = MapLook,
            [nameof(RichTextBlock.DimColor)] = MapLook,
            [nameof(RichTextBlock.CodeColor)] = MapLook,
            [nameof(RichTextBlock.LinkColor)] = MapLook,
            [nameof(RichTextBlock.IsStruck)] = MapLook,
            [nameof(RichTextBlock.Placeholder)] = MapPlaceholder,
            [nameof(RichTextBlock.PlaceholderColor)] = MapPlaceholder,
            [nameof(RichTextBlock.InputAccessory)] = MapInputAccessory
        };

    public static readonly CommandMapper<RichTextBlock, RichTextBlockHandler> CommandMapper =
        new(ViewHandler.ViewCommandMapper)
        {
            [RichTextBlock.FocusAt] = MapFocusAt
        };

    public RichTextBlockHandler() : base(Mapper, CommandMapper)
    {
    }
}
