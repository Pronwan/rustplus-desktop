# 🚀 Rust+ Desktop v11.1.0 Hotfix — Pairing Sync & Proxy Server Support

Hey @everyone! 🎉

We've rolled out a quick hotfix update, **v11.1.0**, addressing companion proxy server pairings, one-click missed pairing recovery, and map monument validation.

---

### 🌟 Key Highlights & Fixes

#### 🔗 Companion Proxy & Pairing Recovery
* **Proxy-Only Server Support**: Fixed an issue where servers routing exclusively through Facepunch companion proxy sockets (where no public IP is provided in the push payload) were dropped. These servers now connect seamlessly via the companion relay socket.
* **Sync Missed Pairings**: Added a **Sync** button to the Notifications tab and Server pairing recovery menu (`...`). If FCM notifications were dropped, delayed, or missed, you can now reconcile your recent pairings from history without having to re-pair in-game.
* **Smart Pairing Deduplication**: Prevents duplicate pairing cards and redundant server entries when syncing notification history. Automatically prunes duplicate pairing rows in your notification center.

#### 🗺️ Map & Monument Validation
* **Monument Matching**: Map loader now validates that cached or uploaded map files match the server's active monuments before applying, ensuring accurate terrain and landmark alignment.

---

### 📥 Download & Update
* If you have Rust+ Desktop running, it will automatically download and install updates via the built-in updater.
* Or download the latest build directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.1.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.1.0)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧

