# Migration status

Started 2026-10-16 from `beta` at `e1bf6594`. This is the first independently buildable migration slice.
All migration source lives inside `Avalonia/`. The existing Windows app remains unchanged.

**Acceptance scope:** every shipped feature must be available in Avalonia, with nearly the same design.
The initial saved-server preview is groundwork, not fulfillment of that scope. No feature may be
dropped or replaced by a reduced workflow without Jawad's explicit instruction.

## Implemented

- Independent solution, .NET 10 SDK/build settings, public NuGet sources, local branding, license, and provenance.
- Portable Core with the unchanged server-clock algorithm and its five existing calibration tests.
- Infrastructure adapter for reading legacy `profiles.json`: server names, hosts, ports, default port,
  blank-name fallback, host/port validation, cancellation, and no writes.
- Avalonia shell, feature view model using CommunityToolkit.Mvvm, compiled bindings, shared styles,
  shared status control, native profile-file picker, refresh, loading/empty/error states.
- Async command coordination prevents simultaneous refresh/file-picker loads. Errors do not echo JSON or secrets.
- Tests for legacy data compatibility, file preservation, invalid data, credentials excluded from
  display models, cancellation, state notifications, picker cancellation, and command concurrency.
- Standalone workflow definition for Windows/Linux/macOS builds, tests, and preview artifact generation.
  It becomes active only when this folder is a repository root; no workflow runs or releases are claimed here.
- Existing application palette and Fluent accent ported from source; complete screen visual parity is pending.
- Source snapshot of 5,988 source candidates (including embedded web/vendor sources), 83 XAML candidates,
  and 963 action-bearing elements. See `reference/UI-CHECKLIST.md` and `reference/manifest.json`.
  This captures source candidates, not exhaustive runtime reachability or all code-only features.
- Updated appearance requirement: Windows 11 Fluent on Windows, and current-OS design conventions elsewhere.
  Windows now uses **FluentAvaloniaUI 3.1.0**, with WinUI-style controls, system font/accent, on **Avalonia 12.1.4**.
  Linux/macOS retain a built-in Fluent base with separate palette/proportion files; native visual parity is pending.
  System light/dark mode and native window decorations are enabled. No forced dark-only theme remains.
- Server selection uses a stable hash of the legacy profile identity; account IDs and pairing tokens
  are not placed in display models. Connection/probe credential reads and lossless profile writes stay in Infrastructure.
- Authenticated Rust+ transport using the reference app's exact local companion-socket package.
  Companion socket is tried first when server ID/login are available, followed by direct fallback.
  Every failed attempt is disposed. The retired `/game` relay is never used.
- Manual connect/disconnect, live population/queue/time, observed day/night clock, paused time,
  independent one-second clock updates, and 2–60 second exponential recovery after a dropped session.
- Socket-disconnect signals immediately cancel the selected session's polling, status reads, device
  work and clock before disposal/recovery. Each watcher is canceled/awaited during teardown and cannot
  affect a replacement session. Poll failures remain a fallback; live-server acceptance is pending.
- Re-pair, companion sign-in, invalid credentials and unavailable profiles have distinct safe messages.
- Switching server, refreshing the source and application shutdown cancel the old session and await
  its cleanup. Late connection/status completions cannot overwrite the newly selected server.
- Paired-device trees preserve aliases, original kind spellings and nested groups without trusting saved live state.
  Confirmed switch ON/OFF, deduplicated nested group commands, partial-failure reporting, alarm state and storage
  snapshots are implemented. Server selection/disconnection cancels work and clears old state. Broadcasts trigger
  authoritative reads rather than treating partial storage broadcasts as empty containers; 30-second refresh backs them up.
- Storage shows packaged item names, quantities, blueprint flags, capacity and upkeep. Missing upkeep dates remain
  unknown. A per-session gate paces status/device requests to 25/second. Device failures never request a server re-pair.
- Server editing preserves all unknown JSON fields, credentials, nested devices and other profiles. Writes check
  the loaded file revision, validate identity uniqueness, flush a temporary file and atomically replace with an exact
  backup. In-place writers are excluded while saving; unrelated atomic-renaming applications still require coordination.
  Edited-address probes bypass companion sockets because those ignore host/port, check nested devices and dispose.
  Changed endpoints reload/reselect and reconnect; canceled edits do not save. Shutdown awaits active probes.
- Account sign-in/status and current/all-device sign-out use platform-neutral contracts, a native browser,
  explicit sign-out confirmation, safe errors and unknown-field-preserving private credential writes.
  Remote success followed by a local conflict is reported as partial completion without overwriting newer data.
- Owned FCM listener with 60-second heartbeat, three-minute inactivity watchdog, 3–60 second reconnect,
  duplicate-ID suppression and the reference 20-second duplicate-pairing guard. Token rotation is never
  suppressed. Current-device ID history is bounded to 1,000 entries, privately persisted and seeded from
  the legacy ID file when needed. Old socket callbacks cannot affect a replacement session.
- Incoming server/device pairings use a shared serialized profile writer, exact backups and private file
  permissions. Stable server IDs, fresh pairing credentials, nested devices/groups/aliases and unknown fields
  survive re-pairing. Legacy port/address changes use up to five direct entity probes per candidate and the
  reference single-host fallback; ambiguous matches are reported without changing profiles.
- Alarm/chat/server metadata backfills missing server IDs/descriptions without changing credentials or
  replacing custom descriptions. Pairing refreshes reselect a moved server and reconnect an active session.
- Account panel displays the newest 100 pairing/alarm/chat/offline-death pushes in memory, with normalized
  sent time shown locally. Source changes, sign-in/out and shutdown await listener disposal; closing the
  account panel keeps listening. Full notification-center persistence/audio/settings are still pending.
- Device-name inference distinguishes `Smart Switch` from the standalone `TC` abbreviation; the old broad
  substring match could misclassify a switch when numeric type information was missing.
- Saved-account startup and successful sign-in start the listener without opening Steam automatically.
  Missing/rejected credentials prompt sign-in. Startup/source changes/shutdown cancel and await old work.
- Push delivery checks correlate a random test title with the selected source's listener. Ticket/receipt
  acknowledgements alone never prove delivery. A rejected token or accepted-but-missing test permits one
  renewal with the saved token, listener restart and one retest. Inconclusive network/provider failures
  do not renew. Manual delivery checks/renewal and bounded cancellation use the same flow.
- Account dialog keeps its Close action visible below the scrollable content at the minimum window size.
- Local device/group organizer supports aliases, grouping a selected subtree, moving between groups/root,
  and sibling ordering. Positional device keys are valid only with the full-file revision, so duplicate
  entity IDs do not rename the wrong alias. Whole JSON nodes and empty-group metadata are preserved.
  Closing/shutdown awaits writes; changed files are rejected. Closing after a save reloads the selected
  tree and reconnects an active session. This does not implement cloud sync, drag gestures or deletion cleanup.
- Smart-alarm in-game titles can be edited/cleared in the organizer. The writer validates the saved
  device kind, trims blank titles to null and preserves unknown settings. Cloud title synchronization is pending.
- Offline recycler workspace with all 776 reference inputs, 796 bundled input/output icons, catalog
  precedence, guaranteed/chance comparisons, tick-based processing times, search/categories, paging,
  fill/clear, numeric and click/wheel/Shift input. Reopening retains quantities/filters. Full-width
  workspace tabs retain the server sidebar. Remaining navigation/localization/tutorial/visual acceptance
  is recorded in RECYCLER.md; long quantities/times no longer wrap 32-bit counts or 24-hour clocks.

## Feature parity

| Feature | Avalonia state | Remaining work |
| --- | --- | --- |
| Saved-server list | Browsing, selection, pairing, name/address/port editing, direct-address/device probe and backed-up schema-preserving writes | Deletion with cloud cleanup, transfers, original context menus and complete sidebar design |
| Server-clock calibration | Wired to live samples, independent display ticks and pause state | Native/live-server acceptance, localized countdown formatting and full original status layout |
| Pairing/auth/accounts | Native-browser sign-in/status, FCM registration/listener, startup delivery checks, one-shot renewal, server/device pairing and re-pair, private writes, confirmed device/global sign-out | Live Steam/Facepunch/FCM acceptance and ambiguous-match resolution UI |
| Rust+ connection/reconnect | Authenticated status session, fallback, immediate socket-disconnect handling, cancellation and recovery | Native/live-server acceptance; soft/full modes and remaining feature subscriptions |
| Live map/markers/overlays | Not ported | Separate coordinate/state logic from rendering; choose portable map/helper hosting |
| Smart devices/automation/timers | Confirmed switches, nested group commands, alarm/storage state, subscriptions, local alias/group/move/reorder editor | Live-server acceptance, drag gestures, empty-group cleanup, cloud sync, add/delete/import/transfer, custom icons, automation, timers, alarm notifications/audio and original layout |
| Chat/team/clan/cloud | Not ported | Port protocol/services, moderation/history, synchronization and presentation |
| Cameras | Not ported | Validate camera SDK/runtime, stream decoding and portable rendering |
| Genetics Lab/web tools | Not ported | Decide and verify portable web hosting/bridge rather than copying WebView2 |
| Settings/localization/backups | Not ported | Portable settings models, resources, storage, lossless schema migration |
| Tray/hotkeys/audio/speech/capture | Not ported | Platform capability audit and adapters with truthful availability |
| Installer/updater | Not ported | Native packaging, signing, updater, permissions and upgrade-path validation |
| Raid calculator | Not ported | Preserve catalog, per-target methods, smart mixes, resource shopping list, persisted plans, clipboard and responsive layout |
| Recycler calculator | Offline catalog, calculations, quantity shortcuts, filters/paging, comparisons, icons and stateful workspace | Original navigation integration, tutorial targets, localization and native visual/accessibility acceptance |
| Shop search/trades | Not ported | Preserve filters, vending offers, alerts, shopping/profit workflows and map links |
| Map drawing/routes/layers/3D | Not ported | Preserve editing, grouping, import/export, keycards, no-build zones, monument overlays and helper integration |
| Command dock/minimap/popouts | Not ported | Preserve tiles/templates/configuration, overlays, floating windows and appearance settings |
| Death markers/stats/wipe tracker | Not ported | Preserve history, heatmaps, archives, marker configuration and cloud synchronization |
| AI companion | Not ported | Preserve answers/history, provider/model selection, chat commands and account settings |
| Notifications/custom alerts | In-memory pairing/alarm/chat/death feed while listening, sent-time normalization and duplicate suppression | Durable inbox, unread state, popup paging, sounds, channel settings and integrations |
| Community/LFG/support/admin | Not ported | Preserve profiles, moderation/reporting, tickets/attachments, account/team administration |
| Tutorials/achievements/help | Not ported | Preserve guided targets, progress, content, patch notes, galleries and consent dialogs |
| Traffic monitor/debug/logs | Not ported | Preserve statistics, filtering, monitoring and diagnostics without leaking credentials |

## Evidence and limitations

The existing Windows baseline suite passed **135 tests** before migration changes. Its build emitted
existing ImageSharp vulnerability warnings. That dependency has not been copied into this preview.

Structural discovery used the repository graph at Tier 2, then direct-source checks. Coverage metadata
was unavailable/stale for relevant files after switching branches; exact source and build/test results
were used for the chosen slice. No exhaustive unused-code or feature inventory is claimed.

The copied server clock still uses its existing phase-average approximation and one-boundary
extrapolation. This change preserves behavior; longer extrapolation is not newly validated.

No feature has been removed from the reference app. Its old clock/tests remain there because the user
requested an independent folder. This is intentional source reuse during migration, not a second
implementation inside the new solution. Broad dead-code cleanup awaits feature-by-feature evidence.

Native Linux/macOS UI, GTK/KDE/AppKit palette/font/menu integration, file-dialog, filesystem-permission, and distribution behavior require native
runs. Self-contained publish output alone does not prove those checks. Localization, high-contrast
themes, and full keyboard/screen-reader acceptance are still pending.

## Next implementation slice

1. Validate live Steam/Facepunch/FCM sign-in, pairing and sign-out on a native desktop with the user;
   validate the implemented startup health checks/registration repair and add ambiguous-match resolution without losing data.
2. Continue device import/transfer, drag gestures, cloud synchronization/deletion cleanup, notifications
   and automation using the verified session/device boundary. Local alias/group/move/reorder is implemented.
3. Port original server context menus and deletion together with its cloud cleanup; preserve all related data and consent flows.
4. Build the original rail/sidebar/workspace arrangement using Fluent components; preserve navigation
   order and density instead of adding placeholder buttons for unported features.
5. Validate actual server communication and the implemented socket-disconnect handling on Windows/Linux/macOS.
6. Continue every candidate in the parity checklist, including tools, dialogs and embedded web features.

## Local verification

- Windows: all **199 .NET test cases** cover the current source, including organization, recycling and socket teardown;
  build and isolated-copy results are recorded in HANDOFF.md at each checkpoint.
- Native browser crash reproduced using a local callback: Windows threw `Unable to create child window
  for native control host` because the executable lacked a supported-OS manifest. Added the official
  Avalonia compatibility declaration to the app host. The same production browser now passes local redirect
  and cancellation checks in a real Windows desktop process. Headless UI tests alone cannot catch this.
- Account tests cover callback origin/nonce validation, duplicate/error fields, signed/JWT claim shapes,
  Pascal-case HTTP fields, cancellation, verification before registration, registration rollback on save
  conflict, unknown-field preservation, current-user-only Windows ACLs and safe account status/error models.
- New checks cover pairing identity/port changes, nested alias/automation preservation, concurrent writes,
  no-op duplicates, rotated tokens, bounded sent IDs, coalesced disconnects, source-change/shutdown cleanup,
  direct re-pair verification/cancellation, server-ID backfill, sign-out scope/confirmation, remote failures,
  partial local cleanup, private backup permissions and notification-feed bounds. UI renders include pairing
  controls, a fixture alarm and sign-out confirmation at the 1000×700 minimum size.
- Tests cover companion/direct route selection, denial classification (including numeric-ID false
  positives), login-required failures, credential validation, token rotation, cancellation/disposal,
  connection loss/recovery, terminal failure during recovery, stale completion after switching, shutdown,
  missing/paused time and clock progress while status I/O is pending.
- Tests also cover SDK device confirmation, missing-entity classification, broadcast coalescing/unsubscription,
  empty/unknown storage, bundled item names, group deduplication, partial failures, stale commands, profile
  backup/unknown-field preservation, edit conflicts, identity validation, endpoint probes and editor shutdown.
- UI tests render the shell, shared status, server/device trees and simulated connected state
  at the reference 1750×814 and 1000×700. They verify bindings, selection cleanup, theme selection and toolbar bounds.
  The editor renders with all three platform style files in both light/dark modes; foreground updates are asserted.
  These are Windows-host renders, not native Linux/macOS acceptance or proof of native appearance.
  Images are emitted under the test output directory. Rendered server values are fixtures, not a live server.
- Reference-capture tool: **2 Python tests pass**, covering event/command capture, source immutability,
  code/web candidates and generated-tree exclusions.
- Isolation: a source-only copy outside the repository restored against a fresh, separate package
  cache, including the local protocol package and FluentAvaloniaUI. The current source passed all
  **199 .NET tests** there with zero warnings, including pairing, startup/renewal, device organization, recycler and disconnection regressions.
- Self-contained Release publishes succeeded for `win-x64`, `linux-x64`, and `osx-arm64` on the Windows host.
  These are compiler/package checks; Linux/macOS executables were **not run**.
- `git diff --exit-code` confirmed no tracked changes to the reference application or root build/config files.
- Checkpoint source commit `1cb470b` contains the verified preview and sign-in window fix; its intended
  push destination is Jawad's fork `origin/beta`. See HANDOFF.md. No release, installer, standalone upload,
  or native Linux/macOS acceptance run was performed.

Connection tests use synthetic credentials and fake transports. No personal profile/token contents
were printed, changed, copied into this folder, or used to contact a real server during verification.
Recovery now uses the socket's disconnect signal as well as status polling. Actual server acceptance
of that behavior remains pending. The full migration and visual parity are still unfinished.

The account modal, listener and sign-out workflows have automated coverage, but real Steam completion,
FCM delivery, remote sign-out and automatic token refresh are not accepted yet. The public Facepunch login
page preserves the supplied loopback callback in its form; no personal login was performed. Successful
re-sign-in creates a new registration without revoking earlier registrations. The text feed is in-memory;
notification-center persistence and audio remain pending. Credentials never enter UI models or logs.
Native Linux/macOS sign-in is untested. Do not treat this preview as production-ready.
