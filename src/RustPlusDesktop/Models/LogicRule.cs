using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models
{
    public class LogicRule : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        public string Id
        {
            get => _id;
            set { _id = value; OnProp(); }
        }

        private string _name = "New Rule";
        public string Name
        {
            get => _name;
            set { _name = value; OnProp(); }
        }

        private int? _customIconId;
        public int? CustomIconId
        {
            get => _customIconId;
            set { _customIconId = value; OnProp(); OnProp(nameof(CustomIcon)); }
        }

        private string? _customIconShortName;
        public string? CustomIconShortName
        {
            get => _customIconShortName;
            set { _customIconShortName = value; OnProp(); OnProp(nameof(CustomIcon)); }
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

        private bool _isEnabled = false;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnProp(); }
        }

        private bool _isLoopEnabled;
        public bool IsLoopEnabled
        {
            get => _isLoopEnabled;
            set { _isLoopEnabled = value; OnProp(); }
        }

        private int _loopCount = 1;
        public int LoopCount
        {
            get => _loopCount;
            set { _loopCount = Math.Max(0, value); OnProp(); }
        }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnProp(); }
        }

        private bool _isConfirmingDelete = false;
        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            set { _isConfirmingDelete = value; OnProp(); }
        }

        private string _triggerType = "SmartAlarm"; // SmartAlarm, SmartSwitch, ChatCommand, RuleTriggered, RuleCompleted
        public string TriggerType
        {
            get => _triggerType;
            set { _triggerType = value; OnProp(); }
        }

        private uint _triggerEntityId;
        public uint TriggerEntityId
        {
            get => _triggerEntityId;
            set { _triggerEntityId = value; OnProp(); }
        }

        private string _triggerCommand = "rulecommand";
        public string TriggerCommand
        {
            get => _triggerCommand;
            set { _triggerCommand = value; OnProp(); }
        }

        private string _triggerRuleId = "";
        public string TriggerRuleId
        {
            get => _triggerRuleId;
            set { _triggerRuleId = value; OnProp(); }
        }

        private bool _triggerState = true;
        public bool TriggerState
        {
            get => _triggerState;
            set { _triggerState = value; OnProp(); }
        }

        private string _conditionOperator = "NONE"; // NONE, AND, OR
        public string ConditionOperator
        {
            get => _conditionOperator;
            set { _conditionOperator = value; OnProp(); }
        }

        private uint _conditionDeviceEntityId;
        public uint ConditionDeviceEntityId
        {
            get => _conditionDeviceEntityId;
            set { _conditionDeviceEntityId = value; OnProp(); }
        }

        private bool _conditionDeviceState = true;
        public bool ConditionDeviceState
        {
            get => _conditionDeviceState;
            set { _conditionDeviceState = value; OnProp(); }
        }

        private ObservableCollection<LogicStep> _steps = new();
        public ObservableCollection<LogicStep> Steps
        {
            get => _steps;
            set { _steps = value; OnProp(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnProp([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class LogicStep : INotifyPropertyChanged
    {
        private string _stepType = "Wait"; // Wait, Toggle, CheckAvailability, StartTimer
        public string StepType
        {
            get => _stepType;
            set { _stepType = value; OnProp(); }
        }

        // ---- StartTimer -------------------------------------------------------------
        // Lets a rule start a countdown when it fires. The intended use is an RF receiver
        // tuned to an oil rig frequency wired to a Smart Alarm: Rust announces the hack,
        // the alarm fires, and the timer stands in for the Chinook tracking that the API
        // no longer supports.

        private int _timerMinutes = 15;
        public int TimerMinutes
        {
            get => _timerMinutes;
            // A timer of zero or less would fire its own expiry immediately.
            set { _timerMinutes = Math.Max(1, value); OnProp(); }
        }

        /// <summary>Custom, SmallOilRig or LargeOilRig.</summary>
        private string _timerTarget = "Custom";
        public string TimerTarget
        {
            get => _timerTarget;
            set { _timerTarget = value; OnProp(); OnProp(nameof(IsOilRigTimer)); }
        }

        /// <summary>Only used when <see cref="TimerTarget"/> is Custom.</summary>
        private string _timerName = "";
        public string TimerName
        {
            get => _timerName;
            set { _timerName = value; OnProp(); }
        }

        /// <summary>
        /// Draws the locked crate on the rig with its countdown. Off still leaves the chat
        /// alerts and the chat command intact — those follow from picking a rig, not from
        /// wanting the marker.
        /// </summary>
        private bool _showCrateOnMap = true;
        public bool ShowCrateOnMap
        {
            get => _showCrateOnMap;
            set { _showCrateOnMap = value; OnProp(); }
        }

        /// <summary>
        /// The text this alarm sends, as set on the alarm in-game.
        ///
        /// Push notifications carry no entity ID. While connected the app recovers one from
        /// the WebSocket event that arrives alongside, but a push from a server the app is not
        /// connected to — or one still queued from before launch — has nothing to match on but
        /// its text. Typing it here closes that gap immediately; leaving it empty is fine, as
        /// the app overwrites this with the real text the first time it sees this alarm
        /// identified by ID. What is learned that way is proven correct, so it wins over
        /// anything typed.
        ///
        /// A text left at Rust's default is refused for matching, because every unrenamed
        /// alarm would share it and real raid alerts would be swallowed.
        /// </summary>
        private string _alarmTextHint = "";
        public string AlarmTextHint
        {
            get => _alarmTextHint;
            set { _alarmTextHint = value ?? ""; OnProp(); }
        }

        [JsonIgnore]
        public bool IsOilRigTimer =>
            _timerTarget == "SmallOilRig" || _timerTarget == "LargeOilRig";

        /// <summary>The name MonumentWatcher keys its events on, or null for a plain timer.</summary>
        [JsonIgnore]
        public string? OilRigName => _timerTarget switch
        {
            "SmallOilRig" => "Small Oil Rig",
            "LargeOilRig" => "Large Oil Rig",
            _ => null,
        };

        private int _waitSeconds = 10;
        public int WaitSeconds
        {
            get => _waitSeconds;
            set { _waitSeconds = value; OnProp(); }
        }

        private uint _targetEntityId;
        public uint TargetEntityId
        {
            get => _targetEntityId;
            set { _targetEntityId = value; OnProp(); }
        }

        private string _targetGroupName = "";
        public string TargetGroupName
        {
            get => _targetGroupName;
            set { _targetGroupName = value; OnProp(); }
        }

        private bool? _toggleState; // null = invert, true = ON, false = OFF
        public bool? ToggleState
        {
            get => _toggleState;
            set { _toggleState = value; OnProp(); }
        }

        private string _conditionOperator = "ALL_OFFLINE"; // ALL_OFFLINE, ANY_OFFLINE, ALL_ONLINE, ANY_ONLINE
        public string ConditionOperator
        {
            get => _conditionOperator;
            set { _conditionOperator = value; OnProp(); }
        }

        private string _conditionDeviceIdsCsv = "";
        public string ConditionDeviceIdsCsv
        {
            get => _conditionDeviceIdsCsv;
            set { _conditionDeviceIdsCsv = value; OnProp(); }
        }

        private ObservableCollection<LogicStep> _conditionalSteps = new();
        public ObservableCollection<LogicStep> ConditionalSteps
        {
            get => _conditionalSteps;
            set { _conditionalSteps = value; OnProp(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnProp([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
