# Changelog — Team chat commands & presence alerts

This PR adds a few team-chat commands and presence announcements, and fixes a couple of chat-reliability issues. Everything is built to be **API-friendly** (single message per command, throttled, no extra polling) — directly addressing the concern that shop-style chat commands tend to flood the API and get players kicked.

## Added
- **`!shop <item>`** — searches the map's vending machines and replies with grid, price and stock.
  - Reads the **already-cached** shop list (`GetLastShopsList()`) — **no extra map/marker polling**.
  - Sends **exactly one** chat message: best matches (in-stock first, then cheapest) packed into a single ~128-char line with a `+N` overflow indicator.
- **`!small` / `!large`** — individual Small/Large Oil Rig crate timers (the existing `!oilrig` was refactored into a shared `BuildSingleRigStatus` helper).
- **`!pos <name>`** — reports a teammate's current grid (exact-then-partial name match).
- **Team presence announcements** (gated by the existing "Chat Announce" master toggle):
  - Teammate **joined** / **left** the team.
  - Teammate went **AFK** — message now includes how long they've been idle (e.g. "X is now AFK (idle 5m)").
  - Teammate **back from AFK** — new announcement reporting how long they were AFK (e.g. "X is back (was AFK for 12m)").
  - Teammate **came online** — message now appends how long they were offline (e.g. "X came online @ G12 (was offline 2h 5m)").
  - Smart handling of **your own** team switches:
    - You join someone else's team → announces **only you** once (not every existing member).
    - People join **your** team (you're leader) → announces each.
    - You leave / the team dissolves (roster collapses to just you) → **suppressed** (no spam).
  - Multiple announcements are **spaced 2.5s apart** so they never burst the API.
- **Configurable AFK threshold** — new per-server setting (default 5 min, range 1–60) replacing the hardcoded value, with a selector in the Chat Commands panel.
- Config rows for the new commands in the Chat Commands panel; join/leave/AFK (and back-from-AFK) templates are editable in the Custom Alerts window.
- In-game emoji shortcodes on alerts: `:skull:` (death), `:wave:` (join/leave), `:vending.machine:` (shop alerts).

## Fixed
- **Chat delivery confirmation** — Rust rewrites `:shortcode:` emoji into glyphs in its echo, which broke the exact-text match used to confirm a sent message, causing wait-timeouts and **duplicate re-sends**. Both sides are now normalized before matching.
- **Instant command responses** — removed an artificial pre-send delay so single replies go out immediately (the delay setting still spaces multi-line replies).

## Notes
- New strings added to `Resources.resx` and `Resources.en-US.resx`.
- No changes to polling cadence or existing features; all additions are opt-in via the chat-commands toggle.
