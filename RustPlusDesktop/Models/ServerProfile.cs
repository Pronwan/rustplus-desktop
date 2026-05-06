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
        set { _isConnected = value; OnProp(); }
    }

    public bool UseFacepunchProxy { get; set; } = false;

    public ObservableCollection<SmartDevice> Devices { get; set; } = new();
    public ObservableCollection<string> CameraIds { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

