using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RustPlusDesk.Services.Emoji;

namespace RustPlusDesk.Controls.Chat;

public class EmojiInputAdorner : Adorner
{
    private readonly TextBox _textBox;
    private static readonly SolidColorBrush s_dimBackgroundBrush = new(Color.FromArgb(0x66, 0x10, 0x15, 0x1C));
    private static readonly SolidColorBrush s_dimTextCoverBrush = new(Color.FromArgb(0x40, 0x00, 0x00, 0x00));

    static EmojiInputAdorner()
    {
        s_dimBackgroundBrush.Freeze();
        s_dimTextCoverBrush.Freeze();
    }

    public EmojiInputAdorner(TextBox textBox) : base(textBox)
    {
        _textBox = textBox;
        IsHitTestVisible = false;

        _textBox.TextChanged += (s, e) => InvalidateVisual();
        _textBox.SelectionChanged += (s, e) => InvalidateVisual();
        _textBox.SizeChanged += (s, e) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var text = _textBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        var matches = EmojiService.EmojiTokenRegex.Matches(text);
        if (matches.Count == 0) return;

        foreach (Match match in matches)
        {
            var token = match.Groups["token"].Value;
            var entry = EmojiService.ResolveEmojiOrItem(token);
            if (entry == null || entry.Icon == null) continue;

            int startIndex = match.Index;
            int endIndex = match.Index + match.Length;

            try
            {
                var startRect = _textBox.GetRectFromCharacterIndex(startIndex, false);
                var endRect = _textBox.GetRectFromCharacterIndex(Math.Max(0, endIndex - 1), true);

                if (startRect.IsEmpty || endRect.IsEmpty) continue;

                double x = startRect.Left;
                double y = startRect.Top;
                double width = Math.Max(12, endRect.Right - startRect.Left);
                double height = Math.Max(14, startRect.Height);

                var tagRect = new Rect(x - 1, y, width + 2, height);

                // 1. Darken and dim the text of the matched token
                dc.DrawRoundedRectangle(s_dimBackgroundBrush, null, tagRect, 3, 3);
                dc.DrawRoundedRectangle(s_dimTextCoverBrush, null, tagRect, 3, 3);

                // 2. Draw the emoji icon directly on/aligned with the token
                double iconSize = Math.Min(height - 2, 18);
                double iconX = x + 2;
                double iconY = y + (height - iconSize) / 2.0;

                var iconRect = new Rect(iconX, iconY, iconSize, iconSize);
                dc.DrawImage(entry.Icon, iconRect);
            }
            catch
            {
                // Ignore layout race conditions while typing
            }
        }
    }
}
