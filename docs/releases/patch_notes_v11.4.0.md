# 🚀 Rust+ Desktop v11.4.0 — Cloud 24/7 Server Matching, Instant Sync & Team Chat Mutes

Hey @everyone! 🎉

Update **v11.4.0** brings instant device and pairing synchronization with Cloud 24/7, synchronized chat alert mutes and timers, in-game team chat commands, and fixes server matching for servers paired since Rust's November update.

---

### ⚡ Instant Cloud & Device Sync

* **Sub-second device sync**: Renaming devices, changing command words, setting custom icons, or deleting devices now pushes to the cloud 400 ms after saving. Edits made on the web companion or mobile push straight to the desktop app in real-time.
* **Smart pairing & website removal sync**: Pairings for address-less (Facepunch ID-only) servers now import smoothly. Devices removed on the website companion are cleanly deleted on desktop, while newly paired devices restore reliably.

### 🔕 Chat Alert Mutes & Timers

* **Synced alert bell**: The alert bell in the title bar now shares its mute state directly with Cloud 24/7. Muting alerts in the desktop app automatically mutes Cloud 24/7 alerts too, so you don't get spammed while raiding or roaming.
* **Timed mutes with countdown**: Click the gear menu on the bell to mute alerts for 15 minutes, 1 hour, 8 hours, or a custom duration. The bell displays the remaining mute time in its tooltip and turns back on automatically when time expires.
* **Consistent mute controls**: "Select all", "Deselect all", and individual alert toggle switches now route through the same synchronized mute engine.

### 💬 In-Game Team Chat Commands

* **`!mute [duration]` & `!unmute`**: Mute and unmute chat alerts directly from in-game team chat (e.g. `!mute 30m`, `!mute 2h`, `!unmute`).
* **`!device` management**: Rebind or rename smart switches directly in-game using `!device <#n|word|name> name|command <value>` (e.g. `!device sam name Main SAMs` or `!device 1 command sam`), with built-in owner-only validation.
* **Enabled by default**: Chat commands are now turned on automatically for newly paired servers.

### ☁️ Cloud 24/7 Server Matching

* **The right server in the cloud**: Since Rust's November update, newly paired servers no longer report their address. Cloud 24/7 now tells these servers apart by their Facepunch ID, so you no longer see another server's map, and your devices reach the cloud reliably.
* **No duplicate servers or alerts**: Servers you re-pair are merged with their existing profile. While the desktop app is open, the cloud gracefully steps back, so alerts no longer arrive twice.
* **Existing servers repaired**: Servers that were mixed up after the update are automatically sorted out on our backend. Update and open the app once to sync your devices to the right server.

### 🚨 Event Alerts & Event Dock

* **Smart alarm, oil rig and timer alerts are back**: Re-implemented following Rust's retired events. Oil rig alerts use a smart alarm configured as an oil rig trigger; if not set up, the entry shows you how.
* **Oil rig crates in the event dock**: With an oil rig trigger configured, the event dock displays your crates with live hacking countdowns and trigger timestamps, and turns on the Logic Engine automatically.

### 🔌 Device Connection Stability

* **No more rate limits when connecting**: Full server connections pace device queries to stay safely within Rust+'s rate limits, preventing devices from temporarily showing as unreachable.

---

### 📥 Download & Update
* Existing installations update automatically via the built-in updater.
* Or download directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.4.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.4.0)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧

