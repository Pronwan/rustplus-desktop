using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models
{
    /// <summary>
    /// One tile on the mini-map's command dock.
    ///
    /// <see cref="Kind"/> is a string rather than an enum on purpose: a layout saved by a newer
    /// build can be read by an older one, which drops the tiles it cannot render instead of
    /// failing to parse the file and wiping the whole dock.
    /// </summary>
    public sealed class CommandDockTile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Clock | Device | TeamChat | ClanChat | Event | Rule</summary>
        public string Kind { get; set; } = CommandDockTileKinds.Clock;

        public int Col { get; set; }
        public int Row { get; set; }
        public int ColSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;

        /// <summary>Device tiles: the paired entity this tile controls or reports on.</summary>
        public uint EntityId { get; set; }

        /// <summary>
        /// The server this tile belongs to, as "{host}-{port}", or null for one that belongs
        /// everywhere.
        ///
        /// Only device tiles get one: an entity id means nothing on another server, so a switch
        /// from last wipe would sit there as a permanent "not paired". Tiles for a server other
        /// than the current one are hidden rather than deleted, and since two servers' devices
        /// are never on screen together they are free to hold the same cells — go back and the
        /// arrangement is as it was.
        /// </summary>
        public string? ServerKey { get; set; }

        /// <summary>Rule tiles: the Logic Engine rule this tile launches.</summary>
        public string? RuleId { get; set; }

        /// <summary>Event tiles: cargo | deepsea | oilrig | heli | chinook | vendor.</summary>
        public string? EventKey { get; set; }

        /// <summary>Clock tiles: 0 digital, 1 analog, 2 Rust style.</summary>
        public int ClockStyle { get; set; }

        /// <summary>Clock tiles: append the time left until sunrise or sunset.</summary>
        public bool ClockShowDayNight { get; set; } = true;

        /// <summary>Clock tiles: 3:20 PM rather than 15:20.</summary>
        public bool Clock12Hour { get; set; }

        /// <summary>
        /// Device tiles: show the icon chosen in the device list instead of the device's name.
        ///
        /// The icon is what the device is recognised by — it is the same picture as in the list,
        /// and at one cell there is room for it and a state word, but not for a name as well.
        /// </summary>
        public bool ShowDeviceIcon { get; set; } = true;

        /// <summary>Chat tiles: shorten long names so the message still fits on one line.</summary>
        public bool ChatAbbreviateNames { get; set; } = true;

        /// <summary>
        /// Discord tiles: which configured notification channel to post into — "chat", "events"
        /// or "shop". Null follows whichever is configured first, so a tile keeps working when
        /// the channel it was pointed at is removed.
        /// </summary>
        public string? DiscordChannel { get; set; }

        /// <summary>Discord tiles: 0 none, 1 @here, 2 @everyone.</summary>
        public int DiscordMention { get; set; }

        // ── Appearance ──────────────────────────────────────────────────────
        //
        // All three are null until the tile's own settings are touched, and fall back to the
        // dock's defaults. Two places that set the same thing drift apart — one says 70% while
        // the tile shows 40% and nobody can tell which wins — so the global values are
        // defaults rather than a second switch, and "back to global" is putting null back.

        /// <summary>Background and border opacity, 0 to 1. Text is never faded.</summary>
        public double? Opacity { get; set; }

        /// <summary>Multiplier on every font size in the tile.</summary>
        public double? FontScale { get; set; }

        /// <summary>A key from <see cref="CommandDockTextColors"/>, or null for the theme's own.</summary>
        public string? TextColorKey { get; set; }
    }

    /// <summary>
    /// The text colours a tile can be set to.
    ///
    /// Deliberately few, and each one picked to hold up on both a bright desert and a night-time
    /// screen — a tile turned fully transparent sits directly on the game, and the theme's grey
    /// stops being readable the moment its background goes.
    /// </summary>
    public static class CommandDockTextColors
    {
        public const string Auto = "auto";
        public const string White = "white";
        public const string Black = "black";
        public const string Cyan = "cyan";
        public const string Amber = "amber";
        public const string Red = "red";
        public const string Green = "green";

        public static readonly string[] All =
            { Auto, White, Black, Cyan, Amber, Red, Green };
    }

    public static class CommandDockTileKinds
    {
        /// <summary>
        /// The mini-map. One per dock, and the only tile whose size is free rather than derived
        /// from its cell span — but otherwise an ordinary occupant of the grid.
        /// </summary>
        public const string Map = "Map";

        /// <summary>
        /// The map tile's id is fixed rather than a fresh Guid.
        ///
        /// Its element is the one declared in XAML and survives every rebuild, so its mouse
        /// handlers are attached once and capture this id. A new id per map would leave those
        /// handlers pointing at a tile that no longer exists.
        /// </summary>
        public const string MapTileId = "map";

        public const string Clock = "Clock";
        public const string Device = "Device";
        public const string TeamChat = "TeamChat";
        public const string ClanChat = "ClanChat";
        public const string Event = "Event";
        public const string Rule = "Rule";
        public const string Session = "Session";
        public const string Discord = "Discord";
    }

    /// <summary>
    /// The dock's saved arrangement. Positions are grid cells whose origin is the map tile's
    /// top-left corner, so the dock keeps its shape when the map is resized: only the number of
    /// cells the map covers changes, and the auto-arrange pushes tiles out of the way.
    /// </summary>
    public sealed class CommandDockLayout
    {
        public List<CommandDockTile> Tiles { get; set; } = new();

        /// <summary>
        /// Where a newly added tile goes: below the map by default, beside it when set.
        ///
        /// Only the auto-placement follows this. Dragging puts a tile anywhere, including left
        /// of or above the map — that is how a bar along the top or the left edge of the screen
        /// gets built, and the window resizes around whatever shape comes out.
        /// </summary>
        public bool GrowRight { get; set; }

        /// <summary>
        /// Where the dock last sat on screen. Global rather than per server: it is a place on the
        /// user's desk, not a property of the server they happen to be on. Null until the dock
        /// has been moved once, which is what keeps the first-run position up to the caller.
        /// </summary>
        public double? WindowLeft { get; set; }

        public double? WindowTop { get; set; }

        /// <summary>
        /// True once the map has been taken off the dock on purpose.
        ///
        /// Needed because "no map tile in the list" also describes every layout written before
        /// the map was a tile at all, and those have to get one back. Without this flag, removing
        /// the map would look identical to an old layout and it would reappear on the next start.
        /// </summary>
        public bool MapRemoved { get; set; }

        /// <summary>
        /// While locked, hovering a tile offers nothing to drag, resize or delete — the dock is
        /// only used, not rearranged.
        ///
        /// Locked is the resting state and the saved default, because arranging happens once and
        /// using happens every session. A hover-armed delete button is fine for the minute you
        /// are building the dock and a hazard for every hour after it.
        /// </summary>
        public bool Locked { get; set; } = true;

        // ── Appearance defaults ─────────────────────────────────────────────
        // What a tile uses until it is given its own value.

        public double DefaultOpacity { get; set; } = 1.0;

        public double DefaultFontScale { get; set; } = 1.0;

        public string DefaultTextColorKey { get; set; } = CommandDockTextColors.Auto;

        /// <summary>Cell size and gap in device-independent pixels.</summary>
        public const double CellSize = 74;
        public const double CellGap = 8;

        public static double CellsToPixels(int cells) =>
            cells <= 0 ? 0 : cells * CellSize + (cells - 1) * CellGap;

        public static double CellOffset(int index) => index * (CellSize + CellGap);

        /// <summary>How many cells a free-size element such as the map tile covers.</summary>
        public static int PixelsToCells(double pixels) =>
            Math.Max(1, (int)Math.Ceiling((pixels + CellGap) / (CellSize + CellGap)));
    }
}
