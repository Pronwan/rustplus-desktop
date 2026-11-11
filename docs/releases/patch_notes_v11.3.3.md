# 🚀 Rust+ Desktop v11.3.3 Hotfix — Pairing Sync Persistence, Device Controls & Discord Feedback

Hey @everyone! 🎉

We've rolled out a follow-up hotfix, **v11.3.3**, fixing device/server deletion persistence, restoring full controls for devices recovered from pairing history, and refining chat command routing with "Discord only" mode!

---

### 🌟 Key Highlights & Fixes

#### 🔌 Devices & Pairing Sync
* **Deleted Devices Stay Deleted**: Pruning missing devices or removing paired items no longer brings them back on the next launch or notification history sync.
* **Full Device Controls on Restored Devices**: Smart switches and devices restored from Facepunch pairing history now have proper controls, switch toggles, and chat command pills.
* **Delete Available Devices with Fluent Confirmations**: You can now delete any device directly from the list, backed by a modern Fluent UI confirmation dialog when deleting active devices.
* **Selective History Restore Picker**: When restoring previously removed devices, a picker dialog lets you review, probe, and select exactly which devices to bring back.
* **Wipe & Server Deletion Persistence**: Removing a server properly retires old pairing notifications so old-wipe servers don't reappear.

#### 💬 Chat Commands & "Discord Only" Mode
* **Command Replies & Hotkey Feedback**: Replies to in-game chat commands and hotkey status notifications now reliably reach the game even when "Discord only" is active.
* **Webhook Routing for Servers Without Team Chat**: When "Discord only" is enabled on servers lacking team chat, you can now selectively route command responses, hotkey feedback, and Logic Engine messages directly to Discord.

---

### 📥 Download & Update
* If you have Rust+ Desktop running, it will automatically download and install updates via the built-in updater.
* Or download the latest build directly from GitHub Releases:
👉 **[Download Rust+ Desktop v11.3.3](https://github.com/Pronwan/rustplus-desktop/releases/tag/v11.3.3)**

*Found any bugs or have feedback? Let us know in <#bug-reports> / <#feedback>!* 🦀🔧
