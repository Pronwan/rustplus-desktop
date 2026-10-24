using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RustPlusDesk.Services.Emoji;

namespace RustPlusDesk.Controls.Chat;

public class RichChatTextBlock : TextBlock
{
    public static readonly DependencyProperty RawTextProperty = DependencyProperty.Register(
        nameof(RawText),
        typeof(string),
        typeof(RichChatTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnRawTextChanged));

    public static readonly DependencyProperty EmojiSizeProperty = DependencyProperty.Register(
        nameof(EmojiSize),
        typeof(double),
        typeof(RichChatTextBlock),
        new FrameworkPropertyMetadata(32.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnRawTextChanged));

    public string RawText
    {
        get => (string)GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    public double EmojiSize
    {
        get => (double)GetValue(EmojiSizeProperty);
        set => SetValue(EmojiSizeProperty, value);
    }

    public RichChatTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
    }

    private static void OnRawTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichChatTextBlock tb)
        {
            tb.UpdateInlines();
        }
    }

    private void UpdateInlines()
    {
        Inlines.Clear();

        var text = RawText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var matches = EmojiService.EmojiTokenRegex.Matches(text);
        if (matches.Count == 0)
        {
            Inlines.Add(new Run(text));
            return;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            // Text before the token
            if (match.Index > lastIndex)
            {
                var prefix = text.Substring(lastIndex, match.Index - lastIndex);
                Inlines.Add(new Run(prefix));
            }

            var token = match.Groups["token"].Value;
            var entry = EmojiService.ResolveEmojiOrItem(token);

            if (entry != null)
            {
                FrameworkElement visual;
                if (entry.IsCustomEmoji)
                {
                    visual = new AnimatedEmojiImage
                    {
                        EmojiName = entry.Name,
                        AutoPlay = true,
                        Width = EmojiSize,
                        Height = EmojiSize,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 2, -3),
                        ToolTip = $"{entry.DisplayName} (:{entry.Name}:)"
                    };
                }
                else if (entry.Icon != null)
                {
                    visual = new Image
                    {
                        Source = entry.Icon,
                        Width = EmojiSize,
                        Height = EmojiSize,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 2, -2),
                        ToolTip = $"{entry.DisplayName} (:{entry.Name}:)"
                    };
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                }
                else
                {
                    visual = null!;
                }

                if (visual != null)
                {
                    var container = new InlineUIContainer(visual)
                    {
                        BaselineAlignment = BaselineAlignment.Center
                    };
                    Inlines.Add(container);
                }
                else
                {
                    Inlines.Add(new Run(match.Value));
                }
            }
            else
            {
                // Unrecognized token -> render verbatim
                Inlines.Add(new Run(match.Value));
            }

            lastIndex = match.Index + match.Length;
        }

        // Remaining text after last token
        if (lastIndex < text.Length)
        {
            Inlines.Add(new Run(text.Substring(lastIndex)));
        }
    }
}
