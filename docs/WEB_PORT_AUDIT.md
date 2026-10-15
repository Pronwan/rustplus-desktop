# Web Port Audit: Rust+ Desktop

**Audit date:** 2026-10-14  
**Repository:** https://github.com/icuxz/rustplus-desktop  
**Branch:** master  
**Phase:** 0 only. No Phase 1 implementation is included.

## Executive summary

The fork is public and accessible. This audit uses the fork's current README, BUILDING.md, WPF project/startup code, and Genetics Lab documentation/component files. A web port is viable for many Rust+ server-facing features, but it is a new web client and service architecture, not a WPF-to-browser conversion.

Recommended stack: cross-platform ASP.NET Core backend reusing compatible C# domain/RustPlusAPI code, SignalR for realtime updates, React + TypeScript frontend, PostgreSQL for durable multi-user state. The highest risk is confirming the existing pairing/FCM/Rust+ connection stack works reliably in a headless Linux container. The public source explicitly excludes the proprietary 3D Map Parser.

## Evidence reviewed

- [README.md](https://github.com/icuxz/rustplus-desktop/blob/master/README.md)
- [BUILDING.md](https://github.com/icuxz/rustplus-desktop/blob/master/BUILDING.md)
- [WPF project](https://github.com/icuxz/rustplus-desktop/blob/master/RustPlusDesktop/RustPlusDesk.csproj)
- [App startup](https://github.com/icuxz/rustplus-desktop/blob/master/RustPlusDesktop/App.xaml.cs)
- [Genetics Lab README](https://github.com/icuxz/rustplus-desktop/blob/master/RustPlusDesktop/Features/GeneticsLab/README.md)
- [Genetics Lab deployment guide](https://github.com/icuxz/rustplus-desktop/blob/master/RustPlusDesktop/Features/GeneticsLab/DEPLOYMENT.md)
- [Genetics Lab license](https://github.com/icuxz/rustplus-desktop/blob/master/RustPlusDesktop/Features/GeneticsLab/LICENSE)

The project targets Windows-specific .NET/WPF and Windows Forms. Startup code uses Windows mutex/named-pipe/tray/window APIs. BUILDING.md says production secrets are excluded and the 3D Map Parser is proprietary and not present in the open-source repository. The Genetics Lab is already a React/TypeScript/Vite web application; review its included license before integrating or redistributing it.

## Feature inventory

| Feature | Current implementation / evidence | Web feasibility | Proposed approach |
|---|---|---|---|
| Pairing, FCM listener, Rust+ connection | C# RustPlusAPI integration and connection services | Partial pending Linux test | Server-owned connection manager per server profile; keep credentials server-side; retry with jittered backoff. |
| Server profiles, connect/disconnect | Desktop server configuration and switching | Full | Persist profiles; one socket per profile, shared across browser clients. |
| Login, roles, user management | Existing cloud/auth integration; not local team RBAC | Full | ASP.NET Core Identity; admin/member/view-only policies enforced by API. |
| 2D map, players, markers, monuments, grid, zoom/pan/follow | WPF map UI/services and map data/assets | Partial | React canvas/SVG map using verified public map assets and Rust+ data. |
| 3D map and base footprints | README/BUILDING.md; parser excluded as proprietary | Partial / not from public source | Exclude initially; independently implement only if format/license permit. |
| Team panel and player positions | Rust+ team/map services and WPF views | Full for API-provided data | SignalR snapshots and deltas with reconnect resync. |
| Team/clan chat | Chat controls and Rust+ services | Partial pending API verification | Browser chat UI; server-side reads/writes, authorization and rate limiting. |
| Smart devices, groups, switch control | Device UI/services | Full for supported Rust+ operations | Persist groups; authorize every control action; central command queue. |
| Alarms, storage monitors, upkeep | Device/event services | Partial | Port supported event logic; deduplicate notifications and persist important events. |
| Event dock: cargo, heli, chinook, timers | Event/timer services; README notes some events use local audio | Partial | Use API-observable events and server timers; manual timers for missing signals. |
| Vending/shop search and trade profit | Shop features; README notes dynamic vending data was removed from Rust+ API | Partial | Use available/cached data with freshness labels; do not promise live inventory. |
| Logic Engine and chat commands | Desktop automation/rule features | Partial | Port to a bounded deterministic backend rule engine with action logs and loop prevention. |
| Raid alerts | Smart alarms and cloud/offline alert integrations | Full for server-observable triggers | Web Push and Discord webhook; keep provider secrets encrypted. |
| Death stats and player/wipe tracker | Tracker/history features and cloud sync | Partial | Self-host event/history store; browser screenshot upload may replace local OCR. |
| Genetics Lab, breeding/farm planning, OCR | `RustPlusDesktop/Features/GeneticsLab`, React/TS/Vite | Full standalone; integration partial | Reuse as separate route/module after license and build review. |
| Raid/recycler calculators | App data and calculator features | Full if datasets are reusable | Port calculations and add deterministic fixture tests. |
| CCTV controls/video | README says camera engine uses native Rust+ camera API | Partial | Investigate protocol; backend relay/transcode to browser-compatible WebRTC/HLS if feasible. |
| Heatmaps and map-file parsing | README describes direct map-file parsing; 3D parser excluded | Partial | Implement independent parser only for lawful, documented formats; upload files explicitly. |
| Discord | Bot/webhook integrations | Full for webhook; bot partial | Start with webhooks or run own bot; never call author's cloud. |
| Alexa / Google Home | Published smart-home integrations | Partial / costly | Drop from initial web port; consider self-hosted Home Assistant integration later. |
| Telegram calls | CallMeBot integration | Partial | Optional user-configured integration; prioritize Web Push and Discord. |
| Device/map sharing, Community Hub, wipe archives | README identifies rustplusdesktop.cloud platform | Partial | Self-host ACLs and storage; defer Community Hub or implement local-only moderated version. |
| AI companion and voice | Provider APIs plus local audio/transcription | Partial | Optional browser microphone with permission and server-side provider adapter; no local game audio capture. |
| Notifications and sound | Desktop event notifications | Full/partial | Web Push for background alerts; browser sound only while browser context permits. |
| Localization | WPF resources | Full | Port strings to React i18n. |
| Installer, auto-update, tray/single-instance | Velopack, Windows Forms, mutex/named pipe | Not as-is | Docker deployment and browser sessions; PWA cannot reproduce native tray/global behavior. |

## Windows and local-hardware dependencies

| Feature | Limitation | Substitute / decision |
|---|---|---|
| Always-on-top widget/minimap overlay | Browser cannot overlay another process | Drop native overlay; provide responsive dashboard/PWA. |
| Crosshair overlay / game-relative positioning | Requires desktop window/game coordinates | Drop. |
| Local process loopback audio detection | Hosted server cannot hear a teammate's Windows game audio | Drop in web deployment; use API events or manual timers. |
| Native unattended death-screen OCR | Browser cannot read another process' screen | User-selected screenshot upload/paste with browser OCR. |
| Global push-to-talk | Global hotkey/audio capture unavailable to ordinary web app | Optional in-page microphone button with explicit permission. |
| Tray/background tracking | Native Windows tray integration | Hosted backend worker runs independently of browser sessions. |
| Global hotkeys | Browser keyboard scope is restricted | In-page shortcuts while focused. |
| Windows speech/system audio routing | Windows speech/audio APIs are not portable | Browser SpeechSynthesis or optional server TTS; behavior varies by browser. |
| Arbitrary local map file access | Browser sandbox | Explicit file upload or configured server-mounted import directory. |
| Genetics Lab OCR | Existing app uses browser-compatible Tesseract/WASM | Keep browser-based OCR; request camera/file permissions explicitly. |

## Proprietary cloud dependencies and replacements

| Existing cloud-dependent feature | Self-hosted replacement / decision |
|---|---|
| Device sharing | Database-backed per-server ACLs, separate view/control permissions. |
| Map overlay sharing | Persist markers/drawings/settings and broadcast authorized updates over SignalR. |
| Wipe archives | PostgreSQL metadata plus local or S3-compatible object storage, retention and backups. |
| Community Hub | Defer or implement a local-only moderated page; do not mirror hosted content. |
| Discord bot | Self-host a bot or use webhooks. |
| Alexa | Drop initially; Home Assistant integration is a separate later project. |
| Telegram calls | Optional user-configured provider; otherwise use Web Push/Discord. |
| Cloud login/auth | Local account store, role policies, secure password reset and optional MFA. |
| Cloud map/history/death sync | Local event/history database with export/import and retention policy. |
| Shared local-audio detections | Do not recreate without a separately installed companion agent; prefer API-observable events. |

No proprietary assets or cloud endpoints should be copied or called. Preserve GPL-3.0 headers and upstream attribution. Verify each reused dependency and the Genetics Lab's separate license.

## Recommended stack

**Backend:** cross-platform ASP.NET Core on a supported .NET LTS release; compatible existing C# domain logic and RustPlusAPI; ASP.NET Core Identity; REST plus SignalR; PostgreSQL; Data Protection/key ring and encryption for stored secrets; hosted background services for connection supervision, timers and alert delivery; xUnit tests.

**Frontend:** React + TypeScript + Vite; SignalR client; responsive server dashboard; canvas/SVG 2D map; Genetics Lab as isolated route/module after license review.

**Deployment:** multi-stage Dockerfile, docker-compose.yml, .env.example, persistent DB/data volumes, health checks and documented HTTPS reverse proxy (Caddy/Nginx/Traefik). Do not run multiple backend replicas until per-server connection ownership is coordinated.

**Why not Node for the backend?** Existing code and RustPlusAPI are C#-oriented. A Node rewrite increases protocol/pairing risk without improving browser capability. The recommendation depends on proving the C# connection stack is cross-platform.

## Security and operational requirements

1. No anonymous access; admin/member/view-only authorization must be enforced server-side.
2. Never expose FCM tokens, Steam credentials or API keys to browsers, URLs, logs or frontend bundles.
3. Encrypt secrets at rest; keep encryption keys outside the database and back them up securely.
4. One backend-owned socket per server profile, shared by all clients.
5. Central command pacing, bounded queues, jittered exponential reconnect backoff and safe retries. Do not invent numerical Rust+ limits; confirm them from the actual dependency and repo history.
6. Deduplicate FCM/event replays and persist important events before fan-out.
7. HTTPS, secure cookies, CSRF protection where applicable, SignalR origin checks, CSP, upload validation and control-action audit logs.
8. Document player-history retention and who can see it.

## Suggested phases

**Phase 1:** authentication/roles, server profile and pairing lifecycle, connection manager, realtime channel, 2D map, team panel/chat where supported, smart devices/groups, event dock/timers, tests, Docker and setup/pairing documentation.

**Phase 2:** shop search where data exists, Logic Engine, chat commands, Web Push/Discord, death and wipe history, calculators, Genetics Lab integration, CCTV only after relay feasibility testing, and independent heatmap/map parsing.

Explicitly exclude from browser parity: always-on-top overlay, crosshair overlay, global hotkeys, local game-process audio detection, unattended native death-screen capture, native tray behavior and system-wide Windows speech.

## Confidence and fastest uncertainty reducer

**Confidence:** approximately 0.70 for architecture/feature boundaries; lower for implementation effort. The Windows dependencies and proprietary parser exclusion are explicit in the repo. Linux compatibility of pairing/FCM and exact API coverage still need runtime validation.

**Fastest test:** build a minimal headless .NET service in the intended Linux Docker environment using the exact RustPlusAPI dependency. Complete FCM pairing, open one Rust+ connection, receive a team/map/device update, send one harmless supported request, force a disconnect, and verify bounded reconnect behavior.

## Phase 0 exit checklist

- [x] Inspect fork README, BUILDING.md, WPF project/startup and Genetics Lab docs/license.
- [x] Inventory features and Windows/cloud dependencies.
- [x] Recommend stack, security baseline and risk test.
- [x] Identify the target fork and default branch.
- [ ] Runtime Linux pairing/connection proof (not performed in this audit).
- [ ] Phase 1 approval (not granted).

**Stop point:** Phase 0 audit only. No Phase 1 implementation has been started.
