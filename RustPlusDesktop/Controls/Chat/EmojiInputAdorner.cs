using System;
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
    private static readonly SolidColorBrush s_pillBackgroundBrush = new(Color.FromArgb(0xF2, 0x1A, 0x22, 0x2E));
    private static readonly Pen s_pillBorderPen = new(new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)), 0.8);

    static EmojiInputAdorner()
    {
        s_pillBackgroundBrush.Freeze();
        s_pillBorderPen.Brush.Freeze();
        s_pillBorderPen.Freeze();
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

        double maxWidth = Math.Max(0, _textBox.ActualWidth - 36);
        double maxHeight = _textBox.ActualHeight;
        if (maxWidth <= 0 || maxHeight <= 0) return;

        // Clip strictly within the text box typing viewport so pills never bleed past the emoji button
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, maxWidth, maxHeight)));
        try
        {
            var matches = EmojiService.EmojiTokenRegex.Matches(text);
            if (matches.Count == 0) return;

            foreach (Match match in matches)
            {
                var token = match.Groups["token"].Value;
                var entry = EmojiService.ResolveEmojiOrItem(token);
                if (entry == null) continue;

                var icon = entry.Icon ?? EmojiService.GetIcon(entry);
                if (icon == null) continue;

                int startIndex = match.Index;
                int endIndex = match.Index + match.Length;

                try
                {
                    var startRect = _textBox.GetRectFromCharacterIndex(startIndex, false);
                    var endRect = _textBox.GetRectFromCharacterIndex(Math.Max(0, endIndex - 1), true);

                    if (startRect.IsEmpty || endRect.IsEmpty) continue;

                    double x = startRect.Left;
                    double y = startRect.Top;
                    double width = Math.Max(16, endRect.Right - startRect.Left);
                    double height = Math.Max(16, startRect.Height);

                    // Skip if completely scrolled outside viewport
                    if (x + width < 0 || x > maxWidth) continue;

                    var tagRect = new Rect(x - 1, y + 1, width + 2, Math.Max(12, height - 2));

                    // 1. Draw solid sleek pill background covering the raw text tag
                    dc.DrawRoundedRectangle(s_pillBackgroundBrush, s_pillBorderPen, tagRect, 4, 4);

                    // 2. Draw the emoji icon centered inside the pill
                    double iconSize = Math.Min(height - 2, 20);
                    double iconX = x + (width - iconSize) / 2.0;
                    double iconY = y + (height - iconSize) / 2.0;

                    var iconRect = new Rect(iconX, iconY, iconSize, iconSize);
                    dc.DrawImage(icon, iconRect);
                }
                catch
                {
                    // Ignore layout race conditions while typing
                }
            }
        }
        finally
        {
            dc.Pop();
        }
    }
}
