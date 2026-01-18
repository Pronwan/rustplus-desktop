using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RustPlusDesk.Models
{
    public sealed class DeviceImportItem : INotifyPropertyChanged
    {
        public ulong OwnerSteamId { get; init; }
        public string OwnerName { get; init; } = "";
        public uint EntityId { get; init; }
        public string? Kind { get; init; }
        public string? Name { get; init; }
        public string? Alias { get; init; }
        public bool AlreadyPresent { get; init; }

        // neu:
        public string ServerName { get; init; } = "";   // Name des aktuellen Server-Profils

        // Hauptanzeige: Alias > Name > #ID
        public string DisplayName
        {
            get
            {
                // Label = Alias > Name
                var label = !string.IsNullOrWhiteSpace(Alias) ? Alias! :
                            !string.IsNullOrWhiteSpace(Name) ? Name! :
                            null;

                // Wenn Alias/Name vorhanden → "Garage (#123456)"
                if (!string.IsNullOrWhiteSpace(label))
                    return $"{label} (#{EntityId})";

                // Sonst nur "#123456"
                return $"#{EntityId}";
            }
        }

        // kleine Zusatzzeile unterhalb (z.B. Owner usw.)
        public string Tagline =>
            AlreadyPresent
                ? $"{OwnerName} (already in your list)"
                : OwnerName;

        // Status-Text rechts
        public string ExtraInfo => ExistsState switch
        {
            "ok" => "Reachable in current map",
            "missing" => "Missing in current map",
            "err" => "Status unknown",
            "local" => "Already present",
            _ => ""
        };
        // NEU: wird für das Binding der Checkbox verwendet
        public bool IsSelectable => !AlreadyPresent;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnProp(); } }
        }

        private string _existsState = "?";
        public string ExistsState
        {
            get => _existsState;
            set { if (_existsState != value) { _existsState = value; OnProp(); OnProp(nameof(ExtraInfo)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnProp([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}