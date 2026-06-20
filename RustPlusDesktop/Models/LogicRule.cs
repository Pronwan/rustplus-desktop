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

        private string _triggerType = "SmartAlarm"; // SmartAlarm, SmartSwitch, ChatCommand
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
        private string _stepType = "Wait"; // Wait, Toggle, CheckAvailability
        public string StepType
        {
            get => _stepType;
            set { _stepType = value; OnProp(); }
        }

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
