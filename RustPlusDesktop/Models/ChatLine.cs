using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace RustPlusDesk.Models;

/// <summary>
/// Sender information with staff roles for badging.
/// </summary>
public sealed class ChatSender
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string DisplayName { get; init; } = "—";
    public string? AvatarUrl { get; init; }
    public string? SteamId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Information for rendering a staff role badge pill in the UI.
/// </summary>
public sealed class RoleBadgeInfo
{
    public string RoleKey { get; init; } = "";
    public string DisplayText { get; init; } = "";
    public Brush BackgroundBrush { get; init; } = Brushes.Transparent;
    public Brush ForegroundBrush { get; init; } = Brushes.White;
    public Brush BorderBrush { get; init; } = Brushes.Transparent;
}

/// <summary>
/// Target of a moderation sanction.
/// </summary>
public sealed class SanctionTarget
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public string? SteamId { get; init; }
}

/// <summary>
/// Moderator who issued or lifted a sanction.
/// </summary>
public sealed class SanctionModerator
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// System sanction broadcast event payload for timeout, ban, or lifted actions.
/// </summary>
public sealed class SystemSanctionEvent
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "system_sanction";
    public string Action { get; init; } = "issued"; // "issued" | "lifted"
    public string Kind { get; init; } = "timeout"; // "timeout" | "ban"
    public string Scope { get; init; } = "chat"; // "chat" | "all" | "dm"
    public string Reason { get; init; } = "";
    public string? Duration { get; init; } // e.g. "1 hour", "24 hours", "7 days", or null for bans
    public DateTime? ExpiresAt { get; init; }
    public string? ExpiresAtIso { get; init; }
    public SanctionTarget? Target { get; init; }
    public SanctionModerator? Moderator { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CreatedAtIso { get; init; }

    public bool IsLifted => string.Equals(Action, "lifted", StringComparison.OrdinalIgnoreCase);
    public bool IsTimeout => !IsLifted && string.Equals(Kind, "timeout", StringComparison.OrdinalIgnoreCase);
    public bool IsBan => !IsLifted && string.Equals(Kind, "ban", StringComparison.OrdinalIgnoreCase);

    public bool HasDuration => !string.IsNullOrWhiteSpace(Duration) && IsTimeout;
    public string DurationBadgeText => !string.IsNullOrWhiteSpace(Duration) ? $"[{Duration}]" : "";
    public Visibility DurationBadgeVisibility => HasDuration ? Visibility.Visible : Visibility.Collapsed;

    public string TargetDisplayName => Target?.DisplayName ?? Target?.Name ?? "—";
    public string TargetSteamId => Target?.SteamId ?? "";
    public string TargetSteamIdFormatted => !string.IsNullOrWhiteSpace(Target?.SteamId) ? $"({Target.SteamId})" : "";
    public bool HasTargetSteamId => !string.IsNullOrWhiteSpace(Target?.SteamId);

    public string ModeratorDisplayName => Moderator?.DisplayName ?? Moderator?.Name ?? "Moderator";
    public RoleBadgeInfo? ModeratorRoleBadge => Moderator?.Roles != null ? ChatLine.GetBadgeForRoles(Moderator.Roles) : null;
    public bool HasModeratorRoleBadge => ModeratorRoleBadge != null;

    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
    public string TimeLabel => CreatedAt.ToLocalTime().ToString("HH:mm");

    public string HeaderTitle => IsLifted
        ? "SANCTION LIFTED"
        : IsBan
            ? "PERMANENT BAN"
            : "CHAT TIMEOUT";

    // Pre-frozen Brushes for UI rendering
    private static readonly Brush TimeoutBg = CreateFrozenBrush(0x1F, 0xF5, 0x9E, 0x0B);
    private static readonly Brush TimeoutBorder = CreateFrozenBrush(0x66, 0xF5, 0x9E, 0x0B);
    private static readonly Brush TimeoutHdrBg = CreateFrozenBrush(0x33, 0xF5, 0x9E, 0x0B);
    private static readonly Brush TimeoutHdrBorder = CreateFrozenBrush(0xFF, 0xF5, 0x9E, 0x0B);
    private static readonly Brush TimeoutHdrFg = CreateFrozenBrush(0xFF, 0xFB, 0xBF, 0x24);

    private static readonly Brush BanBg = CreateFrozenBrush(0x1F, 0xEF, 0x44, 0x44);
    private static readonly Brush BanBorder = CreateFrozenBrush(0x66, 0xEF, 0x44, 0x44);
    private static readonly Brush BanHdrBg = CreateFrozenBrush(0x33, 0xEF, 0x44, 0x44);
    private static readonly Brush BanHdrBorder = CreateFrozenBrush(0xFF, 0xEF, 0x44, 0x44);
    private static readonly Brush BanHdrFg = CreateFrozenBrush(0xFF, 0xF8, 0x71, 0x71);

    private static readonly Brush LiftedBg = CreateFrozenBrush(0x1F, 0x10, 0xB9, 0x81);
    private static readonly Brush LiftedBorder = CreateFrozenBrush(0x66, 0x10, 0xB9, 0x81);
    private static readonly Brush LiftedHdrBg = CreateFrozenBrush(0x33, 0x10, 0xB9, 0x81);
    private static readonly Brush LiftedHdrBorder = CreateFrozenBrush(0xFF, 0x10, 0xB9, 0x81);
    private static readonly Brush LiftedHdrFg = CreateFrozenBrush(0xFF, 0x34, 0xD3, 0x99);

    public Brush CardBackgroundBrush => IsLifted ? LiftedBg : IsBan ? BanBg : TimeoutBg;
    public Brush CardBorderBrush => IsLifted ? LiftedBorder : IsBan ? BanBorder : TimeoutBorder;
    public Brush HeaderBackgroundBrush => IsLifted ? LiftedHdrBg : IsBan ? BanHdrBg : TimeoutHdrBg;
    public Brush HeaderBorderBrush => IsLifted ? LiftedHdrBorder : IsBan ? BanHdrBorder : TimeoutHdrBorder;
    public Brush HeaderForegroundBrush => IsLifted ? LiftedHdrFg : IsBan ? BanHdrFg : TimeoutHdrFg;

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Active slow mode cooldown event payload.
/// </summary>
public sealed class ChatSlowModeEvent
{
    public int Seconds { get; init; }
    public string? UpdatedById { get; init; }
    public string? UpdatedByName { get; init; }
}

/// <summary>
/// One line in the public room (either a chat message or a system sanction alert).
/// </summary>
public sealed class ChatLine
{
    public string Id { get; init; } = "";

    public string Body { get; init; } = "";

    public string? SenderId { get; init; }

    public string SenderName { get; init; } = "—";

    public string? AvatarUrl { get; init; }

    public string? SteamId { get; init; }

    public bool IsSupporter { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public DateTime? SentAt { get; init; }

    public string? SentAtIso { get; init; }

    public string TimeLabel => SentAt?.ToLocalTime().ToString("HH:mm") ?? "";

    public bool ShowHeader { get; set; } = true;

    public Thickness GroupMargin => ShowHeader ? new Thickness(0, 10, 0, 2) : new Thickness(0, 1, 0, 1);

    public Brush SenderNameBrush => IsSupporter ? SupporterNameBrush : DefaultSenderBrush;

    private static readonly Brush SupporterNameBrush = CreateFrozenBrush(0xFF, 0xFF, 0xD1, 0x66); // Warm gold
    private static readonly Brush DefaultSenderBrush = CreateFrozenBrush(0xFF, 0x60, 0xCD, 0xFF);   // Crisp vibrant cyan

    public bool IsSystemSanction { get; init; }

    public SystemSanctionEvent? SanctionEvent { get; init; }

    public RoleBadgeInfo? RoleBadge => GetBadgeForRoles(Roles);

    public bool HasRoleBadge => RoleBadge != null;

    public static ChatLine FromSanction(SystemSanctionEvent sanction)
    {
        return new ChatLine
        {
            Id = sanction.Id,
            Body = sanction.Reason,
            SenderName = sanction.ModeratorDisplayName,
            SentAt = sanction.CreatedAt,
            SentAtIso = sanction.CreatedAtIso,
            IsSystemSanction = true,
            SanctionEvent = sanction,
        };
    }

    private static readonly RoleBadgeInfo SuperAdminBadge = new()
    {
        RoleKey = "super_admin",
        DisplayText = "SUPER ADMIN",
        BackgroundBrush = CreateFrozenBrush(0x22, 0xEF, 0x44, 0x44),
        BorderBrush = CreateFrozenBrush(0x55, 0xEF, 0x44, 0x44),
        ForegroundBrush = CreateFrozenBrush(0xFF, 0xFA, 0x8A, 0x8A),
    };

    private static readonly RoleBadgeInfo AdminBadge = new()
    {
        RoleKey = "admin",
        DisplayText = "ADMIN",
        BackgroundBrush = CreateFrozenBrush(0x22, 0xEF, 0x44, 0x44),
        BorderBrush = CreateFrozenBrush(0x55, 0xEF, 0x44, 0x44),
        ForegroundBrush = CreateFrozenBrush(0xFF, 0xFA, 0x8A, 0x8A),
    };

    private static readonly RoleBadgeInfo ModBadge = new()
    {
        RoleKey = "moderator",
        DisplayText = "MOD",
        BackgroundBrush = CreateFrozenBrush(0x22, 0x3B, 0x82, 0xF6),
        BorderBrush = CreateFrozenBrush(0x55, 0x3B, 0x82, 0xF6),
        ForegroundBrush = CreateFrozenBrush(0xFF, 0x93, 0xC5, 0xFD),
    };

    private static readonly RoleBadgeInfo CmBadge = new()
    {
        RoleKey = "community_manager",
        DisplayText = "CM",
        BackgroundBrush = CreateFrozenBrush(0x22, 0x8B, 0x5C, 0xF6),
        BorderBrush = CreateFrozenBrush(0x55, 0x8B, 0x5C, 0xF6),
        ForegroundBrush = CreateFrozenBrush(0xFF, 0xC4, 0xB5, 0xFD),
    };

    public static RoleBadgeInfo? GetBadgeForRoles(IReadOnlyList<string>? roles)
    {
        if (roles == null || roles.Count == 0) return null;

        if (roles.Any(r => string.Equals(r, "super_admin", StringComparison.OrdinalIgnoreCase)))
            return SuperAdminBadge;

        if (roles.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)))
            return AdminBadge;

        if (roles.Any(r => string.Equals(r, "moderator", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "mod", StringComparison.OrdinalIgnoreCase)))
            return ModBadge;

        if (roles.Any(r => string.Equals(r, "community_manager", StringComparison.OrdinalIgnoreCase) || string.Equals(r, "cm", StringComparison.OrdinalIgnoreCase)))
            return CmBadge;

        // Do not show badges for regular users or non-staff roles
        return null;
    }

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Why the text box is closed, and until when.</summary>
public sealed record ChatSanction(string Kind, string Reason, DateTime? ExpiresAt);

public sealed record ChatSnapshot(
    List<ChatLine> Lines,
    ChatSanction? Sanction,
    int SlowModeSeconds = 0,
    bool Ok = true,
    // Whether this account may use the supporters' room. Carried on every read of either room,
    // so the tab knows what to draw without a request of its own.
    bool SupporterRoom = false,
    int MaxLength = 128);

