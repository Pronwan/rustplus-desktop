# 🚀 Rust+ Desktop v11.1.1 Hotfix — Cache Isolation & UI Freeze Fix

Hey @everyone! 🎉

We've deployed a rapid stability hotfix, **v11.1.1**, resolving cache collisions on Facepunch proxy servers and eliminating a UI freeze caused by dynamic event alerts on modded servers.

---

### 🌟 Key Highlights & Fixes

#### 🛡️ Companion Proxy Cache Isolation
* **Unique Server Identification**: Servers routing through Facepunch's companion socket proxy now use their unique Facepunch server GUID as the cache key instead of the generic `companion-socket_28082`.
* **Zero Collisions**: Map caches, chat histories, clan logs, overlays, and cloud device registries no longer collide or overwrite each other when connected to multiple companion socket servers.
* **Full Direct-Connection Compatibility**: Servers with dedicated IPs and ports continue using their standard host/port identifiers with zero disruption.

#### ⚡ Crash & Freeze Prevention
* **UI Freeze Resolved**: Fixed an 8-second freeze watchdog trigger on modded and PVE servers caused by rapid announcement checks on building-blocked and generic radius markers.
* **Streamlined Spawns**: Announcement logic now strictly filters for actual vehicle/world events (Patrol Helicopter, CH47 Chinook, Travelling Vendor), preventing notification floods and repeated Discord webhook dispatches.

---

### 📥 Download & Update
* If you have Rust+ Desktop running, it will automatically download and install updates via the built-in updater.
* Or download the latest build directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.1.1](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.1.1)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧

