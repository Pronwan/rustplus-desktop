using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models;

public class SmartDevice : INotifyPropertyChanged
{


    /// <summary>
    /// "Small Oil" or "Large Oil" while a Logic Engine rule uses this alarm as an oil rig
    /// trigger, otherwise null.
    ///
    /// Derived state, never persisted: the rules are the truth, and a badge left behind in
    /// the save file would outlive the rule that earned it. Recomputed whenever rules change,
    /// so pulling the device out of the Logic Engine restores normal alarm behaviour by
    /// itself.
    /// </summary>
    [JsonIgnore]
    private string? _oilRigBadge;

    [JsonIgnore]
    public string? OilRigBadge
    {
        get => _oilRigBadge;
        set { if (_oilRigBadge != value) { _oilRigBadge = value; OnProp(); OnProp(nameof(HasOilRigBadge)); } }
    }

    [JsonIgnore]
    public bool HasOilRigBadge => !string.IsNullOrEmpty(_oilRigBadge);

    /// <summary>
    /// "SmallOilRig" or "LargeOilRig" while a rule uses this alarm as a rig trigger.
    ///
    /// The badge above is a translated label and useless to anything outside the UI. This is
    /// the stable key, and it is the one that gets synced: the cloud worker has to know that a
    /// firing alarm is a crate hack rather than a raid, and it can only learn that from here.
    /// Derived from the rules like the badge, never persisted locally.
    /// </summary>
    [JsonIgnore]
    private string? _oilRigTriggerTarget;

    [JsonIgnore]
    public string? OilRigTriggerTarget
    {
        get => _oilRigTriggerTarget;
        set { if (_oilRigTriggerTarget != value) { _oilRigTriggerTarget = value; OnProp(); } }
    }

    /// <summary>
    /// The upper line of the alarm's message as set in-game — the text Rust puts in the push
    /// notification title.
    ///
    /// The only thing that identifies which alarm fired: an alarm push carries server details
    /// and nothing else, no entity id and no entity name. Pairing is the mirror image, giving
    /// the id with a generic "Smart Alarm" as its name. Neither is usable alone, so this is
    /// learned when both arrive together — the WebSocket event names the entity while the push
    /// carries the title.
    ///
    /// Persisted and synced to the cloud, because the worker that drives Alexa only ever sees
    /// the push and has no other way to tell two alarms apart. Editable by hand for anyone who
    /// would rather not trigger every alarm once to teach it.
    /// </summary>
    private string? _inGameAlarmTitle;
    public string? InGameAlarmTitle
    {
        get => _inGameAlarmTitle;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_inGameAlarmTitle != trimmed) { _inGameAlarmTitle = trimmed; OnProp(); }
        }
    }

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

    public DateTime LastPolledAt { get; set; } = DateTime.MinValue;

    private double? _pairedX;
    public double? PairedX
    {
        get => _pairedX;
        set { if (_pairedX != value) { _pairedX = value; OnProp(); OnProp(nameof(AutomationDisplayName)); } }
    }

    private double? _pairedY;
    public double? PairedY
    {
        get => _pairedY;
        set { if (_pairedY != value) { _pairedY = value; OnProp(); OnProp(nameof(AutomationDisplayName)); } }
    }

    public ulong? PairedBySteamId { get; set; }
    public DateTime? PairedLocationCapturedAt { get; set; }

    [JsonIgnore]
    public string AutomationDisplayName => PairedX.HasValue && PairedY.HasValue
        ? $"{DisplayName}  ({PairedX:0}, {PairedY:0})"
        : $"{DisplayName}  (location unavailable)";

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
        set { if (_name != value) { _name = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); OnProp(nameof(AutomationDisplayName)); } }
    }

    private string? _kind;
    public string? Kind
    {
        get => _kind;
        set { if (_kind != value) { _kind = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); OnProp(nameof(AutomationDisplayName)); } }
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
        set { if (_isMissing != value) { _isMissing = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); OnProp(nameof(AutomationDisplayName)); } }
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
        set { if (_alias != value) { _alias = value; OnProp(); OnProp(nameof(PureName)); OnProp(nameof(DisplayName)); OnProp(nameof(AutomationDisplayName)); } }
    }

    // Off by default. The alarm window steals Windows focus, and during an actual raid that
    // is the worst possible moment to have the game pulled out from under you. The in-app
    // overlay and the sound still fire; this is for people who explicitly want the interruption.
    private bool _popupEnabled = false;
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

    private int? _customIconId;
    public int? CustomIconId
    {
        get => _customIconId;
        set { if (_customIconId != value) { _customIconId = value; OnProp(); OnProp(nameof(CustomIcon)); } }
    }

    private string? _customIconShortName;
    public string? CustomIconShortName
    {
        get => _customIconShortName;
        set { if (_customIconShortName != value) { _customIconShortName = value; OnProp(); OnProp(nameof(CustomIcon)); } }
    }

    [JsonIgnore]
    public System.Windows.Media.ImageSource? CustomIcon
    {
        get
        {
            if (CustomIconId.HasValue && CustomIconId.Value != 0)
            {
                return RustPlusDesk.Views.MainWindow.ResolveItemIcon(CustomIconId.Value, CustomIconShortName);
            }
            return null;
        }
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

    private bool _isEditing;
    [JsonIgnore]
    public bool IsEditing
    {
        get => _isEditing;
        set { if (_isEditing != value) { _isEditing = value; OnProp(); } }
    }

    private bool _isEditingSwitchCommand;
    [JsonIgnore]
    public bool IsEditingSwitchCommand
    {
        get => _isEditingSwitchCommand;
        set { if (_isEditingSwitchCommand != value) { _isEditingSwitchCommand = value; OnProp(); } }
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
