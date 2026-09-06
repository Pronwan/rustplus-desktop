using System.Collections.Generic;
using System.Linq;
using Wpf.Ui.Controls;

namespace RustPlusDesk.Services.Sidebar;

/// <summary>
/// Static description of a rail tab's visuals and its backing <see cref="System.Windows.Controls.TabItem"/>.
/// Icons are either a Fluent <see cref="SymbolRegular"/> glyph or a custom geometry path (Death stats).
/// Names/help text prefer a localization key and fall back to a literal.
/// </summary>
public sealed record RailTabInfo(
    string Id,
    string TabItemName,
    string? NameKey,
    string NameFallback,
    string? HelpKey,
    string HelpFallback,
    SymbolRegular Symbol,
    string? GeometryPath = null,
    bool IsPlayersOptIn = false);

public static class RailCatalog
{
    /// <summary>Bumped when the shipped default layout changes so we can re-seed on upgrade.</summary>
    public const int CurrentDefaultVersion = 1;

    public const string DefaultFolderId = "folder-tools";

    /// <summary>Theme-aligned folder accent color (cyan accent).</summary>
    public const string DefaultFolderColor = "#FF60CDFF";

    private const string DeathStatsGeometry =
        "M12,2A9,9 0 0,0 3,11C3,14.03 4.53,16.82 7,18.47V22H9V19H11V22H13V19H15V22H17V18.46C19.47,16.81 21,14.03 21,11A9,9 0 0,0 12,2M8,11A2,2 0 0,1 10,13A2,2 0 0,1 8,15A2,2 0 0,1 6,13A2,2 0 0,1 8,11M16,11A2,2 0 0,1 18,13A2,2 0 0,1 16,15A2,2 0 0,1 14,13A2,2 0 0,1 16,11M12,14L13.5,17H10.5L12,14Z";

    /// <summary>All movable row-1 tabs, in their original shipped order.</summary>
    public static readonly IReadOnlyList<RailTabInfo> All = new List<RailTabInfo>
    {
        new("devices",       "DevicesTabItem",        "DevicesTab",        "Devices",             "UiMonitorAndControlPairedSmartDevices",                   "", SymbolRegular.Connector24),
        new("team",          "TabTeam",               "TeamTab",           "Team",                "UiTrackTeammatesAndSharedActivity",                       "", SymbolRegular.People24),
        new("clan",          "TabClan",               "ClanTab",           "Clan",                "UiTrackClanMembers",                                      "", SymbolRegular.Shield24),
        new("cameras",       "CamerasTabItem",        "CamerasTab",        "Cameras",             "UiViewAndControlPairedCCTVCameras",                       "", SymbolRegular.Video24),
        new("players",       "PlayersTabItem",        "Players",           "Players",             "UiBrowseOnlinePlayersAndTrackedTargets",                  "", SymbolRegular.Globe24, IsPlayersOptIn: true),
        new("notifications", "NotificationsTab",      "NotificationsTab",  "Notifications",       "UiReviewAlertsAndRecentServerEvents",                     "", SymbolRegular.Alert24),
        new("genetics",      "GeneticsLabTab",        null,                "Genetics Lab",        null, "Calculate plant genetics, crossbreeding, and recipes",     SymbolRegular.LeafTwo24),
        new("pwt",           "PlayerWipeTrackerTab",  null,                "Player Wipe Tracker", null, "Track teammate routes, activity, and history for the current wipe", SymbolRegular.History24),
        new("deathstats",    "DeathStatsTab",         null,                "Death stats",         null, "View death statistics for the current server",             SymbolRegular.Empty, GeometryPath: DeathStatsGeometry),
        new("raid",          "RaidCalculatorTab",     "UiRaidCalculator",  "Raid Calculator",     "UiCalculateRaidCostsAndRequiredMaterials",                "", SymbolRegular.Rocket24),
        new("recycler",      "RecyclerCalculatorTab", "Update700Title3",   "Recycler Calculator", "UiCalculateComponentYieldsForWildAndSafeZoneRecyclers",   "", SymbolRegular.Recycle32),
        new("console",       "ConsoleHelperTab",      "ConsoleHelperTitle","Console Helper",      "ConsoleHelperSubtitle",                                   "", SymbolRegular.WindowConsole20),
    };

    /// <summary>Utilities placed inside the default "Tools" folder on first upgrade.</summary>
    public static readonly string[] DefaultFolderTabIds =
        { "genetics", "pwt", "deathstats", "raid", "recycler", "console" };

    private static readonly Dictionary<string, RailTabInfo> _byId = All.ToDictionary(x => x.Id);

    public static RailTabInfo? Find(string id) => _byId.TryGetValue(id, out var v) ? v : null;

    public static bool IsKnownTab(string id) => _byId.ContainsKey(id);

    /// <summary>
    /// The shipped default layout: core tabs on top, all utilities collected into a
    /// collapsed "Tools" folder.
    /// </summary>
    public static RailLayoutData BuildDefault(string toolsFolderName)
    {
        var layout = new RailLayoutData { Version = CurrentDefaultVersion };
        foreach (var id in new[] { "devices", "team", "clan", "cameras", "players", "notifications" })
            layout.Nodes.Add(RailNodeData.Tab(id));
        layout.Nodes.Add(RailNodeData.Folder(DefaultFolderId, toolsFolderName, DefaultFolderColor, DefaultFolderTabIds));
        return layout;
    }

    /// <summary>
    /// Ensures every known tab appears exactly once in <paramref name="layout"/> (appending any
    /// missing ones at root) and drops ids no longer in the catalog. Keeps a user's arrangement
    /// intact across app updates that add or remove tabs.
    /// </summary>
    public static bool Reconcile(RailLayoutData layout)
    {
        bool changed = false;
        var seen = new HashSet<string>();

        // Drop unknown / duplicate ids from folders first.
        foreach (var node in layout.Nodes.Where(n => n.IsFolder))
        {
            int before = node.Children.Count;
            node.Children = node.Children.Where(id => IsKnownTab(id) && seen.Add(id)).ToList();
            if (node.Children.Count != before) changed = true;
        }

        // Then top-level tab nodes.
        var keptNodes = new List<RailNodeData>();
        foreach (var node in layout.Nodes)
        {
            if (node.IsTab)
            {
                if (node.TabId is null || !IsKnownTab(node.TabId) || !seen.Add(node.TabId))
                {
                    changed = true;
                    continue;
                }
            }
            keptNodes.Add(node);
        }
        layout.Nodes = keptNodes;

        // Append any catalog tabs not yet present anywhere.
        foreach (var info in All)
        {
            if (!seen.Contains(info.Id))
            {
                layout.Nodes.Add(RailNodeData.Tab(info.Id));
                seen.Add(info.Id);
                changed = true;
            }
        }

        // Remove empty folders and reset custom colors to theme default.
        int foldersBefore = layout.Nodes.Count;
        layout.Nodes = layout.Nodes.Where(n => !(n.IsFolder && n.Children.Count == 0)).ToList();
        if (layout.Nodes.Count != foldersBefore) changed = true;

        foreach (var node in layout.Nodes.Where(n => n.IsFolder))
        {
            if (node.ColorHex != DefaultFolderColor)
            {
                node.ColorHex = DefaultFolderColor;
                changed = true;
            }
        }

        return changed;
    }
}
