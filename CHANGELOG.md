# Changelog — Team chat commands & presence alerts

This PR adds a few team-chat commands and presence announcements, and fixes a couple of chat-reliability issues. Everything is built to be **API-friendly** (single message per command, throttled, no extra polling) — directly addressing the concern that shop-style chat commands tend to flood the API and get players kicked.

## Added
- **`!shop <item>`** — searches the map's vending machines and replies with grid, price and stock.
  - Reads the **already-cached** shop list (`GetLastShopsList()`) — **no extra map/marker polling**.
  - Replies with the **top 3 matches** (in-stock first, then cheapest) as up to 3 separate messages, spaced by the chat-response delay so the API is never flooded; the last line shows a `(+N more)` overflow indicator.
  - **Filters out junk barter trades**: when real Scrap prices exist, odd trades priced in another item (e.g. "Bone Fragments for 1 LR-300") are dropped — they previously ranked as "cheapest" because `1 < 25` across different currencies. Barter is shown only when nothing is priced in Scrap.
- **`!small` / `!large`** — individual Small/Large Oil Rig crate timers (the existing `!oilrig` was refactored into a shared `BuildSingleRigStatus` helper).
- **`!pos <name>`** — reports a teammate's current grid (exact-then-partial name match).
- **Team presence announcements** (gated by the existing "Chat Announce" master toggle):
  - Teammate **joined** / **left** the team.
  - Teammate went **AFK** — e.g. "X is now AFK for more than 5m". Announced for **anyone in the team, including yourself**.
  - Teammate **back from AFK** — e.g. "X came back from AFK, was AFK for 12m".
  - Teammate **came online** — message now appends how long they were offline (e.g. "X came online @ G12 (was offline 2h 5m)").
  - Teammate **died** — message now appends how long they were alive (e.g. ":skull: X is dead @ G12, was alive for 1h 25m"). The clock starts on respawn, survives going offline/online (sleeping ≠ dying), and is tracked regardless of the announce toggle.
  - Smart handling of **your own** team switches:
    - You join someone else's team → announces **only you** once (not every existing member).
    - People join **your** team (you're leader) → announces each.
    - You leave / the team dissolves (roster collapses to just you) → **suppressed** (no spam).
  - Multiple announcements are **spaced 2.5s apart** so they never burst the API.
- **Configurable AFK threshold** — new per-server setting (default 5 min, range 1–60) replacing the hardcoded value, with a selector in the Chat Commands panel.
- Config rows for the new commands in the Chat Commands panel; join/leave/AFK (and back-from-AFK) templates are editable in the Custom Alerts window.
- In-game emoji shortcodes on alerts: `:skull:` (death), `:wave:` (join/leave), `:vending.machine:` (shop alerts).
- **Persistent event logs** (under `%LOCALAPPDATA%\RustPlusDesk\logs\`, written regardless of the Chat Alerts toggle so the record survives an app crash):
  - `timeline.log` — append-only, crash-safe history of every event (offline/online, AFK/back, death/respawn, oil-rig crate, cargo, heli, vendor, deep sea), one timestamped line each.
  - `events.json` — latest state per event type (keyed per server, overwritten as new events arrive) and **loaded on startup**. On reconnect it rehydrates the "X ago" timers for cargo/heli/vendor/deep sea so those `!` commands still answer correctly after a restart.

## Fixed
- **AFK announcements never fired for your own account** — the AFK alert was hard-skipped for the local player (`SteamId == _mySteamId`), so going AFK yourself produced no message. Now any team member (you included) is announced when Chat Alerts is on, matching how death/online alerts already behave.
- **`!afk` command** — now respects the name-abbreviation setting (was printing raw names) and caps its reply at Rust's 128-char team-chat limit, so a long AFK list no longer silently fails to send.
- **Offline-duration tracking** — a member's `OfflineSince` timestamp was only recorded when Chat Alerts happened to be enabled, so the "(was offline X)" suffix could be missing if alerts were turned on only after they went offline. The timing is now tracked independently of the announce toggle.
- **Discord webhook flooding / rate-limits** — webhook forwarding previously created a fresh `HttpClient` per message and ignored HTTP 429s. It now routes through a serialized, rate-limited queue: one shared client, one in-flight POST at a time, a minimum gap between posts (~40/min ceiling), and it honors Discord's `Retry-After` (header and JSON body) with a single retry.
- **Chat delivery confirmation** — Rust rewrites `:shortcode:` emoji into glyphs in its echo, which broke the exact-text match used to confirm a sent message, causing wait-timeouts and **duplicate re-sends**. Both sides are now normalized before matching.
- **Instant command responses** — removed an artificial pre-send delay so single replies go out immediately (the delay setting still spaces multi-line replies).

## Notes
- New strings added to `Resources.resx` and `Resources.en-US.resx`.
- No changes to polling cadence or existing features; all additions are opt-in via the chat-commands toggle.
