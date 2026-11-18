# 🚀 Rust+ Desktop v11.4.2 — Instant Maps & Modded Server Alerts

Hey @everyone! 🎉

**v11.4.2** brings instant cached map previews, clearer connection modes, continuous background auto-reconnects, modded server alerts, and Discord bot delivery fixes.

---

### 🔌 Connection & Map Previews
* **Instant map previews**: Cached maps appear immediately when opening or switching servers while devices connect in the background.
* **Clearer connection modes**: Connection card explains each mode — **Devices only** (devices, chat & status) vs **Full connect** (adds live map streaming).
* **Continuous background reconnects**: Lost connections and dead sockets retry automatically in the background (first after a few seconds, then once a minute) without needing manual clicks.
* **Smoother loading**: Progress bars show only when device work is active; maps from older parsers reparse automatically.

### 🚨 Modded Server Alerts & Discord Bot
* **Modded Server Alerts**: Modded server broadcast notices ("Wall destroyed at R9") have their own setting under **Settings > Alerts** (Raid alert, standard notice, or off), synced with Cloud 24/7.
* **Dedicated Discord channel (Premium)**: Route modded server alerts to their own channel or fall back to your raid/events channel.
* **Reliable Discord delivery (Premium)**: Raid alerts reach your Discord bot even if a teammate on the free tier is online while your app is closed.

### 💬 Team Chat & Events
* **`!oilrig` unlock duration**: `!oilrig` now reports how long ago the crate unlocked (e.g. *"Oil Rig crate unlocked 4m ago"*).
* **Persistent crate timer**: Oil rig crate timers survive reconnects to the same server.

---

### 📥 Download & Update
* Updates automatically via built-in updater, or download below:
👉 **[Download Rust+ Desktop v11.4.2](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.4.2)**

*Feedback or bugs? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧
