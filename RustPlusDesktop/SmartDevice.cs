using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RustPlusDesk.Models;

public class SmartDevice : INotifyPropertyChanged
{


    private uint _entityId;
    public uint EntityId
    {
        get => _entityId;
        set { if (_entityId != value) { _entityId = value; OnProp(); OnProp(nameof(Display)); } }
    }

    private string? _name;
    public string? Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnProp(); OnProp(nameof(Display)); } }
    }

    private string? _kind;
    public string? Kind
    {
        get => _kind;
        set { if (_kind != value) { _kind = value; OnProp(); OnProp(nameof(Display)); } }
    }

    private bool? _isOn;
    public bool? IsOn
    {
        get => _isOn;
        set { if (_isOn != value) { _isOn = value; OnProp(); OnProp(nameof(Display)); } }
    }

    private bool _isMissing;
    public bool IsMissing
    {
        get => _isMissing;
        set { if (_isMissing != value) { _isMissing = value; OnProp(); OnProp(nameof(Display)); } }
    }

    public string? _alias;
    public string? Alias
    {
        get => _alias;
        set { if (_alias != value) { _alias = value; OnProp(); } }
    }


    public string Display
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Name) ? (Kind ?? "Device") : Name;
            if (IsMissing) label = "❌ " + label;

            string state = "–";
            if (IsOn is bool b)
            {
                state = (Kind?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) ?? false)
                    ? (b ? "ACTIVE" : "INACTIVE")
                    : (b ? "ON" : "OFF");
            }
            return $"{label}  (#{EntityId}) [{state}]";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}