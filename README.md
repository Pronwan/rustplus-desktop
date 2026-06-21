![Headline](RustPlusDesktop/Assets/Images/headlineGIT.jpg)  
[![Discord](https://img.shields.io/badge/Discord-Rust²%20|%20Rust%2B%20Desktop-5865f2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/v4X584wye4)


[![Donate](./RustPlusDesktop/Assets/Images/donate.png)](https://www.patreon.com/c/Pronwan)


# Rust+ Desktop App (Unofficial)


⚠️ **Note**: This is an **unofficial** project and is not affiliated with Facepunch Studios or the game *Rust*.  

It is open source so anyone can verify there is **no malware or hidden components**.

⚠️ **Note**: If you used it for a while and can't pair new servers anymore, simply click on the Pairing button with right mouse button and select to delete the config file.

---


## 🔍 What is this?



The **Rust+ Desktop App** is a Windows application built on the official Rust+ Companion API.  

It lets you pair Rust servers, monitor in-game events, control Smart Devices, and view dynamic map markers — all on your PC.
By now it's more than 'just' Rust Plus. It's Rust² you could say... That's why this is our new icon ;) Was about time.
![Update](./RustPlusDesktop/Assets/Images/icon.png)  


The app ships as a single installer (bundling .NET, Node.js, WebView2 runtime, RustPlusAPI, etc.), so you don’t have to install dependencies manually.



---



## 🚀 Latest Release



➡️ **[Download the latest RustPlusDesk-Setup.exe](../../releases/latest)**


*(I publish the signed/packaged installer as a GitHub Release asset for clean versioning and smaller repositories.)*

[![YouTube V6](./RustPlusDesktop/Assets/Images/6.0%20Cloud%20Patch/Thumbnail_RustPlus6_V6.jpg?raw=true)](https://www.youtube.com/watch?v=Ywv0hjE8nAI)
[![YouTube V5](./RustPlusDesktop/Assets/Images/RustPlus_V5_Thumbnail.jpg?raw=true)](https://www.youtube.com/watch?v=wrqGoTCtAjs)

[![YouTube Video](./RustPlusDesktop/Assets/Images/RustPlus_V4_Thumbnail.png)](https://youtu.be/tmbAn3lIKmM)  
*(click the image to watch on YouTube)*

# 🛠️ 7.0.2 Hotfixes & Improvements:

- **Seamless 3D Map Setup:** We bundled the .NET 8 Runtime directly into the MapParser.exe. It now works perfectly out-of-the-box on all PCs without requiring any manual framework installations!
- **3D Map Visuals:** Added new and improved ground textures for the 3D Maps. 🗺️
- **MapParser BufferOverflow:** Fixed a bug where the MapParser could get stuck in an endless loop due to a BufferOverflow.
- **3D Map Write Protection:** Fixed an issue where building the 3D Map failed due to Windows write protection. Map cache is now safely stored in %APPDATA%.
- **Storage Optimization:** Added automatic cleanup of temporary MapParser logs and cache files upon successful 3D map extraction to save your precious disk space. 🧹
- **Discord !heli Command:** The Attack Helicopter status command now automatically includes the current [Grid] where the Heli is located! 🚁
- **Account Verification & Password Reset:** Added an in-app password reset option and finally fixed the issue where account verification emails wouldn't send properly. 📧
- **☁️ Cloud Domain Migration:** We have officially merged our Cloud service to a new, dedicated domain: www.rustplusdesktop.cloud! Our new official support email is now: **mail@rustplusdesktop.cloud**
.

# 🚀 UPDATE v7.0.0 | 3D Maps, Base Footprint Builder & Enhanced 2D Maps
The new version is now available! Simply restart your app or download the latest update.

**🗺️ Interactive 3D Maps** Explore your Rust server like never before with our fully interactive 3D Maps! Includes live player positions and death markers to help you strategize and locate loot.

**🔍 Optional Map File Parsing** You can now optionally read your local .map files (safely, as read-only) by clicking the 3D Map button to extract precise in-game details. This brings exact Building Blocked Zones, Caves, Icebergs, and more directly to both your 3D and 2D maps! (And don't worry, the 3D map unloads completely from RAM when closed to save resources.)

**🏗️ 3D Base Footprint Builder** Planning a new base? Use the new Footprint Builder in the 3D map to preview exactly how your base will fit. Test if it fits between No-Build Zones, check slope steepness, or verify water depth before you build in-game.

**⚙️ Refined Logic Engine** Our Logic Engine has been further refined for better performance and stability when constructing custom automation rules.

**📍 Enhanced Map Features** In-game Team Markers are now natively displayed on the 2D map. Plus, we've added beautiful custom icons for all Smart Devices!

**♻️ Recycling Calculator** Recycling Calculator for your Components and more. See the difference between safe-zone and regular recycling right away.

# Rust+ Desktop Update — v6.3.1 :rocket:**
This update introduces a powerful new automation engine, optimized map controls, and massive optimizations to data usage and performance. 

:Warning: **NOTE:** **It's a mandatory update for cloud features because we changed overlay from polling to subscribing / streaming on demand.**

**:brain: 1. Smart Logic Engine (Beta) :control_knobs:**
The highlight of this update! Easily build your own automated workflows:

- IFTTT-Style Rule Builder: Construct rules using an intuitive logic editor with custom delays and conditional actions.
- Triggers: Activate rules via Smart Alarms, Smart Switches, or custom Chat Commands (e.g., !raid), which sync automatically with your Chat Settings overlay.
- Logical Gating: Subsequent steps in a rule function as gating conditions. Execution only continues if the condition (like checking device availability/state) is met. :bulb: 

**Pro-Tip:** This allows you to use unpowered Smart Switches as conditional inputs for your automation flow!
__Future Roadmap:__ Upcoming updates will feature even more triggers, advanced conditions, and cloud sharing to easily distribute rules within your team!

**:map: 2. Teammate Overlay Subscriptions & Left Dock :busts_in_silhouette:**
Managing your team's live map views is now easier than ever:

The new Subscription Dock is placed on the left side of the map sidebar.
- Visual Feeds: Pulse-glow indicators show active teammate map subscriptions.
- Quick Interactions: Updates will come via Live-Feed, Screenshots for Bases are now correctly shared and you can  subscribe to up to 5 Teammates at a time. 
- Hover Subscription Dock to quickly unsubscribe, and access the map drawing overlay mode.

**:chart_with_downwards_trend: 3. Adaptive Polling & Egress Optimization :zap:**
Massive traffic reduction for both the client and servers:

- Replaced static 3-second polling with an intelligent back-off strategy and Live Feeds

**:camera_with_flash: 4. Base Screenshot Indicators & Permission Fixes :european_castle:**
Improved base management on the map overlay:

- Screenshot Indicators: Bases on the map overlay that have screenshots attached now feature small visual indicators, allowing you to instantly identify which bases have uploaded references at a glance.
- Permissions Fix: Fixed a permission bug that was preventing team members from viewing screenshots uploaded to bases by their teammates.

# ⚙️ **RUST+ DESKTOP UPDATE — VERSION v6.3.0** ⚙️

A new update is here! This release brings long-awaited live camera & turret controls directly to your desktop, modernizes smart device management, and ensures maximum layout flexibility for all screen resolutions.

🎮 **NEW FEATURES & IMPROVEMENTS**

🔧 **Remote Camera & Turret Controls (Live Steering)**
* **Interactive Control:** Full remote control integration for Auto-Turrets and PTZ cameras directly within the desktop app.
* **Mouse Steering:** Smoothly pan and tilt cameras by dragging directly on the live image (sensitivity has been fine-tuned for precise adjustments).
* **Continuous Button Inputs:** Press and hold D-pad buttons for continuous steering or action execution (fire, reload, jump, duck).
* **New Borderless Window:** The camera view now opens in a sleek WPF borderless frame (fully draggable and dynamically resizable while preserving the image aspect ratio).
* **Interactive Tiles:** Simply click directly on the camera thumbnail in your dashboard to pop out the live stream window.

🎨 **Custom Smart Device Icons**
* You can now assign custom icons to your smart devices in the Device List for a cleaner overview and quicker identification at a glance.

📐 **Flexible Window Layout & Resolution Support (Stretched Res)**
* Drastically reduced minimum width constraints (Minimum window width: 1000px, Map side minimum width: 400px).
* This allows players using lower resolutions or stretched aspect ratios (like 4:3) to comfortably scale and fit the application on their displays.

📱 **Premium WPF Device List Layout**
* Overhauled the smart device dashboard with a modernized WPF-styled list design, providing a cleaner grid view and faster switch control access.

💤 **AFK Status & Command-Delay**
* Easily identify inactive players! Added AFK status indicators to player cards.
* Custom settings overlays for Team Chat styles alongside customizable command execution delays to prevent spam rate-limits.

🛡️ **Hardened Discord Polling Connection**
* Resolved a database keep-alive issue. Optimizations to the polling database connection now prevent the Discord bot integration from disconnecting after two hours of inactivity.

# **🚀 RustPlusDesktop — Patch v6.1.1 (Hotfix & Optimization)**




# Rust+ Desktop v6.0.2 - Hotfix Update
**🐛 Bug Fixes**

- Event Notifications: Fixed an issue where event notifications (e.g., Deep Sea, Cargo Ship, Patrol Heli) weren't showing up correctly. They now correctly appear in the Event Channel instead of just Oil Rig and Timers.
- Trade Alerts: Trade alerts are now loaded correctly.
- Hotkeys: Fixed an issue causing "ghost" hotkeys.
- Discord Bot: Fixed soft-connect Discord raid alerts.
- Server Optimization: Fixed a server-side memory leak by instantiating the Supabase clients globally in the edge functions.

**✨ New Features & Improvements**
- Player Direction Indicator: Added a direction indicator for the movement direction of players on the map. This feature can be toggled in the Team tab.
- Team Markers Settings: Added a new Team Markers section to the main application settings overlay.
- UI Improvements: Improved the layout of the UI timer section. Matched the events dock and custom timer button backgrounds to the in-game time overlay.
- Discord Bot: Translated various hardcoded bot error messages to English for better consistency.

# 🔥 Rust+ Desktop v6.0.1 Hotfix
- Added TTS support for Discord webhooks
- Reworked custom timers with configurable 1-minute countdown and time-up alerts
- Added a separate Advanced Bot channel for shop alerts
- Fixed stale Advanced Bot channel database entries
- Discord Raid Alerts now work during Soft Connect
- One !switch command can now trigger multiple devices
- Improved the Discord webhook setup window to prevent accidental closing

# 🚀 Rust+ Desktop v6.0.0 - The Cloud & Intelligence Update!

We are incredibly excited to announce our maybe biggest update ever! Version 6.0.0 (which includes all the great changes from our unreleased 5.5 update) brings a completely new Cloud Backend and Advanced Discord Integration to Rust+ Desktop.

IMPORTANT: Rust+ Desktop remains completely FREE! We want to assure everyone that the app and its core features, including the new basic cloud features, are entirely free to use. We have simply restructured how data is synced to open the path for all the amazing new team features to come.

Here is a quick breakdown of how the new tiers work:

**🏠 1. Local Setup (No Account - FREE)**
Your data stays 100% local on your PC. This is exactly how the app used to work if you were playing solo.

❌ No Cloud Sync or Backups
❌ No Device Sharing with teammates
❌ No Web Portal Access

**☁️ 2. Free Cloud Account (FREE)**
By simply creating a free account via Email or Discord, you unlock our new cloud synchronization! This replaces the old "Free Version" and gives you:

✔️ Device Sharing: Sync and share up to 10 smart devices across your team.
✔️ Map Sharing: Share your map overlay (300 KB, 2 Bases, 1 Base-Screenshot).
✔️ Basic Discord: Get basic webhook alerts sent straight to your Discord server.
✔️ Web Portal Access: Manage your account via our new dashboard at rustplusdesktop.onrender.com/dashboard.

**⭐ 3. Supporter Cloud**
For clans, large groups, and power users, the Supporter Tier unlocks the ultimate Rust+ experience!

🌟 Pro Map Sharing: Up to 3 MB of overlay data, 10 Bases, limitless devices and 5 Screenshots per base.
🌟 Chat Master System: One person's client acts as the "Master" to prevent duplicate chat commands and raid alerts for the whole team. It intelligently hands off master status to another team member when the app is closed!
🌟 Advanced Discord Bot: A bidirectional Discord Bot with custom command permissions, raid alerts, and event queries directly from your Discord server.
🌟 Early Beta Access: Get early access to test versions via our Discord.

Here's our new [Rust+ Cloud Dashboard](https://rustplusdesktop.onrender.com/) for free and Supporter Roles (you can also create an account or link discord through the app)


# Rust+ Desktop v5.5.0: Custom Timers & Shop Overhaul 🚀
Welcome to **v5.5.0**! This update introduces highly requested custom timers, a completely overhauled in-app shop, and several quality-of-life improvements for the mini map and death markers.

## ✨ New Features & Improvements
### ⏱️ Custom Timers & Chat Alerts
- **Create your own Timers:** Easily set up custom timers directly via the UI or by using the new `!timer [name],[minutes]` chat command.
- **Smart Chat Alerts:** Never miss a beat! Configure automated chat alerts that trigger at 60, 30, 10, or 3 minutes before a timer expires.
- **Active Timer Overview:** Running timers are displayed directly in your chat command window and on the map overlay for quick access.
### 🛒 Huge Shop Overhaul
- **Brand New Shop UI:** The in-app shop interface has been completely redesigned from the ground up.
- **Advanced Filtering:** You can now easily filter vending machine items by price and resource type.
- **New Trade Designs:** Enjoy the brand new "Profit Trade" and "Buy X" visual layouts, making it easier than ever to spot the best deals on the server.
### 🗺️ Map & Death Marker Upgrades
- **Local Death Markers:** You can now customize the exact amount of death markers shown for yourself and your teammates. Death markers are now always saved locally.
- **Background Mini Map:** The mini map will now continue to function and update perfectly even when the application is minimized to the background!
## 🌍 General
- All new UI elements and alerts are fully localized and available in most languages already.
---
*As always, thank you for your feedback and bug reports! Update directly in the app or download the latest release below.*

## 🚀 Patch 5.4.0 is live! 🌍 Global Localization & more
**30+ Languages:** The app now supports over 30 languages with instant, on-the-fly switching.


**🎨 UI Modernization & Overlays**
New Sidebars: Replaced bulky pop-up windows with sleek, modern sidebar overlays for settings.
Cleaner chat views: Chat commands and alerts now open in isolated views to prevent chat clutter.
Your own Map Marker is now slightly darker than your teammates' markers for better visibility.

**🚨 Smart Alert Rework**
Audio alerts can now loop! The mandatory in-app alert popup now acts as your "stop" button when closed.

**🛡️ Data Management & Security**
Backup & Restore: You can now easily backup and restore your server profiles, tracked players, and drawings to an encrypted file.
Granular Reset: Selectively wipe specific app data (e.g., just the cache or pairing config) without needing a full reinstall.
Improved background processes to eliminate UI freezes during state transitions.

**🏗️ Storage & Smart Home**
Fixed the annoying "0 items" Storage Monitor bug – upkeep and items now sync immediately upon connecting!
Improved accuracy for the !upkeep and !upkeepdetail commands.

**🛠️ Bug Fixes & Stability**
Fixed race condition crashes when rapidly switching servers or opening the player window.
Player lists now populate instantly upon "soft connecting".
Fixed infinite loading spinners, broken UI flags, and search filters.

## 🛠️ v5.1.0 🛠️

The "Game Changer" just got a lot more polish. This beta is all about workflow and stability.

What’s new in this version?

- **📦 Shop Integration:** No more popups! Arbitrage and "Buy X" are now built directly into the Shop Detail Panel.
- **⏳ Precise Timers:** Live countdowns for all events in the dock + chat commands to query event status.
- **🗺️ Minimap Upgrade:** Added Circle/Square/16:9 layouts, opacity slider, and a real-time server clock!
- **🏗️ Builder Heaven:** Added !upkeepdetail for granular 24h upkeep reports from your Storage Monitors.
- **🔔 Smart Alerts:** New In-App popup alerts and "Alarm to Team Chat" options.
- **🛡️ Lag Protection:** Increased handshake timeout (15s) and added a chat-alert delay to prevent spam on laggy API servers.
- **⚠️ Player Tracking Overhaul:** The player Tracking System has been changed completely. Please review the changes in Patch Notes in-app to understand what's happening.

## 🚀 Rust+ Desk v5.0  – Game Changer Update!
This release is packed with architectural hardening by @JawadYzbk & significant QoL upgrades.

**🛠️ Core & System**
- Auto-Update System: No more manual downloads! The app now features a background update checker with detailed progress reporting (speed, size, percentage).
- Centralized Runtime Management: More reliable detection and management of Node.js and background CLI processes.
- FCM Hardening: A complete architectural rewrite of background communication to eliminate UI freezes and crashes during connection transitions.

**🗺️ Interface & Map**
Modern Edge-to-Edge Design: A sleek, borderless layout for a truly modern look.
Interpolated Map Movement: Silky-smooth real-time tracking for players and map events.
BattleMetrics Refactor: The BM button has been upgraded to a clean icon and moved directly onto the server connection card.

**👤 Player & Team Tracking**
- Advanced Player List: Rebuilt from the ground up for better performance and styling.
- Custom Grouping: Organize your tracked players with custom Group Names and Colors.
- Live Indicators: Added "Is Online" status and Play Time counters directly to the player cards.

**🏠 Smart Home & Automation**
- Storage Monitor Support: Automatic recognition of Storage Monitors for chat commands.
- Enhanced !Upkeep: Now supports an arbitrary number of connected Tool Cupboards simultaneously.
- Smart Alarm Fix: Audio alerts for Smart Alarms are now working reliably again.

**🛡️ Security & Identity**
- FCM Token Expiry Tracking: The app now proactively warns you via a new sidebar InfoBar before your pairing expires.
- Steam Identity (Preview): New Steam profile integration in the sidebar, laying the groundwork for future account-linked features.

**🛠️ Bug Fixes:** We fixed a LOT of smaller bugs, issues, improved the UI dramatically, the Battlemetrics integration etc. 

## Update 4.5.1 — The Intelligence Update (May 12th) + Shop Polling Hotfix
<img width="595" height="331" alt="grafik" src="RustPlusDesktop/Assets/Screenshots/4.5.0.png" />

**📍 Smart Map Follow**
- **Player Tracking**: Lock the camera onto any teammate or yourself. The map smoothly centers on the target, making it ideal for tracking raids or roams in real-time.

**💬 Chat Command Automation**
- **Switch Control**: Control your base from anywhere by assigning aliases to Smart Switches. Use !toggle, !on, or !off in team chat.
- **Direct Setup**: Manage all your chat command bindings directly within the Team Chat Overlay.

**🛡️ Stability Overhaul**
- **Reliable Sync**: Fixed "ghost" devices and stale data by implementing a complete session reset on disconnect.
- **Fast Probing**: Optimized device checks to prevent UI hangs during server lag or when devices are missing.

## Update 4.2 — Cargo Ship Overhaul (May 5th)
<img width="595" height="331" alt="grafik" src="https://github.com/user-attachments/assets/5e19a7b3-9231-4dc3-8c3b-0a6d14bad1d3" />

**🚢 Smart Cargo Tracking**

- Route Learning: After the first full Cargo cycle, the app remembers docking times, total map life, and trigger points — saved per server and map wipe, resets automatically after a wipe.
- Docking Countdown: A live countdown appears below the Cargo Ship while it's anchored at harbor. Docking duration is learned per server; the default fallback is 8 minutes.
- Remaining On-Map Timer: Once a full cycle is tracked, a remaining-time countdown is shown in the Event Dock and on the Cargo Ship marker. 
<img width="503" height="326" alt="grafik" src="https://github.com/user-attachments/assets/471ed1f8-9af3-4d5b-b3f8-9909b398a217" />

**💬 Cargo Chat Notifications**
<img width="596" height="44" alt="grafik" src="https://github.com/user-attachments/assets/7ae05e27-65fe-48a3-b141-3e519762d37f" />
- Arrival Warning: Team chat alert ~5 minutes before Cargo docks at the next harbor (requires a learned route).
- Docking Alert: Notification when Cargo anchors at a harbor.
- Departure Warning: Notification 5 minutes before Cargo leaves.
- All three can be toggled individually via right-click on the Chat Alerts button.

**🛢️ Oil Rig Crate Countdown**
~~- The app detects when a Chinook hovers over an Oil Rig and automatically starts a crate countdown on the map.~~
<img width="651" height="223" alt="grafik" src="https://github.com/user-attachments/assets/1435999f-e06e-4f1e-9695-92d9df78e429" /> 
(fixed by FP - we'll try to bring it back in)


## Update 4.1.0 - Crosshair Editor (Right Click Crosshair icon to access) (May 1st)

**𖦏 Custom Crosshairs**
- **Draw your own**: intuitive pixel-art style editor. Supports drawing tools (Pen, Pixel, Line, Square, Circle), custom colors, adjustable thickness and opacity, and full Undo/Redo support.
- **Upload PNGs**: Upload existing PNG images to use as crosshairs. The editor automatically scales them to fit the pixel grid. You can also right-click to erase individual pixels and easily rename or delete your creations.
![CrosshairEditor](./RustPlusDesktop/Assets/Screenshots/v4_5.png)
---

## Update 4.0.0 - The Evolution Update | Major Map & Stability Overhaul (April 30th)

**🚀 Key Highlights**
- **Rebuilt Core Architecture**: A massive refactoring of over 4,000 lines of code into a modular system, ensuring the app is faster and future-proof.
- **"Dead Reckoning" Resilience**: Markers and shops no longer disappear during brief server lags. The app now uses predictive interpolation to keep player and event icons moving smoothly even when data is delayed.
- **Interactive Event Dock**: A new real-time sidebar for active events (Patrol Heli, Cargo Ship, Chinook, etc.). Click any event to **auto-lock and track** it dynamically across the map.
- **Smart Shop Clustering**: Multiple vending machines in one base are now grouped into clean cluster icons. Hovering over them reveals a redesigned, scrollable list of all items without map clutter.

![V4 Map Overhaul](./RustPlusDesktop/Assets/Screenshots/v4_map_overhaul.png)

**🛠 Improvements**
- **60 FPS Map Animations**: Butter-smooth zooming and panning with a new cinematic "Overview Dip" when jumping across the map.
- **Modern Shop Search**: Powered by WebView2, the new search interface is near-instant and includes advanced arbitrage (Profit Trades) and pathfinding tools.
- **Offline Icon Caching**: All item icons are now securely cached locally using SHA1 hashes, making map loads instant and saving massive bandwidth.
- **Flexible UI**: Added a GridSplitter for a resizable sidebar and the ability to hide the system console to maximize map space.

**🙌 Special Thanks**
This milestone release was made possible by the incredible contribution of **[JawadYzbk](https://github.com/JawadYzbk)**, who rebuilt the core architecture and implemented the advanced map features!

---

## Update 3.5.0 - Player Intelligence & Background Ops (April 22nd)
**🚀 New Features**
- **Advanced Activity Intelligence**: Introducing a full-scale player tracking system! View 12-week GitHub-style activity grids and 24-hour heatmaps to predict when your enemies (or friends) are most likely to be online or sleeping.
- **Background Operations**: The app can now reside in your System Tray. Collect player data 24/7 without having the main window open.
- **Single Instance Management**: Launching the app via `rustplus://` links or a second desktop shortcut now automatically focuses your already running instance.
- **Auto-Start**: New option to launch the app minimized with Windows, so your tracking database is always up to date.

**🛠 Improvements & Fixes**
- **Battlemetrics Accuracy**: Completely overhauled server identification. Fixed an issue where servers on shared IP ranges (like Rustoria) were sometimes incorrectly identified. 
- **Tray Menu**: Dynamic tray context menu showing current tracking status and last update time.

**🙌 Special Thanks**
A massive shout-out to [JawadYzbk](https://github.com/JawadYzbk) for contributing this entire intelligence system and background logic!

## Update 3.4.0 - Custom Alarms & Device Grouping (April 26)
**🚀 New Features**
- Customizable Smart Alarms: You can now set individual popup alerts and custom audio files. Perfect for turning up the volume and getting woken up specifically for Raids!
- Smart Device Groups: Organize your setup by merging devices into groups. You can rename these groups and control multiple devices simultaneously with a single click (bringing the power of hotkeys to the UI).

**🛠 Improvements**
- Enhanced Team Uploads: Device uploads for team members now fully support hierarchical group structures. No matter how many devices you manage, everything stays organized and easy to navigate.

## Update Notes 3.3.1 (February 16th 26)
~~- **New Pre Deep Sea Notification:** Before Deep Sea is triggered, you can get a notification in Team Chat (around 3 minutes ahead of actual spawn) -> note that the direction will always be shown in West - this is not the actual spawn location. It's just coming from the fact that Deep Sea shops have negative X-coordinates.~~
(Fixed by Facepunch)
- **Stability Patch:** Even on weak servers the connection should now be more stable and smart devices should work more reliably. Reduced duplicate chat fetches, made shop search and shops more stable with caching icons to local drive.

## Update Notes 3.3.0 (January 18th 26)
~~- **New Oilrig Countdown:** When Oilrig is triggered, a crate icon with the remaining time appears on the map. Optional Team Chat notifications remind your team every 5 minutes until the crate unlocks.~~ (Fixed by FP)
- **Leader Auto-Promote:** No more AFK leaders! Team members can now type `!leader` in chat to be instantly promoted to team leader (requires current leader to have the app open).

## Update Notes 3.2.1 (November 21st 25)
- You can now share Smart Devices with your team! No more pairing in-game needed. 
  One guy who pairs the devices is enough - rest of the team just imports with 1 click.

## Update Notes 3.1.2 (November 17th 25)
Version 3.1.2 brings full Storage Monitor integration and the following optimizations:
![Update](./RustPlusDesktop/Assets/Screenshots/3.1.0.png)  
- Shop alerts now also trigger when item was sold out and then comes back online
- Storage Monitor shows traffic light upkeep indicator (from 1 hr. and less)
- Map can be zoomed with NUM +/-
- No duplicate chat notifications when server had been desynced for a short amount of time

## Update Notes 3.0.0 (October 30th 25)
- FULL Shop Analytics Overhaul!
  This comes with instant check for profit trades, trade route check (Buy X for Y) and more
- Map Overlay
  You can draw, set markers, share your map markers with team mates
- Shop Alarm system
  Get alerts (in chat or audio alerts) when a new shop pops up or when a suspicious shop disappeared or when your desired item is back in stock
- new Patch Notes Button with all new features explained

... and more


## Update Notes 2.0.5 (October 6th 25)
- Global Device Hotkeys are here! Assign one key to multiple devices to group them together.
- new Update Button (Bug: reads current version as 0.0.0 so it will always find an update - will be fixed in the future)
- new Pairing possibility through Edge Browser + better Logs
- Mini Map Overlay for ingame use
- Crosshair Overlay
- Team Management
- Camera Support
- Promoting Teammember to Leader
- Death Markers
- Grid Corrections
- Notifications in Chat for Deaths, Spawns, Online, Offline
- added fetching icon symbols from rusthelp.com (including Blueprint Fragments)

![Update](./RustPlusDesktop/Assets/Images/V2-1.png)  
![Update](./RustPlusDesktop/Assets/Images/V2-2.png) 
![Update](./RustPlusDesktop/Assets/Images/V2-3.png) 

Enjoy! :) 
---



## ✨ Features



- Pair Rust servers via Steam + Rust+ Companion
- **Player Activity Intelligence: 12-week heatmaps & 24h activity forecasts**
- **Persistent Background Tracking & System Tray integration**
- **Single Instance Management (Named Pipes)**
- Share Smart Devices and device groups with your Team
- Track Storage Monitors and Upkeep Time 
- Auto-start listener when connecting to a server
- Dynamic map (Cargo, Patrol Heli, Chinook, Travelling Vendor, Players, …)
- Smart Devices (pair in-game while connected — shows up instantly)
- Local storage of paired servers & devices, map overlays
- Vending Machine Search System for Buy and Sell orders
- Profit Trade analytics and deep trade route search (buy X for Y) 
- Open-source for transparency and trust
- Team Chat support and event spawn posts to chat
- Camera Support (no pannable cams yet)
- Mini Map and Crosshairs as Rust Overlay
- Death Markers
- Profile Icons
- Chat-Notifications for spawns, shops, deaths, events and more

---



## 🐞 Known Issues


- **Mixed languages**: Some UI texts may still show in German if a translation was missed  

- **Server-Hopping:**: Hopping through servers too quickly can cause the Listener to crash

- **Many shops**: Hovering 8+ shops at once can cause the Tooltip to flicker

- Please report other issues in the [Issues section](../../issues)

---



## 🛠️ Installation & Setup



1. **Download & install**  

   - Get the installer from **[Releases](../../releases/latest)** and run it



2. **First run**

   - Click Pairing (Listening) to start the initial setup of the Listener.
   ⚠️ **IMPORTANT**: IF error message pops up, please restart the app, rightclick on the button and click on "Try Pairing with Edge".

   - A browser popup will ask you to **pair with Companion** (Facepunch)

     let it run until it's set up (needed only once)

   - Click on "**Login with Steam**" and authorize your local PC to Steam (localhost)  

   - Allow the connection → your Steam account is linked



4. **Pair a server**  

   - In the app, click **Listening (Pairing)**  

   - In *Rust*, click the **Rust+ Pairing Link**  

   - The server will appear automatically in the app



5. **Connect**  

   - Select the server and click **Connect**  

   - Future sessions won’t require another Steam login


6. **Smart Devices**  

   - While connected, pair a device or server in-game → it appears instantly in the app

7. **If the FCM Listener won't start after a while of using the app**
   - you probably have to do the Pairing Process again.
   - Rightclick the Pairing button and select "Delete Config + Pair".
   - That's it.

8. **Alternative manuall pairing**
   - You can do the pairing manually through PowerShell. 

   - Open PowerShell, 
   - Go to your installation folder (e.g. -> a: -> cd programs -> cd RustPlusDesk)
   - Then copy paste this Power Shell code to the console. (Press enter twice) This should pair manually and open a popup in browser:

```powershell
$node = ".\runtime\node-win-x64\node.exe"
$cli  = "$env:LOCALAPPDATA\RustPlusDesk\runtime\rustplus-cli\node_modules\@liamcottle\rustplus.js\cli\index.js"
$cfg  = "$env:APPDATA\RustPlusDesk\rustplusjs-config.json"

if (!(Test-Path $cli)) {
    $zip = ".\runtime\rustplus-cli.zip"
    $dst = "$env:LOCALAPPDATA\RustPlusDesk\runtime\rustplus-cli"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Expand-Archive -Path $zip -DestinationPath $dst -Force
}
```

& $node $cli fcm-register --config-file "$cfg"

## 🛠️ Why initial NCM registration is required:
<details> 
   <summary> NCM Registration Explanation </summary>
On first launch, the app needs to establish a connection to the Rust+ Companion API.
For this, a bundled Node.js process (rustplus-cli) is started, which takes care of two things:

**Registration with Facepunch/Steam**

   - Opens a browser window to the official Rust+ Companion login page.

   - After logging in with Steam, an auth token is generated and passed back to the app.

   - This token is saved in the app’s config file so the process only needs to be done once per installation.

**Local listener for callbacks and notifications**

   - The Node process starts a small HTTP server on localhost:<random port> to receive the auth token.

   - Afterwards, it continues running as a background listener to receive notifications (chat, alarms, events) via Google FCM and forward them to the app.

**Requirements for successful registration**

   - Node.js runtime and rustplus-cli are shipped with the app – no manual installation required.

   - Firewall/Antivirus must not block the Node process:

   - Local loopback (127.0.0.1) must be accessible for the callback port.

**Outbound connections must be allowed on:**

   - TCP 5228–5230 (Google FCM, mtalk.google.com)

   - TCP 443 (HTTPS to Steam, Facepunch, Google)

   - Browser redirect must be allowed (some security tools or proxies may block it).

   - A valid Steam login is required to complete the auth flow.

**👉 After successful registration, the token is stored at**
%APPDATA%\RustPlusDesk\rustplusjs-config.json.
You only need to re-register if this file is missing or corrupted.
  </details>
  
<details>
<summary>🔧 Troubleshooting Registration</summary>

If the initial pairing does not work (no browser window opens, or it keeps restarting):

- **Check if Node is running**  
  - Open *Task Manager* → *Details* → look for `node.exe`.  
  - Or run:  
    ```powershell
    tasklist | findstr node.exe
    ```

- **Check if a local port is listening**  
  - Run:  
    ```powershell
    netstat -ano | findstr LISTENING | findstr 127.0.0.1
    ```
  - You should see a `127.0.0.1:<port>` entry with the same PID as `node.exe`.  
  - If not: Firewall or antivirus may be blocking the local callback server.  

- **Check outbound connections**  
  Test if the required ports are open:  
  ```powershell
  Test-NetConnection mtalk.google.com -Port 5228
  Test-NetConnection companion-rust.facepunch.com -Port 443
  Test-NetConnection steamcommunity.com -Port 443
  All should return TcpTestSucceeded : True
- **Config reset**
If all else fails, close the app and delete:
%APPDATA%\RustPlusDesk\rustplusjs-config.json
On next launch the registration will run again.
  </details>
---



## 📸 Screenshots



### Main Screenshots

![Main Background](./RustPlusDesktop/Assets/Images/rustplusbg.png)  

![Background 2](./RustPlusDesktop/Assets/Images/rustplusbg2.png)  

![Background 3](./RustPlusDesktop/Assets/Images/rustplusbg3.png)  

![Background 4](./RustPlusDesktop/Assets/Images/rustplusbg4.png)  

![Background 5](./RustPlusDesktop/Assets/Images/rustplusbg5.png)  

![Background 6](./RustPlusDesktop/Assets/Images/rustplusbg6.png)  

![Background 7](./RustPlusDesktop/Assets/Images/rustplusbg7.png)  

![Background 8](./RustPlusDesktop/Assets/Images/rustplusbg8.png)



### Video Overview

[![YouTube Video](./RustPlusDesktop/Assets/Images/rustplusbg.png)](https://www.youtube.com/watch?v=4NlFuLPK4wk)  

*(click the image to watch on YouTube)*



---



## 📜 License



This project is licensed under the [GNU GPLv3](./LICENSE).

SPDX-License-Identifier

GPL-3.0-or-later



## Release Checksum:

SHA256-Hash for current RustPlusDesk-Setup.exe 6.1.1:

sha256:6f43839d3350a8856937e2f9ce7412c10c830fe2c91c03e08f468545d5b92572

---



## 🙌 Contributing



Found a bug or want to help?  

Open an [Issue](../../issues) or create a Pull Request.



## Support?



Sure, why not :) 

**https://streamelements.com/pronwan/tip**

