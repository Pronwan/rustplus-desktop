## [11.0.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.0.0) (2026-11-03)

### ✨ Highlights: the Base & Ship Builder

- **The Ship Builder (new).** Build boats on a Boat Building Station with every boat piece and fitting: hulls, floors, walls, stairs, steering wheel, sails, small engines, anchor, cannons, plank and small ramp. Hulls sit square to the station's grid, as in the game. Fit the boat out with ladder hatches, doors, lockers, boxes, repair benches, barricades, batteries, switches, tesla coils and shotgun traps. Up to three ships per plot.
- **The Ship Simulator (new).** Top speed, drag and course come from the game's own boat physics (a pointed bow is faster than a flat one), and the station panel also shows fuel use, health and cost. A Course overlay shows where the boat goes with the wheel left alone, and why. Set the sails, start the engines and drop the anchor all at once or one by one with E. Then take the wheel and sail it, with collisions against the shore and other boats.
- **Build Together (Premium), for bases and ships.** Invite friends from your friends list or by Steam ID. Give or take build rights per guest and see everyone as a player figure. When someone takes the wheel, the others see them steering and the boat moving. Guests join for free.
- **The Base Builder.** Build under the game's own rules with every grade and DLC skin. Furnish the base, including the new batteries, switches, tesla coil and shotgun trap, which show their reach while you place them. Walk through it on foot, and see its TC range and its building and upkeep costs.
- **God Rock, iceberg and cliff bases.** Plan bases on the game's large rocks, cliffs and icebergs: add them to your plot under Scene, or build against them on the 3D map, and the base keeps them. On another server's 3D map the app lists the same structures, flies you to them and checks every piece by the game's rules, so you see whether the base fits or needs adjusting.
- **Import, export and the real map.** Open Builders Sanctuary designs and CopyPaste or Fortify files, export bases as CopyPaste (uMod/Oxide) for real Rust servers, and place them on your server's 3D map with a terrain fit check.

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
