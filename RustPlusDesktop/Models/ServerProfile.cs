using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models;


public class ServerProfile : INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnProp(); }
    }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { _description = value; OnProp(); }
    }

    public string Host { get; set; } = "";
    public int Port { get; set; } = 28082;
    public string SteamId64 { get; set; } = "";
    public string PlayerToken { get; set; } = "";
    public string? BattleMetricsId { get; set; } = null;

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set 
        { 
            if (_isConnected != value)
            {
                _isConnected = value; 
                OnProp();
                OnProp(nameof(IsFullConnected));
                if (!value) IsFullConnected = false; 
            }
        }
    }

    private bool _isFullConnected;
    public bool IsFullConnected
    {
        get => _isFullConnected;
        set 
        { 
            if (_isFullConnected != value) 
            { 
                _isFullConnected = value; 
                OnProp(); 
                OnProp(nameof(IsConnected));
            } 
        }
    }

    public bool UseFacepunchProxy { get; set; } = false;

    public ServerProfile()
    {
        _devices.CollectionChanged += Devices_CollectionChanged;
    }

    private ObservableCollection<SmartDevice> _devices = new();
    public ObservableCollection<SmartDevice> Devices 
    { 
        get => _devices;
        set
        {
            if (_devices != null) _devices.CollectionChanged -= Devices_CollectionChanged;
            _devices = value ?? new();
            _devices.CollectionChanged += Devices_CollectionChanged;
            NotifySmartSwitchesChanged();
            OnProp();
        }
    }

    private void Devices_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        NotifySmartSwitchesChanged();
        NotifyFlatDevicesChanged();
    }

    public void GroupDevices(System.Collections.Generic.IEnumerable<SmartDevice> toRemove, SmartDevice newGroup)
    {
        if (_devices != null) _devices.CollectionChanged -= Devices_CollectionChanged;
        
        foreach (var d in toRemove)
        {
            _devices!.Remove(d);
            if (newGroup.Children == null) newGroup.Children = new System.Collections.ObjectModel.ObservableCollection<SmartDevice>();
            newGroup.Children.Add(d);
        }
        _devices!.Insert(0, newGroup);
        
        if (_devices != null) _devices.CollectionChanged += Devices_CollectionChanged;
        NotifySmartSwitchesChanged();
        OnProp(nameof(Devices));
    }
    public ObservableCollection<string> CameraIds { get; set; } = new();

    [JsonPropertyName("deathMarkers")]
    public List<DeathMarkerData> DeathMarkers { get; set; } = new();

    public double LearnedDaySpeed { get; set; } = 12.0 / 50.0;
    public double LearnedNightSpeed { get; set; } = 12.0 / 10.0;

    // --- CHAT COMMANDS SETTINGS ---
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IEnumerable<SmartDevice> AllDevices
    {
        get
        {
            var list = new System.Collections.Generic.List<SmartDevice>();
            void Flatten(System.Collections.Generic.IEnumerable<SmartDevice> source)
            {
                foreach (var d in source)
                {
                    if (!d.IsGroup) list.Add(d);
                    if (d.Children != null) Flatten(d.Children);
                }
            }
            Flatten(Devices);
            return list;
        }
    }

    private bool _chatCommandsEnabled;
    public bool ChatCommandsEnabled
    {
        get => _chatCommandsEnabled;
        set { _chatCommandsEnabled = value; OnProp(); }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IEnumerable<SmartDevice> SmartSwitches 
    {
        get
        {
            var list = new System.Collections.Generic.List<SmartDevice>();
            list.Add(new SmartDevice { Name = "(None)", EntityId = 0 });
            list.AddRange(System.Linq.Enumerable.Where(AllDevices, d => d.Kind == "SmartSwitch"));
            return list;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.IEnumerable<SmartDevice> TcMonitors 
    {
        get
        {
            var list = new System.Collections.Generic.List<SmartDevice>();
            list.Add(new SmartDevice { Name = "(None)", EntityId = 0 });
            list.AddRange(System.Linq.Enumerable.Where(AllDevices, d => (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor") && (d.Storage == null || d.Storage.IsToolCupboard || d.Storage.ItemsCount == 0)));
            return list;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSmartSwitches => System.Linq.Enumerable.Any(SmartSwitches);

    public void NotifySmartSwitchesChanged()
    {
        OnProp(nameof(SmartSwitches));
        OnProp(nameof(TcMonitors));
        OnProp(nameof(HasSmartSwitches));
    }


    private string _cmdPop = "pop";
    public string CmdPop
    {
        get => _cmdPop;
        set { _cmdPop = ValidateCommand(value, "pop"); OnProp(); }
    }

    private string _cmdList = "commands";
    public string CmdList
    {
        get => _cmdList;
        set { _cmdList = ValidateCommand(value, "commands"); OnProp(); }
    }

    private string _cmdTime = "time";
    public string CmdTime
    {
        get => _cmdTime;
        set { _cmdTime = ValidateCommand(value, "time"); OnProp(); }
    }

    private string _cmdPromote = "promote";
    public string CmdPromote
    {
        get => _cmdPromote;
        set { _cmdPromote = ValidateCommand(value, "promote"); OnProp(); }
    }

    private string _cmdDeepSea = "deepsea";
    public string CmdDeepSea
    {
        get => _cmdDeepSea;
        set { _cmdDeepSea = ValidateCommand(value, "deepsea"); OnProp(); }
    }

    private string _cmdCargo = "cargo";
    public string CmdCargo
    {
        get => _cmdCargo;
        set { _cmdCargo = ValidateCommand(value, "cargo"); OnProp(); }
    }

    private string _cmdAfk = "afk";
    public string CmdAfk
    {
        get => _cmdAfk;
        set { _cmdAfk = ValidateCommand(value, "afk"); OnProp(); }
    }

    private string _chatCommandPrefix = "!";
    public string ChatCommandPrefix
    {
        get => string.IsNullOrEmpty(_chatCommandPrefix) ? "!" : _chatCommandPrefix;
        set { if (value == "!" || value == "." || value == "," || value == "\\") { _chatCommandPrefix = value; OnProp(); } }
    }

    private string _cmdOilRig = "oilrig";
    public string CmdOilRig
    {
        get => _cmdOilRig;
        set { _cmdOilRig = ValidateCommand(value, "oilrig"); OnProp(); }
    }

    private string _cmdHeli = "heli";
    public string CmdHeli
    {
        get => _cmdHeli;
        set { _cmdHeli = ValidateCommand(value, "heli"); OnProp(); }
    }

    private string _cmdVendor = "vendor";
    public string CmdVendor
    {
        get => _cmdVendor;
        set { _cmdVendor = ValidateCommand(value, "vendor"); OnProp(); }
    }

    private string _cmdUpkeepDetail = "upkeepdetail";
    public string CmdUpkeepDetail
    {
        get => _cmdUpkeepDetail;
        set { _cmdUpkeepDetail = ValidateCommand(value, "upkeepdetail"); OnProp(); }
    }

    private int _chatCommandDelaySeconds = 2;
    public int ChatCommandDelaySeconds
    {
        get => _chatCommandDelaySeconds;
        set { if (value >= 1 && value <= 5) { _chatCommandDelaySeconds = value; OnProp(); } }
    }

    private double _chatResponseDelaySeconds = 0.5;
    public double ChatResponseDelaySeconds
    {
        get => _chatResponseDelaySeconds;
        set { if (value >= 0.0 && value <= 5.0) { _chatResponseDelaySeconds = value; OnProp(); } }
    }

    private ObservableCollection<ChatCommandMapping> _switchCommandMappings = new();
    public ObservableCollection<ChatCommandMapping> SwitchCommandMappings
    {
        get => _switchCommandMappings;
        set { _switchCommandMappings = value ?? new(); OnProp(); }
    }

    private ObservableCollection<ChatCommandMapping> _upkeepCommandMappings = new();
    public ObservableCollection<ChatCommandMapping> UpkeepCommandMappings
    {
        get => _upkeepCommandMappings;
        set { _upkeepCommandMappings = value ?? new(); OnProp(); }
    }

    private string ValidateCommand(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        var trimmed = value.Trim().TrimStart('!');
        if (trimmed.Length > 0 && char.IsDigit(trimmed[0]))
        {
            // Forbid starting with a number. Return the previous value or default if no previous.
            return defaultValue; 
        }
        return trimmed;
    }

    private string _cmdCustomTimer = "timer";
    public string CmdCustomTimer
    {
        get => _cmdCustomTimer;
        set { _cmdCustomTimer = ValidateCommand(value, "timer"); OnProp(); }
    }

    private bool _alertCustomTimer = true;
    public bool AlertCustomTimer
    {
        get => _alertCustomTimer;
        set { _alertCustomTimer = value; OnProp(); }
    }

    private string _discordWebhookChatAlertsUrl = "";
    public string DiscordWebhookChatAlertsUrl
    {
        get => _discordWebhookChatAlertsUrl;
        set 
        { 
            _discordWebhookChatAlertsUrl = value;
            if (string.IsNullOrWhiteSpace(value))
            {
                DiscordWebhookChatAlertsEnabled = false;
            }
            else if (!DiscordWebhookChatAlertsEnabled)
            {
                DiscordWebhookChatAlertsEnabled = true;
            }
            OnProp(); 
        }
    }

    private bool _discordWebhookChatAlertsEnabled = false;
    public bool DiscordWebhookChatAlertsEnabled
    {
        get => _discordWebhookChatAlertsEnabled;
        set { _discordWebhookChatAlertsEnabled = value; OnProp(); }
    }

    private bool _discordWebhookChatAlertsTts = false;
    public bool DiscordWebhookChatAlertsTts
    {
        get => _discordWebhookChatAlertsTts;
        set { _discordWebhookChatAlertsTts = value; OnProp(); }
    }

    private bool _timerAlarmEnabled = true;
    public bool TimerAlarmEnabled
    {
        get => _timerAlarmEnabled;
        set { _timerAlarmEnabled = value; OnProp(); }
    }

    private string? _timerAlarmAudioPath;
    public string? TimerAlarmAudioPath
    {
        get => _timerAlarmAudioPath;
        set { _timerAlarmAudioPath = value; OnProp(); }
    }

    private string? _timerCountdownAudioPath;
    public string? TimerCountdownAudioPath
    {
        get => _timerCountdownAudioPath;
        set { _timerCountdownAudioPath = value; OnProp(); }
    }

    private int _timerAlarmSnoozeMinutes = 5;
    public int TimerAlarmSnoozeMinutes
    {
        get => _timerAlarmSnoozeMinutes;
        set { _timerAlarmSnoozeMinutes = Math.Max(0, value); OnProp(); }
    }

    private int _timerAlarmBeepDurationSeconds = 5;
    public int TimerAlarmBeepDurationSeconds
    {
        get => _timerAlarmBeepDurationSeconds;
        set { _timerAlarmBeepDurationSeconds = Math.Max(1, value); OnProp(); }
    }

    private ObservableCollection<CustomTimer> _customTimers = new();
    public ObservableCollection<CustomTimer> CustomTimers
    {
        get => _customTimers;
        set { _customTimers = value ?? new(); OnProp(); }
    }


    [JsonIgnore]
    public string CmdSwitch1 { get => SwitchCommandMappings.Count > 0 ? SwitchCommandMappings[0].Command : "switch1"; set { if (SwitchCommandMappings.Count > 0) SwitchCommandMappings[0].Command = ValidateCommand(value, "switch1"); } }
    [JsonIgnore]
    public uint? BoundSwitchId1 { get => SwitchCommandMappings.Count > 0 ? SwitchCommandMappings[0].EntityId : null; set { if (SwitchCommandMappings.Count > 0) SwitchCommandMappings[0].EntityId = value ?? 0; } }

    [JsonIgnore]
    public string CmdSwitch2 { get => SwitchCommandMappings.Count > 1 ? SwitchCommandMappings[1].Command : "switch2"; set { if (SwitchCommandMappings.Count > 1) SwitchCommandMappings[1].Command = ValidateCommand(value, "switch2"); } }
    [JsonIgnore]
    public uint? BoundSwitchId2 { get => SwitchCommandMappings.Count > 1 ? SwitchCommandMappings[1].EntityId : null; set { if (SwitchCommandMappings.Count > 1) SwitchCommandMappings[1].EntityId = value ?? 0; } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void SyncChatCommands()
    {
        // Sync Switches
        var switches = AllDevices.Where(d => d.Kind == "SmartSwitch").ToList();
        var validSwitchIds = new HashSet<uint>(switches.Select(s => s.EntityId));
        
        for (int i = SwitchCommandMappings.Count - 1; i >= 0; i--)
        {
            var m = SwitchCommandMappings[i];
            if (m.EntityId != 0 && !validSwitchIds.Contains(m.EntityId))
                SwitchCommandMappings.RemoveAt(i);
        }

        foreach (var sw in switches)
        {
            if (!SwitchCommandMappings.Any(m => m.EntityId == sw.EntityId))
            {
                int next = SwitchCommandMappings.Count + 1;
                SwitchCommandMappings.Add(new ChatCommandMapping 
                { 
                    Label = $"Switch {next}", 
                    Command = $"switch{next}", 
                    EntityId = sw.EntityId 
                });
            }
        }

        // Sync Upkeep (Storage Monitors on TCs)
        var tcs = AllDevices.Where(d => (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor") && (d.Storage == null || d.Storage.IsToolCupboard || d.Storage.ItemsCount == 0)).ToList();
        var validTcIds = new HashSet<uint>(tcs.Select(s => s.EntityId));
        
        for (int i = UpkeepCommandMappings.Count - 1; i >= 0; i--)
        {
            var m = UpkeepCommandMappings[i];
            if (m.EntityId != 0 && !validTcIds.Contains(m.EntityId))
                UpkeepCommandMappings.RemoveAt(i);
        }

        foreach (var tc in tcs)
        {
            if (!UpkeepCommandMappings.Any(m => m.EntityId == tc.EntityId))
            {
                int next = UpkeepCommandMappings.Count + 1;
                string cmd = next == 1 ? "upkeep" : $"upkeep{next}";
                UpkeepCommandMappings.Add(new ChatCommandMapping 
                { 
                    Label = next == 1 ? "Upkeep" : $"Upkeep {next}", 
                    Command = cmd, 
                    EntityId = tc.EntityId 
                });
            }
        }
        
        OnProp(nameof(SwitchCommandMappings));
        OnProp(nameof(UpkeepCommandMappings));
    }

    private string? _rustMapsMapId;
    public string? RustMapsMapId
    {
        get => _rustMapsMapId;
        set { _rustMapsMapId = value; OnProp(); }
    }

    private DateTime? _rustMapsFetchTime;
    public DateTime? RustMapsFetchTime
    {
        get => _rustMapsFetchTime;
        set { _rustMapsFetchTime = value; OnProp(); }
    }

    private DateTime? _rustMapsWipeTime;
    public DateTime? RustMapsWipeTime
    {
        get => _rustMapsWipeTime;
        set { _rustMapsWipeTime = value; OnProp(); }
    }

    private List<LogicRule> _logicRules = new();
    public List<LogicRule> LogicRules
    {
        get => _logicRules;
        set { _logicRules = value ?? new(); OnProp(); }
    }

    private bool _isLogicEngineActive = false;
    public bool IsLogicEngineActive
    {
        get => _isLogicEngineActive;
        set { if (_isLogicEngineActive != value) { _isLogicEngineActive = value; OnProp(); } }
    }

    private List<ulong> _subscribedTeammateSteamIds = new();
    public List<ulong> SubscribedTeammateSteamIds
    {
        get => _subscribedTeammateSteamIds;
        set { _subscribedTeammateSteamIds = value ?? new(); OnProp(); }
    }

    [JsonIgnore]
    public List<SmartDevice> FlatDevices
    {
        get
        {
            var result = new List<SmartDevice>();
            FlattenDevicesRecursive(_devices, result);
            return result;
        }
    }

    private void FlattenDevicesRecursive(IEnumerable<SmartDevice> source, List<SmartDevice> dest)
    {
        if (source == null) return;
        foreach (var d in source)
        {
            if (d == null) continue;
            dest.Add(d);
            if (d.Children != null)
            {
                FlattenDevicesRecursive(d.Children, dest);
            }
        }
    }

    public void NotifyFlatDevicesChanged()
    {
        OnProp(nameof(FlatDevices));
    }
}


public class ChatCommandMapping : INotifyPropertyChanged
{
    private string _label = "";
    public string Label { get => _label; set { _label = value; OnProp(); } }

    private string _command = "";
    public string Command 
    { 
        get => _command; 
        set 
        { 
            if (string.IsNullOrWhiteSpace(value)) { _command = ""; OnProp(); return; }
            var val = value.Trim().TrimStart('!');
            if (val.Length > 0 && char.IsDigit(val[0]))
            {
                // Revert or strip? User said "verbieten". I'll strip leading digits if they are typed.
                // Or just don't update if it's invalid.
                // Actually, let's just keep the old value if it starts with a digit.
                OnProp(); // Trigger refresh to show old value in UI
                return;
            }
            _command = val; 
            OnProp(); 
        } 
    }

    private uint _entityId;
    public uint EntityId { get => _entityId; set { _entityId = value; OnProp(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class CustomTimer : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    public string Id { get => _id; set { _id = value; OnProp(); } }

    private string _name = "";
    public string Name { get => _name; set { _name = value; OnProp(); } }

    private string _command = "";
    public string Command { get => _command; set { _command = value; OnProp(); } }

    private DateTime _endTimeUtc;
    public DateTime EndTimeUtc { get => _endTimeUtc; set { _endTimeUtc = value; OnProp(); } }

    private bool _enableCountdownAudio = true;
    public bool EnableCountdownAudio { get => _enableCountdownAudio; set { _enableCountdownAudio = value; OnProp(); } }

    private bool _enableAlarmAudio = false;
    public bool EnableAlarmAudio { get => _enableAlarmAudio; set { _enableAlarmAudio = value; OnProp(); } }

    [JsonIgnore]
    public string RemainingTimeText 
    {
        get 
        {
            var remaining = EndTimeUtc - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0) return "00:00:00";
            if (remaining.TotalHours >= 1.0) return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }

    public void RefreshRemainingTime()
    {
        OnProp(nameof(RemainingTimeText));
    }

    public bool CreatedNotified { get; set; }
    public bool Notified60 { get; set; }
    public bool Notified30 { get; set; }
    public bool Notified10 { get; set; }
    public bool Notified3 { get; set; }
    [JsonIgnore]
    public bool CountdownAudioPlayed { get; set; }
    [JsonIgnore]
    public bool AlarmPlayed { get; set; }
    [JsonIgnore]
    public DateTime? SnoozedUntilUtc { get; set; }
    [JsonIgnore]
    public DateTime? AutoDeleteAtUtc { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

