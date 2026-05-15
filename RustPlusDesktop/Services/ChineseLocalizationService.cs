using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RustPlusDesk.Services;

public static class ChineseLocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> ZhCn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Account"] = "账号",
        ["Active"] = "运行中",
        ["Add server"] = "添加服务器",
        ["Alarms"] = "警报",
        ["Application Settings"] = "应用设置",
        ["App Settings & Options"] = "应用设置与选项",
        ["Arrival Warning (5m before Dock)"] = "到港预警（停靠前 5 分钟）",
        ["Assign global hotkeys to Smart Switches. Multiple devices can share one hotkey (will toggle sequentially)."] = "为智能开关分配全局热键。多个设备可以共用一个热键，并会依次切换。",
        ["Audio"] = "音频",
        ["Auto load shops on connection"] = "连接后自动加载商店",
        ["Auto-Connect to last server on startup"] = "启动时自动连接上次服务器",
        ["Automatically reconnects to the last server and loads the map."] = "自动重连上次服务器并加载地图。",
        ["Automatically starts polling shops and alerts when connected to a server."] = "连接服务器后自动轮询商店和提醒。",
        ["Awesome, let's go!"] = "好的，开始吧！",
        ["Behavior"] = "行为",
        ["Bitte warten …"] = "请稍候 …",
        ["BM"] = "BM",
        ["Camera"] = "摄像头",
        ["Cameras"] = "摄像头",
        ["Cancel"] = "取消",
        ["Cargo Ship"] = "货船",
        ["Cargo Ship:"] = "货船：",
        ["Center on map"] = "在地图居中",
        ["Chat Alerts"] = "聊天提醒",
        ["Chat is not available right now."] = "当前无法使用聊天。",
        ["Chat Commands"] = "聊天指令",
        ["Chat Commands Settings"] = "聊天指令设置",
        ["Check device status"] = "检查设备状态",
        ["Check for Updates"] = "检查更新",
        ["Check GitHub for a newer version"] = "在 GitHub 检查新版本",
        ["Checking GitHub release …"] = "正在检查 GitHub 版本 …",
        ["Chinook (CH47)"] = "军用运输直升机（CH47）",
        ["Choose monitor"] = "选择显示器",
        ["Circle"] = "圆形",
        ["Clear"] = "清除",
        ["Close"] = "关闭",
        ["Close & Activate"] = "关闭并启用",
        ["Close & Deactivate"] = "关闭并停用",
        ["Close Chat"] = "关闭聊天",
        ["Close Search"] = "关闭搜索",
        ["Connect to a server to see the map"] = "连接服务器后查看地图",
        ["Companion-Port:"] = "Companion 端口：",
        ["Connecting …"] = "正在连接 …",
        ["Connected."] = "已连接。",
        ["Core Features & Older Updates"] = "核心功能与历史更新",
        ["Crosshair Style"] = "准星样式",
        ["Crosshair on/off (Right Click For Options)"] = "准星开关（右键打开选项）",
        ["Current Version: 4.5.0"] = "当前版本：4.5.0",
        ["Custom Audio File..."] = "自定义音频文件...",
        ["Death markers"] = "死亡标记",
        ["Deep Sea Event"] = "深海事件",
        ["Deep Sea:"] = "深海：",
        ["Delete"] = "删除",
        ["Delete own overlay"] = "删除自己的覆盖层",
        ["Delete server…"] = "删除服务器…",
        ["Departure (5m Warning)"] = "离港预警（5 分钟）",
        ["Deselect all"] = "取消全选",
        ["Device"] = "设备",
        ["Device Export"] = "设备导出",
        ["Device export failed:"] = "设备导出失败：",
        ["Device Import"] = "设备导入",
        ["Device import failed:"] = "设备导入失败：",
        ["Devices"] = "设备",
        ["Disconnect"] = "断开连接",
        ["Donate"] = "赞助",
        ["Don't show this again"] = "不再显示",
        ["Downloading Icons ("] = "正在下载图标（",
        ["Download failed."] = "下载失败。",
        ["Draw (Right Click: Size / Color)"] = "绘制（右键设置大小/颜色）",
        ["Draw Crosshair"] = "绘制准星",
        ["Draw Crosshair..."] = "绘制准星...",
        ["Draw your own custom crosshairs with an intuitive pixel-art style editor. Supports drawing tools (Pen, Pixel, Line, Square, Circle), custom colors, adjustable thickness and opacity, and full Undo/Redo support."] = "使用直观的像素风编辑器绘制自定义准星。支持画笔、像素、直线、方形、圆形、自定义颜色、粗细、透明度，以及完整撤销/重做。",
        ["Enable Background Player Tracking"] = "启用后台玩家追踪",
        ["Enable Chat Commands"] = "启用聊天指令",
        ["Enable/disable popups and audio independently. Support for custom .wav/.mp3 alarm files via right-click."] = "可分别启用/停用弹窗和音频。右键支持自定义 .wav/.mp3 警报文件。",
        ["EntityId"] = "实体 ID",
        ["Eraser (hold or click)"] = "橡皮擦（按住或点击）",
        ["Error"] = "错误",
        ["Error: "] = "错误：",
        ["Error message here"] = "错误信息",
        ["Error loading analytics view: "] = "加载分析视图失败：",
        ["Events"] = "事件",
        ["Ensure WebView2 Runtime is installed."] = "请确认已安装 WebView2 Runtime。",
        ["Exit"] = "退出",
        ["Export devices to teammates"] = "导出设备给队友",
        ["Exported {0} devices to your team share."] = "已导出 {0} 个设备到队伍共享。",
        ["FPS:"] = "帧率：",
        ["Follow Player on Map"] = "在地图上跟随玩家",
        ["Follow on Map"] = "在地图上跟随",
        ["Full player tracking system with 12-week heatmaps and 24h forecasts. Predict when players are active or sleeping based on historical connection data."] = "完整玩家追踪系统，包含 12 周热力图和 24 小时预测。根据历史连接数据判断玩家活跃或下线时间。",
        ["General"] = "通用",
        ["Green Dot"] = "绿色圆点",
        ["Grid"] = "网格",
        ["Harbor Docking"] = "港口停靠",
        ["Hide System Console (Declutter)"] = "隐藏系统控制台（减少干扰）",
        ["Hides the bottom log/console panel to save map space."] = "隐藏底部日志/控制台面板，节省地图空间。",
        ["Hotkey"] = "热键",
        ["Hotkeys"] = "热键",
        ["Identifier:"] = "标识：",
        ["Idle"] = "空闲",
        ["Import devices"] = "导入设备",
        ["Import devices from teammates"] = "从队友导入设备",
        ["Import selected"] = "导入所选",
        ["INCOMING ALARMS"] = "收到警报",
        ["Invalid input."] = "输入无效。",
        ["Items, Upkeep="] = "项，维护=",
        ["Join our Discord community"] = "加入 Discord 社区",
        ["Keeps tracking players via BattleMetrics even when minimized."] = "最小化时仍通过 BattleMetrics 追踪玩家。",
        ["Kick from Team"] = "踢出队伍",
        ["Last pull: --:--"] = "上次拉取：--:--",
        ["Last update:"] = "上次更新：",
        ["Launch with Windows (Minimized)"] = "随 Windows 启动（最小化）",
        ["Line"] = "直线",
        ["Listen (Pairing)"] = "监听（配对）",
        ["Loading players..."] = "正在加载玩家...",
        ["Login with Steam"] = "使用 Steam 登录",
        ["Magenta Dot"] = "洋红圆点",
        ["Magenta Open Cross"] = "洋红空心十字",
        ["Manage your linked Steam account in the main window."] = "在主窗口管理已绑定的 Steam 账号。",
        ["Manual BM ID..."] = "手动 BM ID...",
        ["Manual Player Tracking & Renaming"] = "手动玩家追踪与重命名",
        ["Manually add players to your trackers list using their BattleMetrics ID. The app fetches their last seen status and allows you to rename them for better organization."] = "使用 BattleMetrics ID 手动添加玩家到追踪列表。应用会获取最后在线状态，并允许重命名以便管理。",
        ["Map Controls"] = "地图控制",
        ["Map Settings"] = "地图设置",
        ["Map-Overlay Zeichnen / Team Overlays"] = "地图覆盖层绘制 / 队伍覆盖层",
        ["Mini 🗺"] = "小地图 🗺",
        ["Mini Green"] = "迷你绿色",
        ["Minimize to Tray instead of closing"] = "关闭时最小化到托盘",
        ["Message could not be sent. Please try again. (Check if you are in a team!)"] = "消息未能发送，请重试。（请确认你已加入队伍！）",
        ["Monuments"] = "地标",
        ["New Shops"] = "新商店",
        ["No cameras found"] = "未找到摄像头",
        ["No description available."] = "暂无描述。",
        ["No devices found"] = "未找到设备",
        ["No devices."] = "没有设备。",
        ["No device exports found for your team / server."] = "没有找到此队伍/服务器的设备导出。",
        ["No detailed description available for this server."] = "该服务器暂无详细描述。",
        ["No direct seller for that item."] = "没有直接出售该物品的商店。",
        ["No offers available"] = "暂无报价",
        ["No profitable flips found."] = "未找到有利润的倒卖路线。",
        ["No results found matching filter."] = "没有符合筛选条件的结果。",
        ["No route found."] = "未找到路线。",
        ["No servers paired yet"] = "还没有配对服务器",
        ["No shop data yet."] = "暂无商店数据。",
        ["No valid route (bottlenecks)."] = "没有有效路线（存在瓶颈）。",
        ["Note: 2 fps is recommended, higher FPS likely close your connection to server"] = "提示：建议 2 FPS，更高帧率可能导致服务器连接关闭",
        ["Not connected."] = "未连接。",
        ["Offline Status"] = "离线状态",
        ["Oil Rig Trigger"] = "石油平台触发",
        ["Oil Rig:"] = "石油平台：",
        ["Once connected, the live Rust map will load here."] = "连接后会在这里加载实时 Rust 地图。",
        ["Online Players"] = "在线玩家",
        ["Online Status"] = "在线状态",
        ["Opacity:"] = "透明度：",
        ["Open Crosshair (R/G)"] = "空心准星（红/绿）",
        ["Open Rust+ Desk"] = "打开 Rust+ Desk",
        ["Open Server on BattleMetrics"] = "在 BattleMetrics 打开服务器",
        ["Open Steam profile"] = "打开 Steam 资料",
        ["Pair a camera to see its preview here"] = "配对摄像头后在这里查看预览",
        ["Pair a smart device, switch or alarm to see it here"] = "配对智能设备、开关或警报器后在这里查看",
        ["Paired servers list"] = "已配对服务器列表",
        ["Pairing: config deleted"] = "配对：配置已删除",
        ["Pairing: error"] = "配对：错误",
        ["Pairing: idle"] = "配对：空闲",
        ["Pairing: listening…"] = "配对：监听中…",
        ["Pairing: stopped"] = "配对：已停止",
        ["Patch Notes"] = "更新日志",
        ["Patch Notes & Version History"] = "更新日志与版本历史",
        ["Patrol Helicopter"] = "武装直升机",
        ["Pen"] = "画笔",
        ["Pixel"] = "像素",
        ["Place Icon (Right Click: Pick Icon)"] = "放置图标（右键选择图标）",
        ["Place Text (Right Click: Color / Size)"] = "放置文字（右键设置颜色/大小）",
        ["Player-Token (Rust+):"] = "玩家 Token（Rust+）：",
        ["Players"] = "玩家",
        ["Players:"] = "玩家：",
        ["Please connect to a server first."] = "请先连接服务器。",
        ["Population:"] = "人数：",
        ["Popup"] = "弹窗",
        ["Press hotkey…"] = "按下热键…",
        ["Press the desired key combination"] = "按下要设置的组合键",
        ["Profile markers"] = "资料标记",
        ["Promote to Leader"] = "提升为队长",
        ["Promote:"] = "提升队长：",
        ["Possible 2-step profit loops"] = "可能的两步利润路线",
        ["Queue:"] = "排队：",
        ["|  Queue:"] = "|  排队：",
        ["Range Line (ticks)"] = "距离线（刻度）",
        ["Refresh"] = "刷新",
        ["Rename device"] = "重命名设备",
        ["Reset ↲"] = "重置 ↲",
        ["Reset ⟲"] = "重置 ⟲",
        ["Reset + Listen (re-pair)"] = "重置并监听（重新配对）",
        ["Reset + Listen with Edge"] = "重置并用 Edge 监听",
        ["Reset Connection"] = "重置连接",
        ["Reset map zoom and position"] = "重置地图缩放和位置",
        ["Reset pairing (delete config)"] = "重置配对（删除配置）",
        ["Reset to Default"] = "恢复默认",
        ["Right click to rename"] = "右键重命名",
        ["Right-click to configure"] = "右键配置",
        ["Rust+ Desk – Patch Notes"] = "Rust+ Desk - 更新日志",
        ["Rust+ Desk – Import Devices"] = "Rust+ Desk - 导入设备",
        ["Rust+ Desktop by Pronwan"] = "Rust+ Desktop by Pronwan",
        ["Rust+ Desktop — v4.4 Update"] = "Rust+ Desktop - v4.4 更新",
        ["Rust+Desktop"] = "Rust+Desktop",
        ["Save"] = "保存",
        ["Save & Close"] = "保存并关闭",
        ["Select all"] = "全选",
        ["Select a Server"] = "选择服务器",
        ["Select which devices you want to import for this server."] = "选择要导入到此服务器的设备。",
        ["Self Death"] = "自己死亡",
        ["Self Respawn"] = "自己重生",
        ["Send"] = "发送",
        ["Server Information"] = "服务器信息",
        ["Server IP/Host:"] = "服务器 IP/Host：",
        ["Set"] = "设置",
        ["Settings"] = "设置",
        ["Shop Search"] = "商店搜索",
        ["SHOP SEARCH"] = "商店搜索",
        ["Shops"] = "商店",
        ["Show latest changes"] = "显示最新改动",
        ["Show Online Players"] = "显示在线玩家",
        ["Show Server Description"] = "显示服务器描述",
        ["Smart Switches"] = "智能开关",
        ["Snapshot:"] = "快照：",
        ["Spawn"] = "刷新",
        ["Square"] = "方形",
        ["Square + Dot"] = "方形 + 圆点",
        ["Stability, Intelligence & Control"] = "稳定性、情报与控制",
        ["Standard Commands"] = "标准指令",
        ["Start Minimized (Always)"] = "始终最小化启动",
        ["Starting Pairing-Listener (Edge) …"] = "正在启动配对监听器（Edge）…",
        ["Starte Pairing-Listener …"] = "正在启动配对监听器 …",
        ["Steam Account"] = "Steam 账号",
        ["Steam login failed: "] = "Steam 登录失败：",
        ["SteamID64:"] = "SteamID64：",
        ["Support (Opens in Browser)"] = "支持（在浏览器打开）",
        ["Suspicious Shops"] = "可疑商店",
        ["Switch 1:"] = "开关 1：",
        ["Switch 2:"] = "开关 2：",
        ["TEAM CHAT"] = "队伍聊天",
        ["Team"] = "队伍",
        ["Team Chat"] = "队伍聊天",
        ["Team Death"] = "队友死亡",
        ["Team Respawn"] = "队友重生",
        ["The Intelligence Update"] = "情报更新",
        ["Thickness:"] = "粗细：",
        ["Thin Circle (bright red)"] = "细圆环（亮红）",
        ["Time:"] = "时间：",
        ["Tip: Right-click Listen for more options"] = "提示：右键监听查看更多选项",
        ["TOGGLE"] = "切换",
        ["Track"] = "追踪",
        ["Track ID"] = "追踪 ID",
        ["Tracking"] = "追踪",
        ["Tracking Active"] = "追踪中",
        ["Trade Alerts"] = "交易提醒",
        ["Travelling Vendor"] = "流浪商人",
        ["Type what you WANT to get."] = "输入你想获得的物品。",
        ["Try Pairing with Edge"] = "尝试用 Edge 配对",
        ["Upload PNG"] = "上传 PNG",
        ["Upload own Overlay / share"] = "上传自己的覆盖层 / 分享",
        ["Update"] = "更新",
        ["Update check failed."] = "更新检查失败。",
        ["Use Facepunch proxy? (y/n)"] = "使用 Facepunch 代理？(y/n)",
        ["View tracker players list"] = "查看追踪玩家列表",
        ["Wipe"] = "清空",
        ["You can only delete missing devices"] = "只能删除缺失设备",
        ["You are up to date."] = "当前已是最新版本。",
        ["— Major Release"] = "- 重大版本",
        ["ðŸŽ¥ Add Camera"] = "添加摄像头",
        ["v4.5.0"] = "v4.5.0",
        ["v4.5.2  |  Shop Polling Hotfix (May 12, 2026)"] = "v4.5.2  |  商店轮询热修复（2026-05-12）",
        ["v4.3.1  |  Bugfixes & Improvements"] = "v4.3.1  |  Bug 修复与改进",
        ["v4.2  |  Cargo Ship Overhaul"] = "v4.2  |  货船功能重做",
        ["v4.1.4  |  Granular Chat Notifications"] = "v4.1.4  |  细粒度聊天通知",
        ["v4.1.2  |  Custom Crosshair Editor"] = "v4.1.2  |  自定义准星编辑器",
        ["v4.0.0  |  The Map & Stability Overhaul (Major Release)"] = "v4.0.0  |  地图与稳定性大改版（重大版本）",
        ["v3.5.4  |  Player Management & Optimization"] = "v3.5.4  |  玩家管理与优化",
        ["v3.5.3  |  The Intelligence Update"] = "v3.5.3  |  情报更新",
        ["v3.4.0  |  Hierarchical Grouping & Advanced Alarms"] = "v3.4.0  |  分层分组与高级警报",
        ["v3.3.1  |  Stability & Event Awareness"] = "v3.3.1  |  稳定性与事件感知",
        ["v3.3.0  |  Oilrig Timer & !leader Command"] = "v3.3.0  |  石油平台计时与 !leader 指令",
        ["ⓘ Info"] = "ⓘ 信息",
        ["⚙ Settings"] = "⚙ 设置",
        ["⌖"] = "⌖",
        ["▼"] = "▼",
        ["✕"] = "✕",
        ["❤"] = "❤",
        [""] = "",
        [""] = "",
        [""] = "",
        [""] = "",
        [""] = "",
        [""] = "",
        [""] = "",
        [""] = "",

        ["• 'Dead Reckoning' & Connection Resilience"] = "• 预测定位与连接韧性",
        ["• 60 FPS Animations & Offline Icon Caching"] = "• 60 FPS 动画与离线图标缓存",
        ["• Advanced Activity Intelligence"] = "• 高级活跃情报",
        ["• Background Stability & Reset Logic"] = "• 后台稳定性与重置逻辑",
        ["• Camera monitoring, Team management, Death markers, and more."] = "• 摄像头监控、队伍管理、死亡标记等功能。",
        ["• Centralized Settings & Auto-Connect"] = "• 集中设置与自动连接",
        ["• Chat Deduplication"] = "• 聊天去重",
        ["• Corrected Player Tracking & Map Alignment"] = "• 修正玩家追踪与地图对齐",
        ["• Customizable Chat Alerts & UI Polish"] = "• 可自定义聊天提醒与界面优化",
        ["• Deep Sea Event Notifications ~3 minutes ahead of actual Deep Sea Spawn"] = "• 深海事件刷新前约 3 分钟通知",
        ["• Deep Sea Timer Improvements"] = "• 深海计时改进",
        ["• Enhanced Connection & Reconnect Stability"] = "• 增强连接与重连稳定性",
        ["• Fixed: Battlemetrics Server Lookup"] = "• 修复：BattleMetrics 服务器查询",
        ["• Fully Featured Crosshair Editor (Rightclick Crosshair Icon to Access)"] = "• 完整准星编辑器（右键准星图标进入）",
        ["• Granular Cargo Chat Notifications"] = "• 细粒度货船聊天通知",
        ["• Granular Notification Management"] = "• 细粒度通知管理",
        ["• Hierarchical Device Grouping"] = "• 分层设备分组",
        ["• Improved Map Rendering & Scaling"] = "• 改进地图渲染与缩放",
        ["• Interactive Event Dock & Auto-Follow"] = "• 交互式事件栏与自动跟随",
        ["• Live Countdown Timers"] = "• 实时倒计时",
        ["• Manual IP & Protocol Support"] = "• 手动 IP 与协议支持",
        ["• Manual Player Tracking & Renaming"] = "• 手动玩家追踪与重命名",
        ["• Modern Shop Search & Advanced Trades"] = "• 现代商店搜索与高级交易",
        ["• Oil Rig Crate Trigger Detection"] = "• 石油平台箱子触发检测",
        ["• Optimized Item Icon Downloads"] = "• 优化物品图标下载",
        ["• PNG Uploads & Management"] = "• PNG 上传与管理",
        ["• Patrol Helicopter Crash Detection"] = "• 武装直升机坠毁检测",
        ["• Persistent Smart Alarm Settings"] = "• 持久化智能警报设置",
        ["• Progress Tracking"] = "• 进度追踪",
        ["• Remote Switch Control via Chat Commands"] = "• 通过聊天指令远程控制开关",
        ["• Smart Cargo Route Learning"] = "• 智能货船路线学习",
        ["• Smart Map Follow & Player Tracking"] = "• 智能地图跟随与玩家追踪",
        ["• Smart Shop Clustering"] = "• 智能商店聚合",
        ["• Smart Timer & Event Intelligence"] = "• 智能计时与事件情报",
        ["• System Tray & Background Ops"] = "• 系统托盘与后台运行",
        ["• v3.2.0: Smart Device Import/Export for teams."] = "• v3.2.0：队伍智能设备导入/导出。",
        ["• v3.1.0: Full Storage Monitor & Upkeep integration."] = "• v3.1.0：完整储物监控与维护集成。",
        ["• v3.0.0: Massive Shop Analytics overhaul & Custom Map Overlays."] = "• v3.0.0：商店分析大改版与自定义地图覆盖层。",

        ["A brand new Event Dock tracks active map events (Patrol Heli, Cargo Ship, Chinook, Travelling Vendor, Deep Sea). Click an event to instantly lock the camera onto it and track it automatically across the map."] = "全新的事件栏会追踪活跃地图事件（武装直升机、货船、军用运输直升机、流浪商人、深海）。点击事件即可锁定镜头并在地图上自动跟随。",
        ["Added a download progress bar to show visual status when the app is fetching item icons or updates."] = "新增下载进度条，用于显示物品图标或更新下载状态。",
        ["Added support for rustplus:// links! You can now manually add servers using 'rustplus://IP:PORT'—a workaround for some servers where the pairing button is disabled."] = "新增 rustplus:// 链接支持。现在可以用 'rustplus://IP:PORT' 手动添加服务器，绕过部分服务器禁用配对按钮的问题。",
        ["Adjusted the render size of the Cargo Ship and optimized map rendering performance when zoomed in deeply. The interface remains snappy and readable even at extreme zoom levels."] = "调整货船渲染尺寸，并优化深度缩放时的地图渲染性能。即使在极端缩放下界面仍保持流畅清晰。",
        ["Icons are now fetched from RustClash CDN in 40x40 size, reducing download size by 12.9x (totaling 151KB). Existing high-res icons remain cached; only missing ones are fetched in the new optimized format."] = "图标现在从 RustClash CDN 获取 40x40 尺寸，下载体积减少 12.9 倍（总计 151KB）。已有高清图标会保留缓存，仅缺失图标使用新优化格式下载。",
        ["Live countdown for Oilrig crates directly on the map. Teammates can use !leader in chat to request promotion."] = "地图上直接显示石油平台箱子实时倒计时。队友可在聊天中使用 !leader 请求提升为队长。",
        ["Map zooming and panning now feature butter-smooth animations and a cinematic 'overview dip'. Item icons are securely cached offline using SHA1 hashes, making map loading near-instant and saving bandwidth."] = "地图缩放和平移现在拥有更顺滑动画和电影感总览过渡。物品图标使用 SHA1 哈希安全离线缓存，使地图加载接近即时并节省带宽。",
        ["Multiple vending machines in a single base are now clustered into one clean map icon. Hovering over the cluster reveals a beautifully redesigned, scrollable popup showing all items sold at that location."] = "同一基地内的多个售货机会聚合为一个清晰地图图标。悬停聚合点可显示重新设计的可滚动弹窗，展示该位置出售的所有物品。",
        ["New Settings Modal: Toggle Auto-Start with Windows, Minimize to Tray, and Auto-Connect to your last server on startup."] = "新增设置窗口：可开关随 Windows 启动、最小化到托盘、启动时自动连接上次服务器。",
        ["Organize smart devices into nested groups! Drag one onto another to create folders and toggle entire groups with one click."] = "将智能设备整理成嵌套分组。把一个设备拖到另一个设备上即可创建文件夹，并一键切换整个分组。",
        ["Overhauled server identification. Searching is now strictly IP-based for 100% accuracy, fixing issues with shared IP ranges (e.g., Rustoria)."] = "重做服务器识别。搜索现在严格基于 IP，准确率更高，修复共享 IP 段（如 Rustoria）造成的问题。",
        ["Right-click 'Chat Alerts' to access a detailed management menu. You can now individually toggle notifications for cargo, helicopters, players, and more. Even Item Alerts and Shop settings can now be managed directly from this menu."] = "右键“聊天提醒”可打开详细管理菜单。现在可以单独开关货船、直升机、玩家等通知，物品提醒和商店设置也可直接在此菜单管理。",
        ["Robust 100-message window check to prevent duplicate notifications during reconnects."] = "使用更稳健的 100 条消息窗口检查，防止重连时重复通知。",
        ["Say goodbye to flickering markers! If a high-pop server (like Rustoria) delays data, the app now uses predictive interpolation to keep players and events moving smoothly. Shops will no longer disappear during brief connection drops."] = "告别标记闪烁。高人数服务器（如 Rustoria）数据延迟时，应用会使用预测插值让玩家和事件平滑移动；短暂掉线时商店也不会消失。",
        ["Team chat notifications for Deep Sea events (direction always West, not actual direction)."] = "深海事件队伍聊天通知（方向始终显示为西方，不代表真实方向）。",
        ["The Shop Search is now an ultra-fast, modern embedded interface. The 'Profit Trades' (Arbitrage) and 'Buy X for Y' tools have been moved to their own dedicated windows. The app also introduces 'Suspicious Shop' detection to warn you about bait bases."] = "商店搜索现在是高速现代化内嵌界面。“利润交易”（套利）和“用 Y 买 X”工具已移到独立窗口。应用还加入“可疑商店”检测，用于提醒诱饵基地。",
        ["The app can now reside in your system tray to collect data 24/7. Improved single-instance handling ensures the window pops up when you need it."] = "应用现在可以常驻系统托盘全天候收集数据。改进的单实例处理确保需要时窗口能弹出。",
        ["Upload existing PNG images to use as crosshairs. The editor automatically scales them to fit the pixel grid. You can also right-click to erase individual pixels and easily rename or delete your creations."] = "上传现有 PNG 图片作为准星。编辑器会自动缩放以适配像素网格。你也可以右键擦除单个像素，并轻松重命名或删除作品。"
    };

    public static bool IsEnabled
    {
        get
        {
            var overrideLanguage = Environment.GetEnvironmentVariable("RUSTPLUS_DESKTOP_LANGUAGE");
            if (!string.IsNullOrWhiteSpace(overrideLanguage))
                return overrideLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(Environment.GetEnvironmentVariable("RUSTPLUS_DESKTOP_DISABLE_ZH"), "1", StringComparison.Ordinal))
                return false;

            var uiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.CurrentCulture;
            return uiCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                || culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string T(string text) => IsEnabled ? Translate(text) : text;

    public static string Translate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return TranslatePreservingWhitespace(text);
    }

    public static string UntilPhase(string duration, string phase)
    {
        if (!IsEnabled) return $"{duration} until {phase}";
        return phase.Equals("night", StringComparison.OrdinalIgnoreCase)
            ? $"距离夜晚还有 {duration}"
            : $"距离白天还有 {duration}";
    }

    public static void ApplyTo(DependencyObject root)
    {
        if (!IsEnabled) return;

        ApplyRecursive(root, new HashSet<DependencyObject>());

        if (root is FrameworkElement element)
        {
            element.Loaded -= LocalizeOnLoaded;
            element.Loaded += LocalizeOnLoaded;
        }
    }

    private static void LocalizeOnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject root)
            ApplyRecursive(root, new HashSet<DependencyObject>());
    }

    private static void ApplyRecursive(DependencyObject current, ISet<DependencyObject> visited)
    {
        if (!visited.Add(current)) return;

        ApplyToElement(current);

        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            ApplyRecursive(child, visited);

        if (current is Visual or Visual3D)
        {
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
                ApplyRecursive(VisualTreeHelper.GetChild(current, i), visited);
        }
    }

    private static void ApplyToElement(DependencyObject current)
    {
        if (current is Window window)
            window.Title = Translate(window.Title);

        if (current is TextBlock textBlock)
        {
            if (textBlock.Inlines.Count > 0)
            {
                foreach (var run in textBlock.Inlines.OfType<Run>())
                {
                    if (!BindingOperations.IsDataBound(run, Run.TextProperty))
                        run.Text = Translate(run.Text);
                }
            }
            else if (!BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
            {
                textBlock.Text = Translate(textBlock.Text);
            }
        }

        if (current is ContentControl contentControl
            && contentControl.Content is string content
            && !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty))
        {
            contentControl.Content = Translate(content);
        }

        if (current is HeaderedContentControl headeredContent
            && headeredContent.Header is string contentHeader
            && !BindingOperations.IsDataBound(headeredContent, HeaderedContentControl.HeaderProperty))
        {
            headeredContent.Header = Translate(contentHeader);
        }

        if (current is HeaderedItemsControl headeredItems
            && headeredItems.Header is string itemsHeader
            && !BindingOperations.IsDataBound(headeredItems, HeaderedItemsControl.HeaderProperty))
        {
            headeredItems.Header = Translate(itemsHeader);
        }

        if (current is DataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
            {
                if (column.Header is string columnHeader)
                    column.Header = Translate(columnHeader);
            }
        }

        if (current is FrameworkElement element && element.ToolTip is string toolTip)
            element.ToolTip = Translate(toolTip);
    }

    private static string TranslatePreservingWhitespace(string source)
    {
        var leading = source.Length - source.TrimStart().Length;
        var trailing = source.Length - source.TrimEnd().Length;
        var coreLength = source.Length - leading - trailing;
        if (coreLength <= 0) return source;

        var core = source.Substring(leading, coreLength);
        var translated = TranslateCore(core);
        if (ReferenceEquals(translated, core) || translated == core) return source;

        return source[..leading] + translated + source[(source.Length - trailing)..];
    }

    private static string TranslateCore(string text)
    {
        if (ZhCn.TryGetValue(text, out var translated))
            return translated;

        return text switch
        {
            "v4.5.2  |  Shop Polling Hotfix (May 12, 2026)" => "v4.5.2  |  商店轮询热修复（2026-05-12）",
            "v4.5.0" => "v4.5.0",
            "v4.3.1  |  Bugfixes & Improvements" => "v4.3.1  |  Bug 修复与改进",
            "v4.2  |  Cargo Ship Overhaul" => "v4.2  |  货船功能重做",
            "v4.1.4  |  Granular Chat Notifications" => "v4.1.4  |  细粒度聊天通知",
            "v4.1.2  |  Custom Crosshair Editor" => "v4.1.2  |  自定义准星编辑器",
            "v4.0.0  |  The Map & Stability Overhaul (Major Release)" => "v4.0.0  |  地图与稳定性大改版（重大版本）",
            "v3.5.4  |  Player Management & Optimization" => "v3.5.4  |  玩家管理与优化",
            "v3.5.3  |  The Intelligence Update" => "v3.5.3  |  情报更新",
            "v3.4.0  |  Hierarchical Grouping & Advanced Alarms" => "v3.4.0  |  分层分组与高级警报",
            "v3.3.1  |  Stability & Event Awareness" => "v3.3.1  |  稳定性与事件感知",
            "v3.3.0  |  Oilrig Timer & !leader Command" => "v3.3.0  |  石油平台计时与 !leader 指令",
            "• v3.2.0: Smart Device Import/Export for teams." => "• v3.2.0：队伍智能设备导入/导出。",
            "• v3.1.0: Full Storage Monitor & Upkeep integration." => "• v3.1.0：完整储物监控与维护集成。",
            "• v3.0.0: Massive Shop Analytics overhaul & Custom Map Overlays." => "• v3.0.0：商店分析大改版与自定义地图覆盖层。",
            _ => text
        };
    }
}
