# 🚀 Rust+ Desktop v11.3.0 — Custom Keybinds, Smarter Chat Commands & Cloud 24/7 Automations

Hey @everyone! 🎉

**v11.3.0** is out: your own keys for the 3D map and the builders, chat commands that work the same in the app and the cloud, and a Cloud 24/7 that keeps your rules, automations and Discord alerts running while the app is closed.

---

### 🎮 For everyone

#### ⌨️ Custom Keybinds
* **Your keys, everywhere**: Change any key of the 3D map, the Base Builder and the Ship Builder. Click the ⌨️ icon next to *Controls*, or *Keys* in the builder toolbar.
* **Hints that match**: The on-screen controls always show your keys, in your own keyboard layout (AZERTY players: hello 👋).

#### 💬 Chat Commands
* **New**: `!online` lists the whole team and who is online, `!deaths` shows the latest team deaths.
* **Cleaner `!commands`**: only lists what can actually answer, and long lists are split instead of cut off.
* **Retired**: `!cargo`, `!heli`, `!vendor` and `!deepsea` are gone, since Rust no longer sends those events. `!oilrig` stays wherever an oil rig trigger is set up.

#### 🤖 Logic Engine Messages
* **Rules that report back**: New steps *Send to Team Chat*, *Send to Clan Chat* and *Send to Discord*, so a rule can tell your team when the turrets are on.

#### 🛠️ Fixes
* A paired TC monitor no longer queries the game every few seconds.
* The mini-map key keeps working while you're in game.
* Routine background messages no longer flood the console.

---

### ☁️ Cloud 24/7 (Premium)

* **Your rules keep running**: Logic Engine rules and Device Automations (player nearby, in-game time) run in the cloud while the app is closed, and hand back to the app the moment you open it.
* **Same commands in the cloud**: `!online`, `!afk`, `!upkeepdetail`, `!oilrig` and your own rule commands answer the same way with the app closed.
* **Door codes in the cloud**: Tick *Door codes in Cloud 24/7* in the chat commands and the cloud can answer your code command too. Stored encrypted, and unticking it removes them.
* **Discord with the app closed**: Raid, crate and death alerts go straight to your Advanced Discord Bot, and team chat plus online/offline alerts reach its chat channel. The extra Discord webhook in *Offline Alerts* is no longer needed.
* **Oil rig triggers fixed**: An alarm set as an oil rig trigger is no longer mistaken for a raid, so it won't ring your Telegram call, and it's announced only once.
* **No more repeated messages**: Closing the app no longer makes the cloud repeat online, offline or death messages your team already saw.
* **Login reminder**: Your Rust+ login expires after about two weeks. When it does, the app, the website and a one-time Discord message remind you to log in again, so your cloud never goes quiet without you knowing.

---

### 📥 Download & Update
* If you have Rust+ Desktop running, it will automatically download and install updates via the built-in updater.
* Or download the latest build directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.3.0](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.3.0)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧
