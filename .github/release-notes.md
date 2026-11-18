## [11.4.2](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.4.2) (2026-11-18)

### 🚀 Features & Enhancements

- **chat**: !oilrig says how long ago the crate unlocked ([e8be9b4](https://github.com/Pronwan/rustplus-desktop/commit/e8be9b4e5f7671cbc87aafce35d7ce2f37ad5f0b)) (Pronwan)
- **alerts**: keep the server's title in server alerts, after our label ([ccee8f7](https://github.com/Pronwan/rustplus-desktop/commit/ccee8f72a4079b3a92da162ec30471fd4b2e4619)) (Pronwan)
- **alerts**: server alerts post their text and a short server line ([4a85674](https://github.com/Pronwan/rustplus-desktop/commit/4a856743fe372c5723e3d960acef756c86dbb19b)) (Pronwan)
- **map**: reparse cached maps made by an older MapParser ([8532761](https://github.com/Pronwan/rustplus-desktop/commit/8532761debd5e0e3f08b213b36a5368dad8c2af9)) (JawadYzbk)
- **alerts**: Modded Server Alerts setting, synced with the account ([5553877](https://github.com/Pronwan/rustplus-desktop/commit/5553877c330232b0dd70005631ce1009edbd7950)) (Pronwan)
- **discord**: optional Server Alerts channel for modded servers ([8159d80](https://github.com/Pronwan/rustplus-desktop/commit/8159d80353e98bdbfb7c9fd52f4d3dbb452c68de)) (Pronwan)

### 🐛 Bug Fixes & Stability

- **connection**: keep retrying when map polls find the connection dead ([55b5fd7](https://github.com/Pronwan/rustplus-desktop/commit/55b5fd72c3e0b506bd51ee37c59e3e58cbc5e105)) (Pronwan)
- **discord**: post as master only while the platform's master lease holds ([a167f73](https://github.com/Pronwan/rustplus-desktop/commit/a167f73778d48f6cc01da5f1be1130be809d6024)) (Pronwan)
- **oilrig**: keep the crate timer across a reconnect to the same server ([a6694f5](https://github.com/Pronwan/rustplus-desktop/commit/a6694f59f43e745c907b7bd4a554a62d3308a16f)) (Pronwan)
- **connection**: keep retrying a lost connection instead of pausing after one attempt ([f9ebaac](https://github.com/Pronwan/rustplus-desktop/commit/f9ebaac787e71dcc4e093d102e5fdbace3bc9647)) (Pronwan)
- **rustmaps**: keep the RustMaps button enabled when a RustMaps map is known ([383a1f5](https://github.com/Pronwan/rustplus-desktop/commit/383a1f5686ec5ce72a305981ea661458ac0aa5ce)) (JawadYzbk)
- **ui**: restore readable connection card description color ([4e36494](https://github.com/Pronwan/rustplus-desktop/commit/4e36494041b8ccb34a2cf8fa89953378fcf64739)) (JawadYzbk)
- **connection**: clarify companion status and bound recovery retries ([4c80946](https://github.com/Pronwan/rustplus-desktop/commit/4c80946bfccbbe7228886022128bc3c07960a901)) (JawadYzbk)
- show device progress only for actual device work ([c871132](https://github.com/Pronwan/rustplus-desktop/commit/c871132d2a68cf6416ab7f9698a566fd15f14879)) (JawadYzbk)
- restore automatic soft connect and reveal cached maps immediately ([f5b68a8](https://github.com/Pronwan/rustplus-desktop/commit/f5b68a8fbc0c4f87ebbe08ecd250b0802038267f)) (JawadYzbk)
- clarify connection modes and smooth server previews ([5deb130](https://github.com/Pronwan/rustplus-desktop/commit/5deb130f695a0ba9463fdbdf9289868b1267ec2e)) (JawadYzbk)
- decouple cached map loading from device setup ([dd22ecf](https://github.com/Pronwan/rustplus-desktop/commit/dd22ecf6de6d996c4d71005b911ab92f8049ff0d)) (JawadYzbk)

### 👥 Contributors

Special thanks to: @Pronwan, @JawadYzbk

**Full Changelog**: https://github.com/Pronwan/rustplus-desktop/compare/v11.4.1...v11.4.2
