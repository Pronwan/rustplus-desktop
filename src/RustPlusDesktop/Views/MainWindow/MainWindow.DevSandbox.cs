using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Social;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Developer sandbox helpers for injecting fake server pairings, device pairings,
/// alarms, and chat messages so the UI behaves normally during local testing.
/// </summary>
public partial class MainWindow
{
    private bool _devFakeChatEnabled;
    private bool _devFakeGlobalChatEnabled;
    private uint _nextFakeEntityId = 900_000_001;

    private bool IsDevChatSandboxActive =>
        _devFakeChatEnabled || (_vm?.Selected?.IsFakeSandbox == true);

    internal static ServerProfile CreateFakeServerProfile(string? customName, int existingCount, string? steamId64)
    {
        string name = string.IsNullOrWhiteSpace(customName)
            ? $"[DEV] Sandbox Server #{existingCount + 1}"
            : customName.Trim();

        return new ServerProfile
        {
            Name = name,
            Host = ServerProfile.FakeSandboxHost,
            Port = 28082 + Math.Max(0, existingCount),
            ServerId = Guid.NewGuid().ToString(),
            SteamId64 = string.IsNullOrWhiteSpace(steamId64) ? "76561198000000001" : steamId64,
            PlayerToken = "dev-sandbox",
            Description = "Simulated paired server for UI testing",
            ChatCommandsEnabled = true,
            ClanChatCommandsEnabled = true,
            IsFakeSandbox = true,
            Devices = new ObservableCollection<SmartDevice>()
        };
    }

    internal static SmartDevice CreateFakeSmartDevice(string kind, uint entityId, string? customName, int sameKindIndex)
    {
        string normalized = SmartDevice.NormalizeKind(kind) ?? "SmartSwitch";
        string defaultName = normalized switch
        {
            "SmartAlarm" => $"Perimeter Alarm #{sameKindIndex}",
            "StorageMonitor" => $"Main Tool Cupboard #{sameKindIndex}",
            _ => $"Base Switch #{sameKindIndex}",
        };

        var device = new SmartDevice
        {
            EntityId = entityId,
            Name = string.IsNullOrWhiteSpace(customName) ? defaultName : customName.Trim(),
            Kind = normalized,
            IsOn = string.Equals(normalized, "StorageMonitor", StringComparison.OrdinalIgnoreCase) ? null : false,
            IsMissing = false,
            IsFakeSandbox = true,
            PairedX = 0.48 + (sameKindIndex % 4) * 0.02,
            PairedY = 0.52 - (sameKindIndex % 4) * 0.015,
            PairedLocationCapturedAt = DateTime.UtcNow,
        };

        if (string.Equals(normalized, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
        {
            device.InGameAlarmTitle = device.Name;
        }
        else if (string.Equals(normalized, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
        {
            var snap = new StorageSnapshot
            {
                IsToolCupboard = true,
                UpkeepSeconds = 115_200,
                SnapshotUtc = DateTime.UtcNow
            };
            snap.Items.Add(new StorageItemVM { ItemId = -151838493, ShortName = "wood", Amount = 4200, MaxStack = 1000 });
            snap.Items.Add(new StorageItemVM { ItemId = -2099697608, ShortName = "stones", Amount = 8500, MaxStack = 1000 });
            snap.Items.Add(new StorageItemVM { ItemId = 69511070, ShortName = "metal.fragments", Amount = 2450, MaxStack = 1000 });
            snap.Items.Add(new StorageItemVM { ItemId = 317398316, ShortName = "metal.refined", Amount = 160, MaxStack = 100 });
            device.Storage = snap;
        }

        return device;
    }

    internal ServerProfile AddFakeServerPairing(string? customName = null, bool selectServer = true)
    {
        var prof = CreateFakeServerProfile(customName, _vm.Servers.Count, _vm.SteamId64);
        _vm.AddServer(prof);
        if (selectServer) _vm.Selected = prof;
        _vm.Save();

        NotificationCenterService.AddNotification(new RustPlusNotification(
            type: "Pairing",
            title: prof.Name,
            message: "Server paired (Dev Sandbox).",
            serverIp: prof.Host,
            serverPort: prof.Port,
            serverName: prof.Name));

        AppendLog($"[DEV] Fake server paired -> {prof.Name} ({prof.Host}:{prof.Port})");
        return prof;
    }

    internal SmartDevice AddFakeDevicePairing(string kind, string? customName = null, ServerProfile? targetServer = null)
    {
        var prof = targetServer ?? _vm.Selected ?? AddFakeServerPairing();
        prof.Devices ??= new ObservableCollection<SmartDevice>();

        uint nextId = AllocateNextFakeEntityId(prof);
        int sameKindCount = prof.Devices.Count(d => string.Equals(d.Kind, SmartDevice.NormalizeKind(kind) ?? kind, StringComparison.OrdinalIgnoreCase));
        var dev = CreateFakeSmartDevice(kind, nextId, customName, sameKindCount + 1);

        if (string.Equals(dev.Kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
        {
            dev.IsExpanded = true;
        }

        prof.Devices.Add(dev);
        prof.SyncChatCommands();
        _vm.Save();

        NotificationCenterService.AddNotification(new RustPlusNotification(
            type: "Pairing",
            title: dev.PureName,
            message: $"{dev.Kind} paired (ID {dev.EntityId})",
            serverIp: prof.Host,
            serverPort: prof.Port,
            serverName: prof.Name)
        {
            EntityId = dev.EntityId,
            EntityName = dev.PureName
        });

        AppendLog($"[DEV] Fake device paired -> {dev.Display} on '{prof.Name}'");
        return dev;
    }

    internal void TriggerFakeAlarm(SmartDevice? targetAlarm = null)
    {
        var prof = _vm.Selected ?? AddFakeServerPairing();
        prof.Devices ??= new ObservableCollection<SmartDevice>();

        var alarm = targetAlarm ?? prof.Devices.FirstOrDefault(d => string.Equals(d.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
            ?? AddFakeDevicePairing("SmartAlarm", null, prof);

        var notif = new AlarmNotification(
            Timestamp: DateTime.Now,
            Server: prof.Name,
            DeviceName: alarm.PureName,
            EntityId: alarm.EntityId,
            Message: "Simulated alarm triggered from Dev Sandbox.",
            Ip: prof.Host,
            Port: prof.Port,
            Title: alarm.InGameAlarmTitle ?? alarm.PureName);

        NotificationCenterService.AddNotification(new RustPlusNotification(
            type: "Alarm",
            title: alarm.PureName,
            message: notif.Message,
            serverIp: prof.Host,
            serverPort: prof.Port,
            serverName: prof.Name)
        {
            EntityId = alarm.EntityId,
            EntityName = alarm.PureName
        });

        alarm.IsOn = true;
        TriggerLogicEngineOnDeviceEvent(alarm.EntityId, true);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(10_000);
            await Dispatcher.InvokeAsync(() => alarm.IsOn = false);
        });

        PlayAlarmAudio(alarm);

        if (alarm.OverlayEnabled)
        {
            AddAlarmToOverlay(alarm, notif);
        }

        if (alarm.PopupEnabled)
        {
            if (_alarmWin is null || !_alarmWin.IsLoaded)
            {
                _alarmWin = new AlarmWindow { Owner = this };
                _alarmWin.Closed += (_, __) => _alarmWin = null;
                _alarmWin.Show();
            }
            _alarmWin.Add(notif);
        }

        if (TrackingService.NotificationsToastEnabled)
        {
            ShowInfoSnackbar(alarm.PureName, notif.Message, WpfUi.ControlAppearance.Danger);
        }

        AppendLog($"[DEV] Simulated Smart Alarm triggered -> {alarm.PureName} (#{alarm.EntityId})");
    }

    internal void AddFakeChatMessage(ChatChannel channel, string? author = null, string? text = null)
    {
        _devFakeChatEnabled = true;

        string senderName = string.IsNullOrWhiteSpace(author)
            ? (channel == ChatChannel.Global ? "RustPlayer#42" : "Teammate (Dev)")
            : author.Trim();

        string body = string.IsNullOrWhiteSpace(text)
            ? channel switch
            {
                ChatChannel.Clan => "Clan check-in from Dev Sandbox — raiders near G14!",
                ChatChannel.Global => "Anyone want to trade crude oil for sulfur near Outpost?",
                _ => "Team check-in from Dev Sandbox — autoturrets are armed!"
            }
            : text.Trim();

        if (channel == ChatChannel.Global)
        {
            InjectFakeGlobalMessage(senderName, body, isMine: false);
            AppendLog($"[DEV] Fake Global chat added -> {senderName}: {body}");
            return;
        }

        var prof = _vm.Selected ?? AddFakeServerPairing();
        var msg = new TeamChatMessage(DateTime.UtcNow, senderName, 76561198000000042UL, body);
        AppendChatIfNew(msg, channel, isHistorical: false, skipDiscordRelay: true);
        if (ChatContentBorder?.Visibility == Visibility.Visible && _activeChatChannel == channel)
        {
            RebuildChatMessages();
            ScrollChatToBottom();
        }
        AppendLog($"[DEV] Fake {channel} chat added on '{prof.Name}' -> {senderName}: {body}");
    }

    internal void SeedFakeStarterKit()
    {
        var server = AddFakeServerPairing("[DEV] Rustopia EU Main", selectServer: true);
        AddFakeDevicePairing("SmartSwitch", "Main Gate Doors", server);
        AddFakeDevicePairing("SmartSwitch", "Auto Turrets", server);
        AddFakeDevicePairing("SmartAlarm", "Compound Breach Alarm", server);
        AddFakeDevicePairing("StorageMonitor", "Main Tool Cupboard", server);

        AddFakeChatMessage(ChatChannel.Team, "Alex", "All turrets fueled, flipping main gate switch now.");
        AddFakeChatMessage(ChatChannel.Team, "Sam", "TC upkeep looks solid for 32 hours.");
        AddFakeChatMessage(ChatChannel.Clan, "ClanLead", "Roam squad gearing up at Launch Site in 5m.");
        AddFakeChatMessage(ChatChannel.Global, "OutpostTrader", "Selling M249 & HQM at K12 vending machine!");

        ShowInfoSnackbar(
            "Developer Sandbox",
            $"Sample server '{server.Name}', 4 devices, and sample chats added.",
            WpfUi.ControlAppearance.Success);
    }

    internal (int ServersRemoved, int DevicesRemoved) ClearFakeSandboxData()
    {
        int devicesRemoved = 0;
        foreach (var prof in _vm.Servers.Where(s => !s.IsFakeSandbox).ToList())
        {
            if (prof.Devices == null) continue;
            var fakes = prof.Devices.Where(d => d.IsFakeSandbox).ToList();
            foreach (var f in fakes)
            {
                prof.Devices.Remove(f);
                devicesRemoved++;
            }
            if (fakes.Count > 0) prof.SyncChatCommands();
        }

        var fakeServers = _vm.Servers.Where(s => s.IsFakeSandbox).ToList();
        foreach (var srv in fakeServers)
        {
            devicesRemoved += srv.Devices?.Count ?? 0;
            _vm.Servers.Remove(srv);
        }

        if (_vm.Selected == null || fakeServers.Contains(_vm.Selected))
        {
            _vm.Selected = _vm.Servers.FirstOrDefault();
        }

        GlobalChatFeed.ClearDevMessages();
        _devFakeChatEnabled = false;
        _devFakeGlobalChatEnabled = false;
        ApplyGlobalChatTickerVisibility();
        _vm.Save();

        AppendLog($"[DEV] Cleared {fakeServers.Count} fake server(s) and {devicesRemoved} fake device(s).");
        return (fakeServers.Count, devicesRemoved);
    }

    private void InjectFakeGlobalMessage(string senderName, string body, bool isMine)
    {
        _devFakeGlobalChatEnabled = true;
        EnsureGlobalLaneWired();
        EnsureGlobalChatTickerWired();

        var line = new ChatLine
        {
            Id = "dev-" + Guid.NewGuid().ToString("N"),
            Body = body,
            Room = GlobalChatFeed.DefaultRoom,
            SenderId = isMine ? "dev-self" : "dev-peer",
            SenderName = senderName,
            IsSupporter = !isMine,
            BadgeLabel = "DEV",
            BadgeColor = "#60CDFF",
            SentAt = DateTime.UtcNow,
            IsMine = isMine
        };

        GlobalChatFeed.InjectDevMessage(line);
        ApplyGlobalChatTickerVisibility();
        GlobalChatTickerStrip?.Render();
    }

    private uint AllocateNextFakeEntityId(ServerProfile prof)
    {
        while (prof.Devices != null && FindDeviceById(prof.Devices, _nextFakeEntityId) != null)
        {
            _nextFakeEntityId++;
        }
        return _nextFakeEntityId++;
    }
}

