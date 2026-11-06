# Changelog

All notable changes to Rust+ Desktop are documented in this file.

## [11.1.1](https://github.com/JawadYzbk/rustplus-desktop/releases/tag/v11.1.1) (2026-11-06)

### 🐛 Bug Fixes & Stability

- **cache**: isolate companion proxy servers and prevent marker announcement freeze ([aeed53b](https://github.com/JawadYzbk/rustplus-desktop/commit/aeed53bc01c8f1ed17878b3ba0929d806675ab6a)) (JawadYzbk)

### 👥 Contributors

Special thanks to: @JawadYzbk

**Full Changelog**: https://github.com/JawadYzbk/rustplus-desktop/compare/v11.1.0...v11.1.1
All notable changes to Rust+ Desktop are documented in this file.

## [11.1.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.1.0) (2026-11-06)

### 🚀 Features & Enhancements

- **pairing**: sync missed server pairings from history and handle proxy-only servers ([5c9939f](https://github.com/Pronwan/rustplus-desktop/commit/5c9939f8519c610a9e2bf82015e6e350b8491b15)) (JawadYzbk)

### 🐛 Bug Fixes & Stability

- **map**: only accept a map file that matches the server's monuments ([bfd1d2c](https://github.com/Pronwan/rustplus-desktop/commit/bfd1d2cf2e6bce307acbb718b049eb9122535f79)) (JawadYzbk)

### 👥 Contributors

Special thanks to: @JawadYzbk

**Full Changelog**: https://github.com/Pronwan/rustplus-desktop/compare/v11.0.1...v11.1.0
# Changelog

All notable changes to Rust+ Desktop are documented in this file.

## [11.0.1](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.0.1) (2026-11-06)

### 🚀 Features & Enhancements

- **models**: keep models outside the install and download them as packs ([a06b035](https://github.com/Pronwan/rustplus-desktop/commit/a06b035daed1ef7d68e160f5bed0342de3b30a98)) (JawadYzbk)

### 👥 Contributors

Special thanks to: @JawadYzbk

**Full Changelog**: https://github.com/Pronwan/rustplus-desktop/compare/v11.0.0...v11.0.1
All notable changes to Rust+ Desktop are documented in this file.

## [11.0.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.0.0) (2026-11-03)

### 🚀 Features & Enhancements

- **builder**: Ship Builder in Tools, Build Together for ships ([2444aba](https://github.com/Pronwan/rustplus-desktop/commit/2444aba9f6362a907ac520fb884364bdff58e0ac)) (Carsten Podszun)
- **builder**: tell a co-op guest when the host grants or takes build rights ([643c3bb](https://github.com/Pronwan/rustplus-desktop/commit/643c3bb4aed1559987f47befaceb136fc16807cb)) (Carsten Podszun)
- **builder**: add refinery and shelf icons ([b5c05fb](https://github.com/Pronwan/rustplus-desktop/commit/b5c05fba06a3882563d596b1ae0ba3af2c2c120e)) (JawadYzbk)
- **builder**: add the spray can item icon ([915f4be](https://github.com/Pronwan/rustplus-desktop/commit/915f4be6f89577109023a18ae6d49cdbe8f5029f)) (JawadYzbk)
- **builder**: relay co-op player positions and offer guests a copy ([162cbff](https://github.com/Pronwan/rustplus-desktop/commit/162cbff88c606e1d8903015445fdf72b4ff93337)) (Carsten Podszun)
- **builder**: carry co-op build changes between host and guests ([18dac39](https://github.com/Pronwan/rustplus-desktop/commit/18dac39df6076390ba3703440ddbeedcb83ac809)) (Carsten Podszun)
- **builder**: connect the Together panel to co-op build sessions ([4029a56](https://github.com/Pronwan/rustplus-desktop/commit/4029a56e3ef9a254347f6de854e4f1a4cc9e35b9)) (Carsten Podszun)
- **builder**: answer co-op build invites from anywhere in the app ([83fe954](https://github.com/Pronwan/rustplus-desktop/commit/83fe954994bb409e576c4c28a6702629ae6ff0d5)) (Carsten Podszun)
- **builder**: add the Wind Turbine item icon ([9d573bf](https://github.com/Pronwan/rustplus-desktop/commit/9d573bf643a924492c85664877f22739831cea05)) (JawadYzbk)
- **builder**: bundle RustHelp icons for the barricades ([b0b9bb3](https://github.com/Pronwan/rustplus-desktop/commit/b0b9bb35d4687f68365cbf2a3f0a0efc08309e57)) (JawadYzbk)
- **builder**: bundle RustHelp icons for the external walls and gates ([d6c8f0d](https://github.com/Pronwan/rustplus-desktop/commit/d6c8f0d1a6cfdf954b474518f5787f11e07c61fd)) (JawadYzbk)
- **builder**: bundle RustHelp item icons for the Base Builder ([2a6f1d2](https://github.com/Pronwan/rustplus-desktop/commit/2a6f1d2d2283041d6a3c6e9ef750f6f3856c5fd8)) (JawadYzbk)
- **tools**: add the Base Builder workspace ([125b398](https://github.com/Pronwan/rustplus-desktop/commit/125b398da38743b86dddea7cb5971f93c89aa4cf)) (JawadYzbk)
- **pairing**: say where to pair in game, and welcome the first paired server ([460eee5](https://github.com/Pronwan/rustplus-desktop/commit/460eee5f9828e521e084021b15597c428e9bd2e6)) (Carsten Podszun)
- **map**: ask before showing the HD map, and let the map image's size be chosen ([28e6244](https://github.com/Pronwan/rustplus-desktop/commit/28e6244c062c1ee557c39a1f37d061dcaaa5f36f)) (Carsten Podszun)
- **map**: relay Builders Sanctuary bases to the 3D viewer ([6e96b5a](https://github.com/Pronwan/rustplus-desktop/commit/6e96b5ae9c913f85159dfca097d89baa5dc06e2f)) (JawadYzbk)
- **map**: sync map toolbar layers with the 3D view ([8e75659](https://github.com/Pronwan/rustplus-desktop/commit/8e75659ac4206b42d5eb5a8367000a5ee926198f)) (JawadYzbk)

### 🐛 Bug Fixes & Stability

- **builder**: keep a Build Together session alive across tab switches and flush the draft on logoff ([921f907](https://github.com/Pronwan/rustplus-desktop/commit/921f9071ae5ca5c3730b2fa5573e422a1412ae1a)) (JawadYzbk)
- **builder**: ask before an invite takes a player away from their own build ([d770fa2](https://github.com/Pronwan/rustplus-desktop/commit/d770fa2deddfa4e2f1a60c9f2613084876f24ac5)) (Carsten Podszun)
- keep map and builder session state in sync ([bf39a36](https://github.com/Pronwan/rustplus-desktop/commit/bf39a368c30ced4309f36919bcf9ecf3df0cbef9)) (JawadYzbk)
- **builder**: wait for the recovery draft, show toasts in the builder, and stop stale map parses ([17f53c4](https://github.com/Pronwan/rustplus-desktop/commit/17f53c4badec2fa2b1c7e6160422b9dc251cc8a4)) (Carsten Podszun)
- **discord**: warn the owner when the cloud pauses an unreachable channel ([a6e8a45](https://github.com/Pronwan/rustplus-desktop/commit/a6e8a456d23897065336078564313995802af1e6)) (Carsten Podszun)
- **builder**: offer the right file type when saving a showcase video ([d468ab5](https://github.com/Pronwan/rustplus-desktop/commit/d468ab57a932ae69740c591ceab9c07ccd60a7ca)) (JawadYzbk)
- **builder**: free the Base Builder's memory when leaving the workspace ([6081448](https://github.com/Pronwan/rustplus-desktop/commit/60814485a6b7c2f47904c8e75b52c40d4d996116)) (JawadYzbk)
- **builder**: make full screen and F11 work in the Base Builder workspace ([fbf3050](https://github.com/Pronwan/rustplus-desktop/commit/fbf305021b173ef1df0e53745ca560103f889629)) (JawadYzbk)
- **builder**: intercept API requests outside the mapped asset host ([3deb16e](https://github.com/Pronwan/rustplus-desktop/commit/3deb16e17d1d59894d0a2e96862ba01d97fcbb4c)) (JawadYzbk)
- **time**: split the day/night countdown 70/10 and measure modded phases ([1aa84b9](https://github.com/Pronwan/rustplus-desktop/commit/1aa84b976aa720d5bd61f691fd7dded6e8ca0dd8)) (JawadYzbk)
- **map**: keep lone deaths faint on the death heatmap ([29318dd](https://github.com/Pronwan/rustplus-desktop/commit/29318dd5ceaa1c783cca64bf1eb64ec96f3b97db)) (JawadYzbk)
- **devices**: calm the action bar and align the filter row ([6d1e24d](https://github.com/Pronwan/rustplus-desktop/commit/6d1e24da8d92f7142c95ad5fad89036fe6a56e76)) (JawadYzbk)
- **patch-notes**: show the v8.0.0 screenshots ([1b086db](https://github.com/Pronwan/rustplus-desktop/commit/1b086db7033dcb9c634b51ea6e3a5f24fc3d349b)) (JawadYzbk)
- **sidebar**: calm connection card and fix status translations ([a703fa0](https://github.com/Pronwan/rustplus-desktop/commit/a703fa0c9d19a83f1202f4d1c3fccb6ed5452baa)) (JawadYzbk)
- **sidebar**: compact connection controls and keep menus open ([ec548f2](https://github.com/Pronwan/rustplus-desktop/commit/ec548f25780567071bebad840c757ff05868c9a5)) (JawadYzbk)
- **sidebar**: cap expanded server list at six rows ([d9ee512](https://github.com/Pronwan/rustplus-desktop/commit/d9ee512667a0e956dd00caf7b85d5e19ce35ab1f)) (JawadYzbk)
- **map**: package 3D viewer models and preserve GLB worker ([d8c0e2f](https://github.com/Pronwan/rustplus-desktop/commit/d8c0e2fcbdeddce7035723bd2c5934223334a965)) (JawadYzbk)
- improve sidebar server management and deletion UX ([fa0938c](https://github.com/Pronwan/rustplus-desktop/commit/fa0938cb0a6ddd37ba78f98d42071ff8e139e807)) (JawadYzbk)
- **tests**: keep the test run out of the developer's own app data ([b098dd3](https://github.com/Pronwan/rustplus-desktop/commit/b098dd3ac97a8c16b26177e47c4ed44acc7a075e)) (Carsten Podszun)
- **overlay**: keep routes on their own server, and let the trash button clear them too ([ed5338b](https://github.com/Pronwan/rustplus-desktop/commit/ed5338b4b9bc61bc0bb6ca790e71703befcf46e0)) (Carsten Podszun)
- **map**: decode the cached server image at the chosen size in the first place ([8924539](https://github.com/Pronwan/rustplus-desktop/commit/8924539ef34c40b8dd51e1c3b4375b961bb282da)) (Carsten Podszun)
- **settings**: stop dropdown text being cut off, and keep the memory log to debug builds ([50f3da0](https://github.com/Pronwan/rustplus-desktop/commit/50f3da0bceeab186e46ef503d91dc24b56b28935)) (Carsten Podszun)
- **build**: pin MapParser restore to its net8 framework ([c9d0f6e](https://github.com/Pronwan/rustplus-desktop/commit/c9d0f6ed651782998160aac4f30b72caa6b4d4f9)) (JawadYzbk)
- **overlay**: keep drawings on the spot they mark when the map image changes ([5193751](https://github.com/Pronwan/rustplus-desktop/commit/51937515cfdd647dc5b44e623ec9972f0c5d7af5)) (Carsten Podszun)
- **minimap**: keep its buttons up for a second after the pointer leaves ([c8ee9c7](https://github.com/Pronwan/rustplus-desktop/commit/c8ee9c7470da6e849b63c58ab1a0d4b521a9b92e)) (Carsten Podszun)
- **map3d**: give the 3D texture the ocean margin when it is the server's image ([6257a49](https://github.com/Pronwan/rustplus-desktop/commit/6257a492e4bffeafec6be751585ce5565b2629c7)) (Carsten Podszun)
- **map**: lay out the server's map image with its ocean margin after a render is deleted ([f071fcb](https://github.com/Pronwan/rustplus-desktop/commit/f071fcbb37e48143a80892d60f8a355256f6bf0b)) (Carsten Podszun)
- **map**: keep the 3D viewer at 100% zoom ([bcae031](https://github.com/Pronwan/rustplus-desktop/commit/bcae0319e4beaddea9110e777b7e0bb4d6e0f824)) (JawadYzbk)
- **map**: restore native 3d input and viewport rendering ([77cd2ba](https://github.com/Pronwan/rustplus-desktop/commit/77cd2ba42fdc0af697b6ce722e9adc5ccc270838)) (JawadYzbk)
- keep minimap settings reachable across transparent corners ([4da02f9](https://github.com/Pronwan/rustplus-desktop/commit/4da02f94863beae24f741e792c411110d7984172)) (JawadYzbk)
- continue map grid across the viewport ocean ([6b0d5ab](https://github.com/Pronwan/rustplus-desktop/commit/6b0d5ab25560c965419f10ce22b8e38ff8e08cfc)) (JawadYzbk)
- replace connect prompt with a single loading card ([c3cd45f](https://github.com/Pronwan/rustplus-desktop/commit/c3cd45f5f467576cba811dad1d5cca92a7e1e42a)) (JawadYzbk)
- reveal shops and events only after server data ([4493e17](https://github.com/Pronwan/rustplus-desktop/commit/4493e17dd83df1c31d99a99fe721421ae60d5732)) (JawadYzbk)

### ⚡ Performance Improvements

- **map**: drop the heatmap's raw raster when the heatmap is turned off ([598c88c](https://github.com/Pronwan/rustplus-desktop/commit/598c88c3b2a04d21e8efebf5807bcecdc38d38cc)) (Carsten Podszun)
- **map**: stop parsing the whole 90 MB map_data.json for the 2D map ([f5fc64a](https://github.com/Pronwan/rustplus-desktop/commit/f5fc64a64307126879b425204d5c1aff64909aec)) (Carsten Podszun)
- cache grid label pixels across zoom frames ([6c42d0c](https://github.com/Pronwan/rustplus-desktop/commit/6c42d0cb5e51cd2e77c561bd3e8b51bebef64e3c)) (JawadYzbk)
- reduce follow and zoom work on map overlays ([8b24b78](https://github.com/Pronwan/rustplus-desktop/commit/8b24b786099c3d997942fd00332c8759aab07877)) (JawadYzbk)
- avoid duplicate map and reference layer rebuilds ([2ff02ab](https://github.com/Pronwan/rustplus-desktop/commit/2ff02ab8dd3b09ab88c9873669cad61879db63b9)) (JawadYzbk)
- render accurate map grids with cached vectors ([c2285a8](https://github.com/Pronwan/rustplus-desktop/commit/c2285a8b65956a347a3b254cc8fb7a58bb19f946)) (JawadYzbk)

### 👥 Contributors

Special thanks to: @Carsten Podszun, @JawadYzbk

**Full Changelog**: https://github.com/Pronwan/rustplus-desktop/compare/v10.3.0...v11.0.0
# Changelog

All notable changes to Rust+ Desktop are documented in this file.

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
# Changelog

All notable changes to Rust+ Desktop are documented in this file.

## [10.2.0](https://github.com/JawadYzbk/rustplus-desktop/releases/tag/v10.2.0) (2026-10-14)

### 🚀 Features & Enhancements

- **servers**: add smart server re-pairing, device transfer & connection editor dialogs ([f732db1](https://github.com/JawadYzbk/rustplus-desktop/commit/f732db1f8efa28dc6a7b7d908a4fb1d0b0bbab14)) (JawadYzbk)
- **ui**: refine pairing controls layout and bundle newly cached item icons ([cd91fad](https://github.com/JawadYzbk/rustplus-desktop/commit/cd91fadd81142db3cdd14aa44ac77fd0826d0514)) (JawadYzbk)
- **genetics-lab**: sync the app tutorial ([f242eb1](https://github.com/JawadYzbk/rustplus-desktop/commit/f242eb13a5fa743128da3593cc6b7c57e30abaa6)) (Claude)
- **genetics-lab**: check for an update every time the tab is opened ([4932b0e](https://github.com/JawadYzbk/rustplus-desktop/commit/4932b0eb13aae6b168aebc461315e864cdfbeee0)) (Claude)
- **genetics-lab**: sync clone moving, misread correction and plant-name search ([d497ae7](https://github.com/JawadYzbk/rustplus-desktop/commit/d497ae7ed6d7b12a8ad8f4a8574131a96ce5b74b)) (Claude)
- **genetics-lab**: sync plant groups manager and plant-type voting ([c9035d9](https://github.com/JawadYzbk/rustplus-desktop/commit/c9035d9d91cd6125c833dee1aa5d58aa132924ed)) (Claude)
- **genetics-lab**: Patch notes button in the Genetics Lab header ([91bdcf4](https://github.com/JawadYzbk/rustplus-desktop/commit/91bdcf4b413d64534b5dd63d2712201a16f87dcd)) (Claude)
- **genetics-lab / breeding**: show the guided breeding step in a 3D planter; fix black plant leaves ([2998217](https://github.com/JawadYzbk/rustplus-desktop/commit/2998217c101d0ec41fd8a3cb0315562a60b1809b)) (Claude)
- **genetics-lab / guide**: interactive 3D planter guide with Rust's own planter and plant models ([b971d42](https://github.com/JawadYzbk/rustplus-desktop/commit/b971d4213b508cd5d94b34bb38de459618ede340)) (Claude)
- **genetics-lab / updates**: open patch notes at any time ([420bbbc](https://github.com/JawadYzbk/rustplus-desktop/commit/420bbbcba8a1e3832ecaec6954e777480a7370d6)) (Claude)
- **genetics-lab**: update Genetics Lab in place, independent of desktop releases ([06e9ec3](https://github.com/JawadYzbk/rustplus-desktop/commit/06e9ec3ccb5ca831278be3c41fdb8d68c1d9a6c1)) (Claude)
- **genetics-lab / updates**: ship update manifests and show updates inside Genetics Lab ([57ce273](https://github.com/JawadYzbk/rustplus-desktop/commit/57ce273233af70e875bb0664b2fbfc96f44f2656)) (Claude)
- **genetics-lab / recipes**: correct every recipe and add the livestock-update pies and breads ([f28d47d](https://github.com/JawadYzbk/rustplus-desktop/commit/f28d47ddf520ad1d74406aee8c9716e446cc2012)) (JawadYzbk)

### 🐛 Bug Fixes & Stability

- **genetics-lab**: auto-calibrate finds the gene row at small UI scales ([2663e61](https://github.com/JawadYzbk/rustplus-desktop/commit/2663e61c3791896b5c75707ee21a386f82b81879)) (Claude)
- **genetics-lab**: keep checking for Genetics Lab updates while the app runs ([5fbcb53](https://github.com/JawadYzbk/rustplus-desktop/commit/5fbcb53565cfc0071eb48983afbfbde2c608e8e6)) (Claude)

### 🔄 Submodules & Genetics Lab Updates

- **genetics-lab**: plant type detection, per-plant gene lists, guided breeding fixes ([5c1904d](https://github.com/JawadYzbk/rustplus-desktop/commit/5c1904dd429bd7112c59b44a8bca0218662ec3bf)) (Claude)
- **genetics-lab**: plant picker, session plant type, 3D guide follows your route, berry colours, README ([2dfb965](https://github.com/JawadYzbk/rustplus-desktop/commit/2dfb9654dcc0ef345888b4ba1f09bd0694edf384)) (Claude)

### 👥 Contributors

Special thanks to: @JawadYzbk, @Claude

**Full Changelog**: https://github.com/JawadYzbk/rustplus-desktop/compare/v10.1.2...v10.2.0
