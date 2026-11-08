# 🚀 Rust+ Desktop v11.2.0 — High-Performance Map Engine, Monument Toggles & Lite Build

Hey @everyone! 🎉

We're excited to introduce **Rust+ Desktop v11.2.0**, packed with huge performance upgrades, a dramatically smoother map experience, new monument filtering controls, and a much lighter installer footprint!

---

### 🌟 Key Highlights & Enhancements

#### ⚡ High-Performance Map Engine & Smooth Panning
* **Display-Rate Zoom & Pan**: Zooming and panning now synchronize directly with your monitor's refresh rate, making map navigation buttery-smooth and responsive.
* **Non-Blocking Animations**: Fixed an issue where continuous map animations could starve mouse and wheel input. Controls now remain instantly responsive during transitions.

#### 🔋 Idle Efficiency & 33% Faster Connections
* **Zero Idle Redraws**: Background frame rendering now halts completely when the app is idle, slashing background CPU and GPU usage down to near zero.
* **Rapid Map Loading**: Connecting to servers with parsed maps is now up to **33% faster** thanks to streaming JSON parsing, internal runtime integration, and shared icon caching.
* **Stutter-Free Sidebar**: Sidebar drawer unfolding and folding transitions have been re-engineered for smooth, 60+ FPS animation.

#### 🗺️ Extra Monument Filter Toggles
* **Granular Monument Controls**: Easily show or hide specific extra monument categories with individual toggle switches.
* **Quick Show/Hide All**: Added one-click **Show All** and **Hide All** buttons to quickly declutter your map overview.

#### 📦 Ultra-Lightweight "Lite" Default Package
* **Slimmer Installer**: Switched default releases to our optimized "Lite" build, stripping out hundreds of megabytes of bundled 3D models from the installer download.
* **On-Demand Model Streaming**: 3D models and assets are now streamed seamlessly when you open the 3D Map viewer, keeping the initial installation fast and lightweight.

---

### 📥 Download & Update
* If you have Rust+ Desktop running, it will automatically download and install updates via the built-in updater.
* Or download the latest build directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.2.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.2.0)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧

