using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models;

public class SmartDevice : INotifyPropertyChanged
{


    private uint _entityId;
    public uint EntityId
    {
        get => _entityId;
        set { if (_entityId != value) { _entityId = value; OnProp(); OnProp(nameof(Display)); } }
    }

   // public int? UpkeepSeconds => Storage?.UpkeepSeconds;

    public string UpkeepText => HumanizeUpkeep(Storage?.UpkeepSeconds);

    //public int ItemsCount => Storage?.Items?.Count ?? 0;

    // Humanizer (lokal – oder in Utils-Klasse auslagern)
    private static string HumanizeUpkeep(int? secs)
    {
        if (secs is null) return "–";
        var s = secs.Value;
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m";
        if (s < 86400) return $"{s / 3600}h";
        return $"{s / 86400}d";
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

    private StorageSnapshot? _storage;
    [JsonIgnore]
    public StorageSnapshot? Storage
    {
        get => _storage;
        set
        {
            if (!ReferenceEquals(_storage, value))
            {
                // ggf. alten Handler lösen
                if (_storage != null) _storage.Items.CollectionChanged -= StorageItemsChanged;

                _storage = value;
                OnProp(nameof(Storage));
                OnProp(nameof(HasStorage));
                OnProp(nameof(ItemsCount));      // Proxy: nützlich für XAML
                OnProp(nameof(UpkeepSeconds));   // Proxy: nützlich für XAML

                if (_storage != null)
                {
                    // wenn sich die Items-Sammlung ändert → Count im UI aktualisieren
                    _storage.Items.CollectionChanged += StorageItemsChanged;
                }
            }
        }
    }

    private void StorageItemsChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnProp(nameof(ItemsCount));
    }
    // bequeme Proxy-Properties für’s Binding (OneWay):
    public int ItemsCount => Storage?.ItemsCount ?? 0;     // nutzt deine ItemsCount aus StorageSnapshot
    public int? UpkeepSeconds => Storage?.UpkeepSeconds;
    public bool HasStorage => Storage != null;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnProp(nameof(IsExpanded)); } }
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