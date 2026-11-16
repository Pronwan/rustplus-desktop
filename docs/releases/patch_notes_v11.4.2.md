# 🚀 Rust+ Desktop v11.4.2 — Instant Map Previews, Clearer Connections & Server Alerts

Update **v11.4.2** is a hotfix for connecting and switching servers: your saved map shows right away, the connection card tells you what each mode does, and reconnects stop retrying endlessly.

---

### 🔌 Connection & Map

* **Saved map shows instantly**: When you open or switch to a server, its saved map appears at once as a cached preview, while the app connects your devices and checks the current wipe.
* **Clearer connection modes**: The connection card now explains each mode. **Devices only** connects your devices, chat and team status without live map updates; **Full connect** adds live map updates.
* **Calmer reconnects**: When Rust+ can't be reached, the app tries again a limited number of times instead of retrying without end. If a saved server has no companion server ID, it tells you to sync your pairings or pair the server again in game.
* **Device progress only when needed**: The device progress bar only shows while devices are really being loaded.

### 🚨 Alerts & Discord Bot

* **Modded Server Alerts**: Alerts a modded server sends on its own, such as "Wall destroyed at R9", have their own setting under **Settings > Alerts**, synced with the website and Cloud 24/7. Treat them as a **raid alert** (alarm window, raid channel, Telegram call) or as a **standard alert** (a short notice, events channel, no call), or turn them off.
* **Server Alerts in Discord (Premium)**: Server alerts now also reach your Discord bot while the app is closed. In the Discord Bot settings you can give them their own channel; left empty, they go to your raid or events channel, depending on the setting above.
* **No more missing Discord alerts (Premium)**: If a teammate on the free plan had the app open while yours was closed, raid alerts did not reach your Discord bot. The cloud now posts them in that case.

---

### 📥 Download & Update
* Existing installations update automatically via the built-in updater.
* Or download directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.4.2](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.4.2)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧
