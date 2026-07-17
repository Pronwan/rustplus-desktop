using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models;

public sealed class DeviceAutomationRule : INotifyPropertyChanged
{
    private string _name = "New Automation";
    private bool _isEnabled;
    private bool _isExpanded = true;
    private string _conditionType = "PlayerProximity";
    private string _playerMatchMode = "AnyOnline";
    private ulong _specificPlayerSteamId;
    private uint _locationEntityId;
    private double _distanceMeters = 250;
    private string _startTime = "20:00";
    private string _endTime = "08:00";
    private uint _targetEntityId;
    private bool _matchedState;
    private bool _unmatchedState = true;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get => _name; set => Set(ref _name, value); }
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public string ConditionType { get => _conditionType; set => Set(ref _conditionType, value); }
    public string PlayerMatchMode { get => _playerMatchMode; set => Set(ref _playerMatchMode, value); }
    public ulong SpecificPlayerSteamId { get => _specificPlayerSteamId; set => Set(ref _specificPlayerSteamId, value); }
    public uint LocationEntityId { get => _locationEntityId; set => Set(ref _locationEntityId, value); }
    public double DistanceMeters { get => _distanceMeters; set => Set(ref _distanceMeters, Math.Max(1, value)); }
    public string StartTime { get => _startTime; set => Set(ref _startTime, value); }
    public string EndTime { get => _endTime; set => Set(ref _endTime, value); }
    public uint TargetEntityId { get => _targetEntityId; set => Set(ref _targetEntityId, value); }
    public bool MatchedState { get => _matchedState; set => Set(ref _matchedState, value); }
    public bool UnmatchedState { get => _unmatchedState; set => Set(ref _unmatchedState, value); }

    [JsonIgnore] public bool? LastAppliedState { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
