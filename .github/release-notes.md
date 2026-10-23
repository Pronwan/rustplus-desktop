## [10.3.0](https://github.com/JawadYzbk/rustplus-desktop/releases/tag/v10.3.0) (2026-10-23)

### 🚀 Features & Enhancements

- **map**: default the heatmap density slider to 25% ([19aef3b](https://github.com/JawadYzbk/rustplus-desktop/commit/19aef3b87fbb3dcc3cfcf7ddebfed9a601f380a7)) (JawadYzbk)
- **map**: add Halloween event heatmaps, density sliders and active heatmap state ([9092c34](https://github.com/JawadYzbk/rustplus-desktop/commit/9092c34645a6660fe8be104b51b427e122340af9)) (JawadYzbk)
- **ai-companion**: add guided setup wizard with per-provider key guides ([3f6657f](https://github.com/JawadYzbk/rustplus-desktop/commit/3f6657f6f63ac94f1b427173651cdf5fcc9314a7)) (JawadYzbk)
- add option to clear 3d map data per server and delete missing devices ([f19f663](https://github.com/JawadYzbk/rustplus-desktop/commit/f19f663e4c03dfc8f7ccb7f407ab9ef8b6ce72e9)) (JawadYzbk)
- **map**: use native grab and grabbing pointers and default to normal arrow when map is not loaded ([2345b48](https://github.com/JawadYzbk/rustplus-desktop/commit/2345b48d62ed7bdf21a8b82d8393e7a7868b28bb)) (JawadYzbk)
- **map**: loosen canvas pan bounds and add smooth continuous exponential zoom ([bb9a4c4](https://github.com/JawadYzbk/rustplus-desktop/commit/bb9a4c45aa5324e80c14b1e294fcd50ee690e788)) (JawadYzbk)
- **map**: implement 58-monument naming dictionary and smooth kinetic map controls ([0fb0678](https://github.com/JawadYzbk/rustplus-desktop/commit/0fb0678d5a0032fb564dd727022f9f276027a3f5)) (JawadYzbk)
- **map**: scale resource heatmaps to their own range and rasterize off-thread ([bd0953e](https://github.com/JawadYzbk/rustplus-desktop/commit/bd0953e2e8949f49d2cecf2717a872499955af5c)) (JawadYzbk)
- **map**: integrate local high-res map parser, unconstrained cargo path, water color alignment, and extra zoom-out levels ([0cb215b](https://github.com/JawadYzbk/rustplus-desktop/commit/0cb215b8bc75c97d93eee0891415921cc46f2a7b)) (JawadYzbk)
- **deaths**: track water and deep sea deaths as their own locations ([26ae32d](https://github.com/JawadYzbk/rustplus-desktop/commit/26ae32de17a06ee5cbbd6d9d0d8260e1aeacc39d)) (JawadYzbk)
- **servers**: restore saved servers from the cloud ([9af6d1e](https://github.com/JawadYzbk/rustplus-desktop/commit/9af6d1e0a958c9e34cda1069bb416b1c6d683075)) (JawadYzbk)
- **logic**: turn the Logic Engine on when the oil rig trigger is added ([b3c2e87](https://github.com/JawadYzbk/rustplus-desktop/commit/b3c2e87e822e992643acef2db489b90b71eab050)) (JawadYzbk)
- **map**: rework the Layers flyout and add death-marker settings to it ([5ddfcec](https://github.com/JawadYzbk/rustplus-desktop/commit/5ddfcec9ea076c029e0670b8ab3eca62be956e8d)) (JawadYzbk)
- **avalonia**: restore responsive reference panel layout ([6cc3f08](https://github.com/JawadYzbk/rustplus-desktop/commit/6cc3f0832fa4d8b7c51069b3d7de82d5f7a8f796)) (JawadYzbk)
- **premium**: keep Patreon as the Support Development link for premium users ([e33ac2c](https://github.com/JawadYzbk/rustplus-desktop/commit/e33ac2c14ad151f9bc114abc7795dae68427d0ed)) (JawadYzbk)
- **premium**: send upgrade buttons to the Paddle pricing page ([0d730c8](https://github.com/JawadYzbk/rustplus-desktop/commit/0d730c868fe5a80429c2ef52a1a426a3c89250e3)) (JawadYzbk)
- **automation**: add NONE as an Otherwise choice that leaves the switch alone ([0ab5735](https://github.com/JawadYzbk/rustplus-desktop/commit/0ab5735368f9a821417edf674489f9c6ca6325bb)) (JawadYzbk)
- **avalonia**: react to socket loss and edit saved alarm titles ([d4bba03](https://github.com/JawadYzbk/rustplus-desktop/commit/d4bba03af898d26d09818905700b7f6e36e5eecf)) (JawadYzbk)
- **avalonia**: port the offline recycler workspace with bundled item artwork ([61d2eab](https://github.com/JawadYzbk/rustplus-desktop/commit/61d2eab546bc18244ffe8eca19f2b7c5e74f3a89)) (JawadYzbk)
- **avalonia**: organize saved device trees without losing settings ([a8cf2a4](https://github.com/JawadYzbk/rustplus-desktop/commit/a8cf2a48afe856bfd120ec02e1a6bcb900e802c5)) (JawadYzbk)
- **avalonia**: verify push delivery and renew saved registrations at startup ([9fd4ee6](https://github.com/JawadYzbk/rustplus-desktop/commit/9fd4ee6dfef59f73980c99fc01d2d52b1d1f5e10)) (JawadYzbk)
- **avalonia**: add pairing listener, protected profile updates and sign-out ([8e1df49](https://github.com/JawadYzbk/rustplus-desktop/commit/8e1df49bb1840de2ffca22f44910832b6388ddbd)) (JawadYzbk)
- **notifications**: show Rust+ notifications like the live map's bell ([e1bf659](https://github.com/JawadYzbk/rustplus-desktop/commit/e1bf6594b94195fccb6906d05321b63d054689ee)) (JawadYzbk)
- **settings**: redesign Connected Services around what each integration does ([5a05e30](https://github.com/JawadYzbk/rustplus-desktop/commit/5a05e30601c557270119d50abec7d0913b5e6886)) (JawadYzbk)
- **cloud**: sync Facepunch server ids with the platform ([2ec6a33](https://github.com/JawadYzbk/rustplus-desktop/commit/2ec6a33575468979d732246ed84d1321a8ca59be)) (JawadYzbk)
- **rustplus**: connect through Facepunch's companion socket ([051352a](https://github.com/JawadYzbk/rustplus-desktop/commit/051352a5abaf10673e54ccdbf3742f7d1bfa7c4d)) (JawadYzbk)

### 🐛 Bug Fixes & Stability

- **map**: stop the window freezing while a parsed map loads ([ea8250b](https://github.com/JawadYzbk/rustplus-desktop/commit/ea8250bb071f1d0cad94a14e65b8e4a5125a623e)) (JawadYzbk)
- **map**: add missing heatmap layers instead of offering a re-parse that never ran ([66d9de2](https://github.com/JawadYzbk/rustplus-desktop/commit/66d9de23f0d6ed9c769d101d070838c9a3f1a5a1)) (JawadYzbk)
- **map**: add suitable pan cursors and stop zoom and elastic transitions on navigation ([a86eee2](https://github.com/JawadYzbk/rustplus-desktop/commit/a86eee2bc84fe30ca917792488c5b08e27aa0ab4)) (JawadYzbk)
- **genetics-lab / recipes**: apply the tea rebalance values to the Advanced and Pure teas ([d2ddcc7](https://github.com/JawadYzbk/rustplus-desktop/commit/d2ddcc7db46961d93d4687d7053804feb92ba4c8)) (JawadYzbk)
- **map**: show only dense heatmap areas and use full-resolution heatmaps ([56b4f64](https://github.com/JawadYzbk/rustplus-desktop/commit/56b4f64d27ab9413124a1adf3e184ffae49f6371)) (JawadYzbk)
- **servers**: remove a deleted server's local data ([26b0200](https://github.com/JawadYzbk/rustplus-desktop/commit/26b0200af8cd44a5af7cc0a0c40e0a69c5448c1d)) (JawadYzbk)
- **map**: show monuments on offline maps and lay the rendered map out at its real scale ([893016a](https://github.com/JawadYzbk/rustplus-desktop/commit/893016a11395c79d8df264369df3fb295be55fe2)) (JawadYzbk)
- **map3d**: render the map image for imported offline maps ([a02e4f9](https://github.com/JawadYzbk/rustplus-desktop/commit/a02e4f9a4ffa572a5b42be7c8d6e7dc00c1cc877)) (JawadYzbk)
- **map3d**: reuse cached parse for the current wipe and keep the rendered map image ([ce2cfd5](https://github.com/JawadYzbk/rustplus-desktop/commit/ce2cfd52d466033c6228ad23bca2e88291c35951)) (JawadYzbk)
- **startup**: stop the loading screen from staying on top of other windows ([c6be8fe](https://github.com/JawadYzbk/rustplus-desktop/commit/c6be8feac680000f0d3c2078c14105b6f982b063)) (JawadYzbk)
- **map**: keep the death heatmap readable on long wipes ([1e07d7f](https://github.com/JawadYzbk/rustplus-desktop/commit/1e07d7f844c2ccd1587e22f202f86406b3100abb)) (JawadYzbk)
- **wipe-tracker**: stop drawing teleports as route lines ([fb0a2a5](https://github.com/JawadYzbk/rustplus-desktop/commit/fb0a2a5b4f0ac63bab8ac22e703caca10e0d646b)) (JawadYzbk)

### ⚡ Performance Improvements

- **wipe-tracker**: store long wipes compactly and open them fast ([72a8c08](https://github.com/JawadYzbk/rustplus-desktop/commit/72a8c08246ac98df3b35652fae6a2ab685208548)) (JawadYzbk)

### 👥 Contributors

Special thanks to: @JawadYzbk, @Carsten Podszun

**Full Changelog**: https://github.com/JawadYzbk/rustplus-desktop/compare/v10.2.0...v10.3.0
