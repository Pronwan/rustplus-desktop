using System;
using System.Collections.ObjectModel;

namespace RustPlusDesk.Models;


public class ServerProfile
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 28082;
    public string SteamId64 { get; set; } = "";
    public string PlayerToken { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool UseFacepunchProxy { get; set; } = false;

    public ObservableCollection<SmartDevice> Devices { get; set; } = new();
    public ObservableCollection<string> CameraIds { get; set; } = new();
}

