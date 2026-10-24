# 🚀 Rust+ Desktop v10.1.0 — 3D Map Rework & Livestock Breeding!

Hey @everyone! 🎉

We are super excited to roll out **v10.1.0**, featuring a massive overhaul to our **3D Map Engine**, brand-new **Fortify Base Imports**, high-precision **1-Meter Terrain Raycasting**, **Atmospheric Sky & Lighting**, and **Livestock Breeding** in Genetics Lab! 🏰🌄🐔

---

### 🌟 What's New in v10.1.0

#### 🏰 Fortify Base Imports & 3D Placement Workflow
* **Import Fortify Bases Directly (`.json`)**: Load full multi-tier base designs directly into your live 3D map with multi-layer building support!
* **Live Terrain-Fit & Support Analysis**: Instant real-time foundation elevation validation, overlap detection, and placement blocker alerts before placing.
* **Foundation Tier Selection**: Toggle between **Twig**, **Wood**, **Stone**, **Sheet Metal**, and **Armored (Top Tier)** materials with accurate transparency controls.
* **Flexible Placement Controls**: Fine-tune base elevation, rotation, ramp foot burial, or use **Force Place** to override warnings when needed.
* **High External Walls & Gates**: Barbed wire cutouts, gates, and embrasures render in full 3D detail.

#### 🚪 Interactive In-Scene Doors & Inspection Mode
* **Door, Gate & Hatch Interactions**: Click to open/close hinged single doors, double doors, garage doors, prison cell gates, and ladder floor hatches directly in 3D.
* **First-Person Walkthrough**: Inspect your base layout and sightlines at player eye-level directly inside the 3D map view!

#### 🧬 Genetics Lab — Livestock Breeding
* **Livestock Crossbreeding Calculator**: Simulate, calculate, and optimize gene combinations, breeding trees, and traits for in-game livestock right alongside plant genetics.

#### 📐 1-Meter High-Precision Terrain & Raycasting
* **Pixel-Perfect Elevation Raycasting**: Ground raycasting now samples exact 1-meter elevation height layers (eliminating ~0.26m–2.6m triangle deviations) for flawless orbit centering and placement.
* **Camera-Following Detail Patch**: Refined the dynamic 1-meter detail patch under your camera with aligned texture projection across steep hills, cliffs, and roads.
* **Optimized Performance**: Faster ground collision tests and smoother navigation across dense maps.

#### ⛅ Atmospheric Rust-Style Sky Dome
* **Rayleigh & Mie Atmospheric Scattering**: Replaced flat skies with a dynamic gradient sky dome, solar halo, sun disk, and soft drifting wind clouds.
* **Horizon & Fog Blending**: Distant ocean horizons and fog now blend naturally into the sky lighting with exposure compensation.

---

### 📥 Download & Update
* If you have Rust+ Desktop installed, it will automatically download and update via the built-in updater!
* Or download the latest installer directly from GitHub Releases:
👉 **[Download Rust+ Desktop v10.1.0](https://github.com/Pronwan/rustplus-desktop/releases/latest)**

*Found any bugs or have suggestions? Let us know in <#feedback> / <#bug-reports>!*
Enjoy the new 3D builder and livestock genetics! 🦀💥
