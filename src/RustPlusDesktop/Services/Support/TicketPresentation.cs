using System;
using System.Globalization;
using System.Linq;

namespace RustPlusDesk.Services.Support;

/// <summary>The colour family a ticket's status pill uses. The pill always carries a text label too.</summary>
public enum TicketTone
{
    /// <summary>Open or being worked on: the app accent.</summary>
    Accent,

    /// <summary>Waiting on the person who filed it.</summary>
    Warning,

    /// <summary>Resolved.</summary>
    Success,

    /// <summary>Closed or unknown.</summary>
    Neutral,
}

/// <summary>
/// How the Tickets screen presents a ticket: status tone, reference tag and timestamps. Pure
/// functions, so the rules are testable without UI.
/// </summary>
public static class TicketPresentation
{
    public static TicketTone ToneFor(string? status) => status switch
    {
        "open" or "in_progress" => TicketTone.Accent,
        "awaiting_user" => TicketTone.Warning,
        "resolved" => TicketTone.Success,
        _ => TicketTone.Neutral,
    };

    /// <summary>Resolved and closed tickets take no more replies from the filer.</summary>
    public static bool IsLocked(string? status) => status is "closed" or "resolved";

    /// <summary>
    /// Short, stable tag from the ticket id ("#3F9A1C"). Subjects are free text and often repeat
    /// ("Help me"), so the list needs something that tells two tickets apart. Taken from the end of
    /// the id: ids are time-ordered, so tickets opened together share a prefix.
    /// </summary>
    public static string Reference(string? id)
    {
        var chars = new string((id ?? "").Where(char.IsLetterOrDigit).TakeLast(6).ToArray());
        return chars.Length == 0 ? "" : "#" + chars.ToUpperInvariant();
    }

    /// <summary>
    /// Compact time for a list row: the time today, the weekday within the last week, the month and
    /// day this year, and the short date before that.
    /// </summary>
    public static string ListTime(DateTimeOffset? at, DateTimeOffset now, CultureInfo culture)
    {
        if (at is not { } value) return "";
        var local = value.ToOffset(now.Offset);
        var days = (now.Date - local.Date).Days;

        if (days <= 0) return local.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        if (days < 7) return local.ToString("ddd", culture);
        if (local.Year == now.Year) return local.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
        return local.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
    }

    /// <summary>Timestamp for a message: the time today, otherwise the date and the time.</summary>
    public static string MessageTime(DateTimeOffset? at, DateTimeOffset now, CultureInfo culture)
    {
        if (at is not { } value) return "";
        var local = value.ToOffset(now.Offset);
        var time = local.ToString(culture.DateTimeFormat.ShortTimePattern, culture);

        if (local.Date == now.Date) return time;
        var date = local.Year == now.Year
            ? local.ToString(culture.DateTimeFormat.MonthDayPattern, culture)
            : local.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
        return $"{date}, {time}";
    }

    /// <summary>The full date and time, for tooltips.</summary>
    public static string FullTime(DateTimeOffset? at, DateTimeOffset now, CultureInfo culture)
        => at is { } value ? value.ToOffset(now.Offset).ToString("f", culture) : "";
}
