using System;
using System.Windows;
using System.Windows.Media;

namespace RustPlusDesk.Models;

/// <summary>One line in the inbox: a thread, seen from your side of it.</summary>
public sealed class SocialThread
{
    public string Id { get; init; } = "";

    /// <summary>pending, accepted or declined. Only direct threads carry one.</summary>
    public string State { get; init; } = "accepted";

    /// <summary>Whose thread this is, from your side — never yourself.</summary>
    public string CounterpartName { get; init; } = "—";

    public string? CounterpartId { get; init; }

    public string? AvatarUrl { get; init; }

    public bool IsOnline { get; init; }

    public int UnreadCount { get; init; }

    public DateTime? LastMessageAt { get; init; }

    /// <summary>True while this is a request you have not answered.</summary>
    public bool IsPending => State == "pending";

    /// <summary>True once you declined it — kept visible rather than deleted, and marked as such.</summary>
    public bool IsDeclined => State == "declined";

    public Visibility PendingBadge => IsPending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeclinedBadge => IsDeclined ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnreadBadge => UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string UnreadLabel => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public FontWeight NameWeight => UnreadCount > 0 ? FontWeights.Bold : FontWeights.Normal;

    public Brush StatusBrush => IsOnline
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A))
        : new SolidColorBrush(Color.FromRgb(0x9A, 0x54, 0x54));

    /// <summary>Short and local — an inbox is scanned, and a full timestamp is noise in a list.</summary>
    public string WhenLabel
    {
        get
        {
            if (LastMessageAt is not { } at) return "";

            var local = at.ToLocalTime();
            var age = DateTime.Now - local;

            return age.TotalHours < 24 ? local.ToString("HH:mm")
                 : age.TotalDays < 7 ? local.ToString("ddd")
                 : local.ToString("d MMM");
        }
    }
}

/// <summary>One message inside a thread.</summary>
public sealed class SocialMessage
{
    public string Id { get; init; } = "";

    public string Body { get; init; } = "";

    public string SenderName { get; init; } = "";

    public bool IsMine { get; init; }

    public DateTime? SentAt { get; init; }

    /// <summary>Your own messages sit right, everyone else's left — the usual chat shape.</summary>
    public HorizontalAlignment Side => IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public Brush Bubble => IsMine
        ? new SolidColorBrush(Color.FromRgb(0x1C, 0x4B, 0x82))
        : new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x35));

    public string TimeLabel => SentAt?.ToLocalTime().ToString("HH:mm") ?? "";
}
