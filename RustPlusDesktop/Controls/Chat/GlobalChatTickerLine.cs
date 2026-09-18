using RustPlusDesk.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RustPlusDesk.Controls.Chat;

/// <summary>
/// One line of the ticker: a name, and what they said.
///
/// A control rather than two named elements in the strip because the strip holds two of these and
/// slides one over the other. It also renders the body through
/// <see cref="RichChatTextBlock"/>, so <c>:exclamation:</c> is an emoji here exactly as it is in
/// the room — a token shown verbatim is the strip advertising that it cannot read its own content.
/// </summary>
public sealed class GlobalChatTickerLine : Grid
{
    /// <summary>Small enough to sit on a 30px strip without pushing the text off its baseline.</summary>
    private const double TickerEmojiSize = 16;

    private readonly TextBlock _author;
    private readonly RichChatTextBlock _body;
    private readonly TranslateTransform _slide = new();

    public GlobalChatTickerLine()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        RenderTransform = _slide;

        _author = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 5, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        SetColumn(_author, 0);
        Children.Add(_author);

        _body = new RichChatTextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5,
            EmojiSize = TickerEmojiSize,

            // The strip is one line high, so the body must not wrap into a second one it has no
            // room for. RichChatTextBlock wraps by default, which is right in the room and wrong
            // here.
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        SetColumn(_body, 1);
        Children.Add(_body);
    }

    /// <summary>How far the line is displaced from its resting position, for the roll.</summary>
    public double SlideOffset
    {
        get => _slide.Y;
        set => _slide.Y = value;
    }

    public TranslateTransform Slide => _slide;

    /// <summary>Shows a message, with the author in whatever colour they carry.</summary>
    public void SetLine(ChatLine line)
    {
        _author.Text = line.SenderName + ":";
        _author.Foreground = line.SenderNameBrush;
        _author.Visibility = Visibility.Visible;

        _body.RawText = line.Body;
        _body.ClearValue(TextBlock.ForegroundProperty);
        _body.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
    }

    /// <summary>
    /// Shows a stand-in for when nobody is talking — the strip is never allowed to be blank.
    /// </summary>
    public void SetNotice(string text)
    {
        _author.Text = string.Empty;
        _author.Visibility = Visibility.Collapsed;

        _body.RawText = text;
        _body.SetResourceReference(TextBlock.ForegroundProperty, "TextSubtle");
    }
}
