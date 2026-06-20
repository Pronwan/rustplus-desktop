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
            
            // Re-bind items source
            RulesItemsControl.ItemsSource = null;
            RulesItemsControl.ItemsSource = _vm.Selected.LogicRules;
        }

        private void BtnCloseLogicEngine_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            _vm?.Save();
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
