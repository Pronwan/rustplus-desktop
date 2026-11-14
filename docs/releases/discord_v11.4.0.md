# 🚀 Rust+ Desktop v11.4.0 — Cloud Matching & In-Game Mutes

Hey @everyone! 🎉

**v11.4.0** brings sub-second Cloud 24/7 sync, in-game alert mutes & timers, device commands, and full server matching fixes after Rust's November update.

---

### ☁️ Cloud 24/7 & Server Matching
* **Fixed server matching**: Servers paired since November no longer share addresses. Cloud 24/7 identifies them by Facepunch ID, restoring correct maps and devices.
* **No duplicates or double alerts**: Re-paired servers merge with existing profiles; Cloud 24/7 steps back when desktop is open. Backend fixes repair mixed-up servers.

### ⚡ Instant Cloud & Device Sync
* **Sub-second sync**: Renames, command words, icons, and deletions push to the cloud in 400ms.
* **Live 2-way sync**: Edits or device removals made on web/mobile sync straight to desktop in real-time.

### 🔕 Chat Alert Mutes & Timers
* **Synced alert bell**: The title-bar bell shares its mute state with Cloud 24/7, silencing alerts across both.
* **Timed mutes**: Right-click/gear on the bell to mute for 15m, 1h, 8h, or custom, with a countdown tooltip.

### 💬 In-Game Team Chat Commands
* **`!mute [time]` & `!unmute`**: Mute and unmute alerts directly from team chat (e.g. `!mute 30m`, `!mute 2h`).
* **`!device` control**: Rebind commands or rename switches in-game (`!device sam name Main SAMs`), owner-protected. Enabled by default on new servers.

### 🚨 Event Alerts & Stability
* **Alarm, oil rig & timer alerts restored**: Restored with live crate countdowns in the event dock.
* **No rate limits**: Full connects pace queries safely within Rust+'s limits.

---

### 📥 Download & Update
* Updates automatically via built-in updater, or download below:
👉 **[Download Rust+ Desktop v11.4.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.4.0)**

*Feedback or bugs? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧