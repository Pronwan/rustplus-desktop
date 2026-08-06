using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Models;
using RustPlusDesk.ViewModels;

namespace RustPlusDesk.Views
{
    public partial class LogicEngineOverlay : UserControl
    {
        public MainWindow? ParentWindow { get; set; }
        private MainViewModel? _vm;

        public LogicEngineOverlay()
        {
            InitializeComponent();
            Loaded += LogicEngineOverlay_Loaded;
        }

        private void LogicEngineOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            _vm = ParentWindow?.DataContext as MainViewModel;
            DataContext = _vm;
            RefreshListBindings();
        }

        public void RefreshListBindings()
        {
            if (_vm?.Selected == null) return;
            _vm.Selected.LogicRules ??= new List<LogicRule>();

            FillKnownAlarmTexts();

            // Re-bind items source
            RulesItemsControl.ItemsSource = null;
            RulesItemsControl.ItemsSource = _vm.Selected.LogicRules;
        }

        /// <summary>
        /// Fills blank alarm texts from the trigger device, for rules whose step became an oil
        /// rig timer after the device was already chosen — the selection handler cannot see
        /// that, since nothing about the device changed.
        ///
        /// Only blanks: a value already there was either typed deliberately or learned from a
        /// real notification, and neither should be overwritten behind the user's back.
        /// </summary>
        private void FillKnownAlarmTexts()
        {
            var profile = _vm?.Selected;
            if (profile?.LogicRules == null || profile.Devices == null) return;

            bool changed = false;
            foreach (var rule in profile.LogicRules)
            {
                if (rule.TriggerType != "SmartAlarm" || rule.TriggerEntityId == 0) continue;

                var device = FindDevice(profile.Devices, rule.TriggerEntityId);
                if (string.IsNullOrWhiteSpace(device?.InGameAlarmTitle)) continue;

                foreach (var step in EnumerateSteps(rule))
                {
                    if (step.StepType != "StartTimer" || !step.IsOilRigTimer) continue;
                    if (!string.IsNullOrWhiteSpace(step.AlarmTextHint)) continue;

                    step.AlarmTextHint = device!.InGameAlarmTitle!;
                    changed = true;
                }
            }

            if (changed) _vm!.Save();
        }

        private static SmartDevice? FindDevice(IEnumerable<SmartDevice>? devices, uint entityId)
        {
            if (devices == null) return null;

            foreach (var device in devices)
            {
                if (device.EntityId == entityId) return device;

                var child = FindDevice(device.Children, entityId);
                if (child != null) return child;
            }

            return null;
        }

        private void BtnCloseLogicEngine_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            _vm?.Save();

            // Adding or removing an oil rig timer changes what the crate alerts and the crate
            // countdown command can promise. Re-ask here so the menus match the rules the user
            // just wrote, rather than only after the next connect.
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.RefreshOilRigTimerCapability();
                mw.ApplyEventCapabilitiesToMenus();
            }
        }

        private void BtnStopLogicEngine_Click(object sender, RoutedEventArgs e)
        {
            ParentWindow?.StopLogicEngineExecution();
        }

        private void ToggleEngineActive_StateChanged(object sender, RoutedEventArgs e)
        {
            if (_vm?.Selected != null)
            {
                _vm.Save();
            }
        }

        /// <summary>
        /// Fills the alarm text in from the device the moment one is picked.
        ///
        /// The app already records what each alarm says in-game, learned the first time it
        /// fires while the app is running. Making the user type it a second time here would be
        /// asking for something we already know — and a typo would then silently target the
        /// wrong alarm.
        ///
        /// Overwrites rather than only filling blanks: the field describes the selected alarm,
        /// so switching the trigger to a different device makes whatever stood there wrong.
        /// The stored value is learned from the real notification and outranks anything typed.
        /// </summary>
        private void CmbTriggerDeviceAlarm_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo || combo.DataContext is not LogicRule rule) return;
            if (combo.SelectedItem is not SmartDevice device) return;

            string? title = device.InGameAlarmTitle;
            if (string.IsNullOrWhiteSpace(title)) return;

            bool changed = false;
            foreach (var step in EnumerateSteps(rule))
            {
                if (step.StepType != "StartTimer" || !step.IsOilRigTimer) continue;
                if (string.Equals(step.AlarmTextHint, title, StringComparison.Ordinal)) continue;

                step.AlarmTextHint = title;
                changed = true;
            }

            if (changed) _vm?.Save();
        }

        /// <summary>Steps including those nested in conditional branches.</summary>
        private static IEnumerable<LogicStep> EnumerateSteps(LogicRule rule)
        {
            foreach (var step in rule.Steps)
            {
                yield return step;
                foreach (var nested in step.ConditionalSteps)
                    yield return nested;
            }
        }

        private void BtnAddOilRigRule_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.Selected == null) return;

            // Collapse existing rules so the new one is the focus
            foreach (var rule in _vm.Selected.LogicRules)
            {
                rule.IsExpanded = false;
            }

            var newRule = new LogicRule
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Large/Small Oil Rig Chat/Timer ",
                CustomIconId = -1768880890,
                CustomIconShortName = "fish.smallshark",
                IsEnabled = true,
                IsLoopEnabled = false,
                LoopCount = 1,
                IsExpanded = true,
                IsConfirmingDelete = false,
                TriggerType = "SmartAlarm",
                TriggerEntityId = 0,
                TriggerCommand = "rulecommand",
                TriggerRuleId = "",
                TriggerState = true,
                ConditionOperator = "NONE",
                ConditionDeviceEntityId = 0,
                ConditionDeviceState = true,
                Steps = new ObservableCollection<LogicStep>
                {
                    new LogicStep
                    {
                        StepType = "StartTimer",
                        TimerMinutes = 15,
                        TimerTarget = "LargeOilRig",
                        TimerName = "",
                        ShowCrateOnMap = true,
                        AlarmTextHint = "",
                        WaitSeconds = 10,
                        TargetEntityId = 0,
                        TargetGroupName = "",
                        ToggleState = null,
                        ConditionOperator = "ALL_OFFLINE",
                        ConditionDeviceIdsCsv = "",
                        ConditionalSteps = new ObservableCollection<LogicStep>()
                    }
                }
            };

            _vm.Selected.LogicRules.Add(newRule);
            RefreshListBindings();
            _vm.Save();
        }

        private void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.Selected == null) return;

            // Collapse existing rules so the new one is the focus
            foreach (var rule in _vm.Selected.LogicRules)
            {
                rule.IsExpanded = false;
            }

            var newRule = new LogicRule
            {
                Name = $"Rule {(_vm.Selected.LogicRules.Count + 1)}",
                IsEnabled = false,
                IsExpanded = true,
                TriggerType = "SmartAlarm",
                Steps = new ObservableCollection<LogicStep>()
            };

            _vm.Selected.LogicRules.Add(newRule);
            RefreshListBindings();
            _vm.Save();
        }

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.Selected == null || sender is not FrameworkElement el || el.Tag is not LogicRule rule) return;

            // Clear any other rule's delete confirmation so only one is active
            foreach (var r in _vm.Selected.LogicRules)
            {
                if (r != rule) r.IsConfirmingDelete = false;
            }
            rule.IsConfirmingDelete = true;
        }

        private void BtnConfirmDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.Selected == null || sender is not FrameworkElement el || el.Tag is not LogicRule rule) return;

            _vm.Selected.LogicRules.Remove(rule);
            RefreshListBindings();
            _vm.Save();
        }

        private void BtnCancelDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicRule rule) return;
            rule.IsConfirmingDelete = false;
        }

        private void BtnToggleRuleExpand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicRule rule) return;
            rule.IsExpanded = !rule.IsExpanded;
        }

        private void BtnChangeRuleIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicRule rule) return;

            var dlg = new RustPlusDesk.Views.Windows.ChangeDeviceIconDialog(rule.CustomIconId, rule.CustomIconShortName, "Rule")
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();

            if (!dlg.IsSaved) return;

            if (dlg.IsResetClicked || !dlg.SelectedIconId.HasValue)
            {
                rule.CustomIconId = null;
                rule.CustomIconShortName = null;
            }
            else
            {
                rule.CustomIconId = dlg.SelectedIconId;
                rule.CustomIconShortName = dlg.SelectedIconShortName;
            }
            _vm?.Save();
        }

        private void BtnAddStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicRule rule) return;

            rule.Steps ??= new ObservableCollection<LogicStep>();
            rule.Steps.Add(new LogicStep
            {
                StepType = "Wait",
                WaitSeconds = 10
            });
            _vm?.Save();
        }

        private void BtnDeleteStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicStep step) return;

            // Find rule that contains this step
            if (_vm?.Selected != null)
            {
                foreach (var r in _vm.Selected.LogicRules)
                {
                    if (r.Steps.Contains(step))
                    {
                        r.Steps.Remove(step);
                        break;
                    }
                }
                _vm.Save();
            }
        }

        private void BtnAddNestedStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicStep step) return;

            step.ConditionalSteps ??= new ObservableCollection<LogicStep>();
            step.ConditionalSteps.Add(new LogicStep
            {
                StepType = "Toggle",
                ToggleState = true
            });
            _vm?.Save();
        }

        private void BtnDeleteNestedStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement el || el.Tag is not LogicStep step) return;

            if (_vm?.Selected != null)
            {
                foreach (var r in _vm.Selected.LogicRules)
                {
                    foreach (var s in r.Steps)
                    {
                        if (s.ConditionalSteps.Contains(step))
                        {
                            s.ConditionalSteps.Remove(step);
                            _vm.Save();
                            return;
                        }
                    }
                }
            }
        }
    }
}
