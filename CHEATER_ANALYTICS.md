# 🛡️ Cheater Analytics — Feature Branch

> **Branch:** `claude/focused-pasteur-2pCsw`  
> **Based on:** Rust+ Desktop v5.4.0 by [Pronwan](https://github.com/Pronwan)  
> **Status:** Ready for review / merge to main

---

## What Was Built

This branch adds a **Cheater-to-Player Ratio Analytics** panel to Rust+ Desktop — a local, privacy-respecting tool that helps server admins track, flag, and report suspected cheaters during a wipe.

No IP collection. No hardware fingerprinting. No Discord scraping. All data stays local.

---

## Feature Overview

### Shield Button
A new shield icon button (🛡️) appears in every server card header. Clicking it opens the Cheater Analytics sidebar panel.

### Current Wipe Snapshot
Live metrics updated as you flag players:

| Metric | Description |
|---|---|
| **Players** | Active player count (manually entered) |
| **Confirmed** | Players with confirmed bans or marked confirmed |
| **Suspected** | Flagged players not yet confirmed |
| **Risk %** | `(Confirmed + Suspected) / Players × 100` |
| **Risk Level** | Low / Moderate / High / Critical colour-coded band |

Risk bands:
- 🟢 **Low** — 0–5%
- 🟡 **Moderate** — 5–15%
- 🟠 **High** — 15–30%
- 🔴 **Critical** — >30%

### Flag a Player
Expand the **+ Flag a Player** section to add a suspect:
- Steam ID (triggers automatic VAC/game ban lookup via Steam public API)
- Display name, confidence level, flag source, notes, evidence link

### Report & Export
Once players are flagged, one click sends a full report:

| Button | Action |
|---|---|
| 📋 **Copy F7 List** | Builds a plain-text F7-style report, saves to Desktop, copies to clipboard |
| 💾 **Export CSV** | Saves a CSV of all flagged players to Desktop |
| 📨 **Send Discord** | Posts a formatted embed to your Discord server via webhook |

The Discord webhook URL is entered once and **persisted automatically** — it pre-fills every time you open the panel.

### Discord Report Example

![Discord report embed showing flagged players](docs/discord_report_preview.png)

The embed includes server name, wipe date, confirmed/suspected counts, and a full list of flagged players with confidence level, source, and evidence notes.

---

## New Files

| File | Purpose |
|---|---|
| `RustPlusDesktop/Models/CheaterRecord.cs` | Data model for a flagged player |
| `RustPlusDesktop/Models/CheaterAnalyticsSnapshot.cs` | Computed wipe snapshot with ratio + risk band |
| `RustPlusDesktop/Services/CheaterAnalyticsService.cs` | Local JSON persistence and snapshot builder |
| `RustPlusDesktop/Services/SteamBanLookupService.cs` | Steam `ISteamUser/GetPlayerBans` public API wrapper |
| `RustPlusDesktop/Services/CheaterReportService.cs` | CSV, F7 text, and Discord webhook report builder |
| `RustPlusDesktop/ViewModels/CheaterAnalyticsViewModel.cs` | MVVM ViewModel with all commands |
| `RustPlusDesktop/Views/CheaterAnalyticsPanel.xaml` | WPF sidebar panel UI |
| `RustPlusDesktop/Views/CheaterAnalyticsPanel.xaml.cs` | Code-behind |

### Modified Files

| File | Change |
|---|---|
| `RustPlusDesktop/MainWindow.xaml` | Shield button + panel overlay |
| `RustPlusDesktop/MainWindow.xaml.cs` | Button handler, lazy ViewModel init |

---

## Data & Privacy

- All flagged player data is stored locally at `%AppData%\RustPlusDesk\cheater_records_{serverId}.json`
- Snapshot history at `%AppData%\RustPlusDesk\cheater_snapshots_{serverId}.json`
- Discord webhook URL cached at `%AppData%\RustPlusDesk\cache\cheater_discord_webhook.json`
- The only outbound network calls are:
  - Steam public `ISteamUser/GetPlayerBans` API (optional, requires user-provided API key)
  - Discord webhook POST (only when user clicks **Send Discord**)

---

## How to Test Locally

```powershell
# Clone or pull the branch
git clone https://github.com/Pronwan/rustplus-desktop-qa.git
git checkout claude/focused-pasteur-2pCsw

cd RustPlusDesktop

# Create required local secrets file (gitignored)
# Copy ObfuscatedSecrets.cs from your working install or create a stub

dotnet run
```

Then click the 🛡️ shield button on any server card.

---

## Discord Webhook Setup

1. Open your Discord server → **Server Settings → Integrations → Webhooks**
2. Click **New Webhook**, pick a channel, copy the URL
3. Paste into the webhook box in the Cheater Analytics panel
4. Click **📨 Send Discord** — the URL saves automatically for next time
