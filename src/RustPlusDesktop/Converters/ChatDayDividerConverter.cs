using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters;

/// <summary>
/// Where a chat list crosses into a new day, and what to call that day.
///
/// The lines carry no "first of the day" flag of their own, so this works it out from the line and
/// the list it sits in. Kept apart from the converter so the rule can be tested without bindings.
/// </summary>
public static class ChatDayDivider
{
    /// <summary>The local calendar day a line was sent on, or null when it carries no time.</summary>
    public static DateTime? LocalDay(ChatLine? line)
        => line?.SentAt is { } at ? at.ToLocalTime().Date : null;

    /// <summary>True when both lines have a time and fall on the same local day.</summary>
    public static bool IsSameLocalDay(ChatLine? a, ChatLine? b)
        => LocalDay(a) is { } dayA && LocalDay(b) is { } dayB && dayA == dayB;

    /// <summary>
    /// The day a divider above <paramref name="index"/> should name, or null for no divider.
    /// The first timed line always opens its day; after that a divider appears only where the day
    /// changes. Lines without a time are skipped when looking back, so they never cause a repeat.
    /// </summary>
    public static DateTime? DividerDay(IReadOnlyList<ChatLine?> lines, int index)
    {
        if (lines is null || index < 0 || index >= lines.Count) return null;
        if (LocalDay(lines[index]) is not { } day) return null;

        for (int i = index - 1; i >= 0; i--)
        {
            if (LocalDay(lines[i]) is { } previous) return previous == day ? null : day;
        }

        return day;
    }

    /// <summary>
    /// The day as words the reader's language already has: weekday and date, plus the year once it
    /// is not this year. No "today"/"yesterday", which would need strings of their own in every
    /// language the app ships.
    /// </summary>
    public static string Label(DateTime day, DateTime today, CultureInfo culture)
    {
        culture ??= CultureInfo.CurrentUICulture;
        if (day.Year != today.Year) return day.ToString("D", culture);

        return day.ToString("dddd", culture) + ", " + day.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
    }
}

/// <summary>
/// Multi-binding converter for the chat list: values are the line and the list's ItemsSource.
/// Returns the divider label, or with ConverterParameter "visibility" whether to show it.
/// </summary>
public sealed class ChatDayDividerConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool wantsVisibility = parameter is string p && p.Equals("visibility", StringComparison.OrdinalIgnoreCase);
        DateTime? day = null;

        if (values is { Length: >= 2 } && values[0] is ChatLine line && values[1] is IEnumerable source)
        {
            var lines = source as IReadOnlyList<ChatLine?> ?? ToList(source);
            int index = IndexOf(lines, line);
            if (index >= 0) day = ChatDayDivider.DividerDay(lines, index);
        }

        if (wantsVisibility) return day is null ? Visibility.Collapsed : Visibility.Visible;
        return day is { } d ? ChatDayDivider.Label(d, DateTime.Today, CultureInfo.CurrentUICulture) : "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static List<ChatLine?> ToList(IEnumerable source)
    {
        var list = new List<ChatLine?>();
        foreach (var item in source) list.Add(item as ChatLine);
        return list;
    }

    private static int IndexOf(IReadOnlyList<ChatLine?> lines, ChatLine line)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (ReferenceEquals(lines[i], line)) return i;
        }
        return -1;
    }
}
