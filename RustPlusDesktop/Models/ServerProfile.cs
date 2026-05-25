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
    }
    public ObservableCollection<string> CameraIds { get; set; } = new();

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
        while (SwitchCommandMappings.Count < switches.Count)
        {
            int next = SwitchCommandMappings.Count + 1;
            SwitchCommandMappings.Add(new ChatCommandMapping 
            { 
                Label = $"Switch {next}", 
                Command = $"switch{next}", 
                EntityId = switches[next-1].EntityId 
            });
        }
        // Remove mappings for switches that no longer exist? 
        // Better not to remove, just leave them or mark as invalid, but user said "extends the list accordingly".
        // I'll update the EntityId of existing ones if they are 0
        for (int i = 0; i < SwitchCommandMappings.Count; i++)
        {
            if (SwitchCommandMappings[i].EntityId == 0 && i < switches.Count)
                SwitchCommandMappings[i].EntityId = switches[i].EntityId;
        }

        // Sync Upkeep (Storage Monitors on TCs)
        var tcs = AllDevices.Where(d => (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor") && (d.Storage == null || d.Storage.IsToolCupboard || d.Storage.ItemsCount == 0)).ToList();
        while (UpkeepCommandMappings.Count < tcs.Count)
        {
            int next = UpkeepCommandMappings.Count + 1;
            string cmd = next == 1 ? "upkeep" : $"upkeep{next}";
            UpkeepCommandMappings.Add(new ChatCommandMapping 
            { 
                Label = next == 1 ? "Upkeep" : $"Upkeep {next}", 
                Command = cmd, 
                EntityId = tcs[next-1].EntityId 
            });
        }
        for (int i = 0; i < UpkeepCommandMappings.Count; i++)
        {
            if (UpkeepCommandMappings[i].EntityId == 0 && i < tcs.Count)
                UpkeepCommandMappings[i].EntityId = tcs[i].EntityId;
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

