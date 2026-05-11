using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
                if (!value) IsFullConnected = false; // If API disconnects, full connect is also gone
            }
        }
    }

    private bool _isFullConnected;
    public bool IsFullConnected
    {
        get => _isFullConnected;
        set { if (_isFullConnected != value) { _isFullConnected = value; OnProp(); } }
    }

    public bool UseFacepunchProxy { get; set; } = false;

    public ServerProfile()
    {
        Devices.CollectionChanged += (s, e) => NotifySmartSwitchesChanged();
    }

    public ObservableCollection<SmartDevice> Devices { get; set; } = new();
    public ObservableCollection<string> CameraIds { get; set; } = new();

    public double LearnedDaySpeed { get; set; } = 12.0 / 50.0;
    public double LearnedNightSpeed { get; set; } = 12.0 / 10.0;

    // --- CHAT COMMANDS SETTINGS ---
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
            list.AddRange(System.Linq.Enumerable.Where(Devices, d => d.Kind == "SmartSwitch"));
            return list;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSmartSwitches => System.Linq.Enumerable.Any(SmartSwitches);

    public void NotifySmartSwitchesChanged()
    {
        OnProp(nameof(SmartSwitches));
        OnProp(nameof(HasSmartSwitches));
    }


    private string _cmdPop = "pop";
    public string CmdPop
    {
        get => _cmdPop;
        set { _cmdPop = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private string _cmdTime = "time";
    public string CmdTime
    {
        get => _cmdTime;
        set { _cmdTime = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private string _cmdPromote = "promote";
    public string CmdPromote
    {
        get => _cmdPromote;
        set { _cmdPromote = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private string _cmdDeepSea = "deepsea";
    public string CmdDeepSea
    {
        get => _cmdDeepSea;
        set { _cmdDeepSea = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private string _cmdCargo = "cargo";
    public string CmdCargo
    {
        get => _cmdCargo;
        set { _cmdCargo = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private string _cmdOilRig = "oilrig";
    public string CmdOilRig
    {
        get => _cmdOilRig;
        set { _cmdOilRig = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private string _cmdSwitch1 = "switch1";
    public string CmdSwitch1
    {
        get => _cmdSwitch1;
        set { _cmdSwitch1 = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private uint? _boundSwitchId1;
    public uint? BoundSwitchId1
    {
        get => _boundSwitchId1;
        set { _boundSwitchId1 = value; OnProp(); }
    }

    private string _cmdSwitch2 = "switch2";
    public string CmdSwitch2
    {
        get => _cmdSwitch2;
        set { _cmdSwitch2 = value?.TrimStart('!') ?? ""; OnProp(); }
    }

    private uint? _boundSwitchId2;
    public uint? BoundSwitchId2
    {
        get => _boundSwitchId2;
        set { _boundSwitchId2 = value; OnProp(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

