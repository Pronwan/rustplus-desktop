using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models;

/// <summary>
/// One editable value inside a console command, e.g. the item in "craft.add {item} {amount}".
/// The type decides which editor the panel shows, so new parameter kinds only need a template.
/// </summary>
public sealed class ConsoleCommandParam : INotifyPropertyChanged
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("default")] public string Default { get; set; } = "";
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }

    private string? _value;

    /// <summary>Current value; falls back to the shipped default until the user changes it.</summary>
    [JsonIgnore]
    public string Value
    {
        get => _value ?? Default;
        set
        {
            var v = value ?? "";
            if (Value == v) return;
            _value = v;
            OnProp();
            OnProp(nameof(IsItem));
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [JsonIgnore] public bool IsItem => string.Equals(Type, "item", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsBool => string.Equals(Type, "bool", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsPlainText => !IsItem && !IsBool;

    /// <summary>
    /// Checkbox view of a bool parameter. Rust writes these as the literal words true/false, so
    /// the value stays a string and only the presentation is boolean - no converter needed, and
    /// nothing to keep in sync.
    /// </summary>
    [JsonIgnore]
    public bool BoolValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase) || Value == "1";
        set
        {
            Value = value ? "true" : "false";
            OnProp();
        }
    }

    /// <summary>True when the value still matches what shipped, so the UI can offer a reset.</summary>
    [JsonIgnore] public bool IsDefault => string.Equals(Value, Default, StringComparison.Ordinal);

    public event EventHandler? ValueChanged;

    public void ResetToDefault() => Value = Default;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// A console command as shipped in console-commands.json, plus whatever the user has changed:
/// parameter values and the key it should be bound to. Both are persisted separately from the
/// catalogue, so shipping new commands never overwrites someone's binds.
/// </summary>
public sealed class ConsoleCommandDef : INotifyPropertyChanged
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "client";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("featured")] public bool Featured { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("bindable")] public bool Bindable { get; set; }
    [JsonPropertyName("defaultBind")] public string? DefaultBind { get; set; }
    [JsonPropertyName("params")] public List<ConsoleCommandParam> Params { get; set; } = new();

    private string? _bindKey;

    /// <summary>The key this command is bound to, in Rust's own notation (kp1, mouse3, f5 ...).</summary>
    [JsonIgnore]
    public string BindKey
    {
        get => _bindKey ?? DefaultBind ?? "";
        set
        {
            var v = (value ?? "").Trim();
            if (BindKey == v) return;
            _bindKey = v;
            OnProp();
            OnProp(nameof(HasBind));
            OnProp(nameof(BindLine));
            OnProp(nameof(BindKeyDisplay));
        }
    }

    [JsonIgnore] public bool HasBind => !string.IsNullOrWhiteSpace(BindKey);

    /// <summary>What the key button shows when nothing is bound yet.</summary>
    [JsonIgnore] public string BindKeyDisplay => HasBind ? BindKey : "—";

    /// <summary>The command with every placeholder filled in - this is what gets copied.</summary>
    [JsonIgnore]
    public string ResolvedCommand
    {
        get
        {
            var s = Command;
            foreach (var p in Params)
                s = s.Replace("{" + p.Name + "}", p.Value);
            return s;
        }
    }

    /// <summary>
    /// The ready-made bind line. Quotes are only added when the command contains a separator,
    /// because Rust needs them for chained commands but they look like noise on a simple one.
    /// </summary>
    [JsonIgnore]
    public string BindLine
    {
        get
        {
            var cmd = ResolvedCommand;
            bool needsQuotes = cmd.Contains(';') || cmd.Contains(' ');
            var body = needsQuotes && !cmd.StartsWith("\"") ? $"\"{cmd.Replace("\"", "'")}\"" : cmd;
            return $"bind {(HasBind ? BindKey : "<key>")} {body}";
        }
    }

    [JsonIgnore] public bool HasParams => Params.Count > 0;

    /// <summary>Raised whenever something worth persisting changed.</summary>
    public event EventHandler? UserStateChanged;

    public void AttachParamWatchers()
    {
        foreach (var p in Params)
        {
            p.ValueChanged -= OnParamChanged;
            p.ValueChanged += OnParamChanged;
        }
    }

    private void OnParamChanged(object? sender, EventArgs e)
    {
        OnProp(nameof(ResolvedCommand));
        OnProp(nameof(BindLine));
        UserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetParams()
    {
        foreach (var p in Params) p.ResetToDefault();
    }

    /// <summary>Restores a stored bind and stored parameter values onto a freshly loaded command.</summary>
    public void ApplyUserState(ConsoleCommandUserState state)
    {
        if (state == null) return;
        if (state.Bind != null) BindKey = state.Bind;
        if (state.Values != null)
        {
            foreach (var p in Params)
                if (state.Values.TryGetValue(p.Name, out var v) && v != null)
                    p.Value = v;
        }
    }

    public ConsoleCommandUserState ToUserState() => new()
    {
        Bind = _bindKey,
        Values = Params.Where(p => !p.IsDefault).ToDictionary(p => p.Name, p => p.Value)
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>What we persist per command. Deliberately sparse: only what differs from the catalogue.</summary>
public sealed class ConsoleCommandUserState
{
    [JsonPropertyName("bind")] public string? Bind { get; set; }
    [JsonPropertyName("values")] public Dictionary<string, string>? Values { get; set; }
}

public sealed class ConsoleCommandCatalog
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("commands")] public List<ConsoleCommandDef> Commands { get; set; } = new();
}

/// <summary>A titled block of commands, e.g. "Combat", used to break the long list up.</summary>
public sealed class ConsoleCommandGroup
{
    public string Title { get; init; } = "";
    public ObservableCollection<ConsoleCommandDef> Commands { get; } = new();
}
