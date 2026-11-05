using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RustPlusDesk.Services
{
    public class LogicEngineRuntimeService : INotifyPropertyChanged
    {
        private static readonly LogicEngineRuntimeService _instance = new LogicEngineRuntimeService();
        public static LogicEngineRuntimeService Instance => _instance;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _currentRuleName;
        public string? CurrentRuleName
        {
            get => _currentRuleName;
            set
            {
                if (_currentRuleName != value)
                {
                    _currentRuleName = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentStepNumber;
        public int CurrentStepNumber
        {
            get => _currentStepNumber;
            set
            {
                if (_currentStepNumber != value)
                {
                    _currentStepNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _currentStepType;
        public string? CurrentStepType
        {
            get => _currentStepType;
            set
            {
                if (_currentStepType != value)
                {
                    _currentStepType = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<string> PendingRules { get; } = new ObservableCollection<string>();

        private CancellationTokenSource? _currentCancellation;
        public CancellationTokenSource? CurrentCancellation
        {
            get => _currentCancellation;
            set
            {
                if (_currentCancellation != value)
                {
                    _currentCancellation = value;
                    OnPropertyChanged();
                }
            }
        }

        public void RequestStop()
        {
            try
            {
                _currentCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; nothing to cancel.
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
