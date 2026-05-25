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

    private bool _isGroup;
    public bool IsGroup
    {
        get => _isGroup;
        set { if (_isGroup != value) { _isGroup = value; OnProp(); OnProp(nameof(HasGroupSwitches)); } }
    }

    private System.Collections.ObjectModel.ObservableCollection<SmartDevice> _children = new();
    public System.Collections.ObjectModel.ObservableCollection<SmartDevice> Children
    {
        get => _children;
        set 
        { 
            if (_children != value) 
            { 
                if (_children != null) _children.CollectionChanged -= Children_CollectionChanged;
                _children = value; 
                if (_children != null) _children.CollectionChanged += Children_CollectionChanged;
                OnProp(); 
                OnProp(nameof(HasGroupSwitches)); 
            } 
        }
    }

    private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnProp(nameof(HasGroupSwitches));
    }



    [JsonIgnore]
    public bool HasGroupSwitches
    {
        get
        {
            if (!IsGroup || Children == null) return false;
            foreach (var child in Children)
            {
                if (string.Equals(child.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(child.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase) ||
                    child.HasGroupSwitches)
                {
                    return true;
                }
            }
            return false;
        }
    }


   // public int? UpkeepSeconds => Storage?.UpkeepSeconds;

    [JsonIgnore]
    public string StorageSummary
    {
        get
        {
            if (Storage == null) return "–";
            if (Storage.IsToolCupboard)
            {
                var secs = UpkeepSeconds ?? 0;
                if (secs <= 0) return "Upkeep: 0s";

                int days = secs / 86400;
                int rem = secs % 86400;
                int hours = rem / 3600;
                rem = rem % 3600;
                int mins = rem / 60;
                int secsLeft = rem % 60;

                var parts = new System.Collections.Generic.List<string>();
                if (days > 0) parts.Add($"{days}d");
                if (hours > 0) parts.Add($"{hours}h");
                if (mins > 0) parts.Add($"{mins}m");
                if (parts.Count == 0 && secsLeft > 0) parts.Add($"{secsLeft}s");
                if (parts.Count == 0) parts.Add("0s");

                return string.Join(" ", parts);
            }
            
            var count = ItemsCount;
            return count == 1 ? "1 item" : $"{count} items";
        }
    }


    private string? _name;
    public string? Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); } }
    }

    private string? _kind;
    public string? Kind
    {
        get => _kind;
        set { if (_kind != value) { _kind = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); } }
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
                OnProp(nameof(StorageSummary));  

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
        OnProp(nameof(StorageSummary));
    }
    // bequeme Proxy-Properties für’s Binding (OneWay):
    public int ItemsCount => Storage?.ItemsCount ?? 0;     // nutzt deine ItemsCount aus StorageSnapshot
    public int? UpkeepSeconds
{
    get
    {
        if (Storage?.UpkeepSeconds is not int baseSecs)
            return null;

        var elapsed = (int)Math.Max(0,
            (DateTime.UtcNow - Storage.SnapshotUtc).TotalSeconds);

        var remain = baseSecs - elapsed;
        if (remain < 0) remain = 0;
        return remain;
    }
}
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
        set { if (_isMissing != value) { _isMissing = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); } }
    }

    private bool _isToggleBusy;
    [JsonIgnore]
    public bool IsToggleBusy
    {
        get => _isToggleBusy;
        set { if (_isToggleBusy != value) { _isToggleBusy = value; OnProp(); } }
    }

    public string? _alias;
    public string? Alias
    {
        get => _alias;
        set { if (_alias != value) { _alias = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); } }
    }

    private bool _popupEnabled = true;
    public bool PopupEnabled
    {
        get => _popupEnabled;
        set { if (_popupEnabled != value) { _popupEnabled = value; OnProp(); } }
    }

    private bool _audioEnabled = true;
    public bool AudioEnabled
    {
        get => _audioEnabled;
        set { if (_audioEnabled != value) { _audioEnabled = value; OnProp(); } }
    }

    private bool _audioLoopEnabled = false;
    public bool AudioLoopEnabled
    {
        get => _audioLoopEnabled;
        set { if (_audioLoopEnabled != value) { _audioLoopEnabled = value; OnProp(); } }
    }

    private bool _overlayEnabled = true;
    public bool OverlayEnabled
    {
        get => _overlayEnabled;
        set { if (_overlayEnabled != value) { _overlayEnabled = value; OnProp(); } }
    }

    private string? _audioFilePath;
    public string? AudioFilePath
    {
        get => _audioFilePath;
        set { if (_audioFilePath != value) { _audioFilePath = value; OnProp(); } }
    }

    private string? _lastAlarmMessage;
    public string? LastAlarmMessage
    {
        get => _lastAlarmMessage;
        set { if (_lastAlarmMessage != value) { _lastAlarmMessage = value; OnProp(); } }
    }


    public string PureName
    {
        get => string.IsNullOrWhiteSpace(Alias) ? (string.IsNullOrWhiteSpace(Name) ? (Kind ?? "Device") : Name) : Alias;
    }

    public string DisplayName
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Alias) ? (string.IsNullOrWhiteSpace(Name) ? (Kind ?? "Device") : Name) : Alias;
            if (IsMissing) label = "❌ " + label;
            return label;
        }
    }

    public string Display
    {
        get
        {
            string state = "–";
            if (IsOn is bool b)
            {
                state = (Kind?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) ?? false)
                    ? (b ? "ACTIVE" : "INACTIVE")
                    : (b ? "ON" : "OFF");
            }
            return $"{DisplayName}  (#{EntityId}) [{state}]";
        }
    }

    public void NotifyUpkeepChanged()
    {
        OnProp(nameof(UpkeepSeconds));
        OnProp(nameof(StorageSummary));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}