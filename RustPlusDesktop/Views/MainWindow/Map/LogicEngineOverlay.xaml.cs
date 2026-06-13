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
            
            var newRule = new LogicRule
            {
                Name = $"Rule {(_vm.Selected.LogicRules.Count + 1)}",
                IsEnabled = false,
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

            _vm.Selected.LogicRules.Remove(rule);
            RefreshListBindings();
            _vm.Save();
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
