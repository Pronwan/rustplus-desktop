using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Models;
using RustPlusDesk.ViewModels;

namespace RustPlusDesk.Views;

public partial class DeviceAutomationOverlay : UserControl
{
    public MainWindow? ParentWindow { get; set; }
    private MainViewModel? _vm;

    public DeviceAutomationOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _vm = ParentWindow?.DataContext as MainViewModel;
            DataContext = _vm;
            RefreshListBindings();
        };
    }

    public void RefreshListBindings()
    {
        if (_vm?.Selected == null) return;
        _vm.Selected.DeviceAutomationRules ??= new List<DeviceAutomationRule>();
        RulesItemsControl.ItemsSource = null;
        RulesItemsControl.ItemsSource = _vm.Selected.DeviceAutomationRules;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        _vm?.Save();
    }

    private void AutomationActive_Changed(object sender, RoutedEventArgs e) => _vm?.Save();

    private void BtnAddRule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Selected == null) return;
        _vm.Selected.DeviceAutomationRules.Add(new DeviceAutomationRule
        {
            Name = $"Automation {_vm.Selected.DeviceAutomationRules.Count + 1}"
        });
        RefreshListBindings();
        _vm.Save();
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Selected == null || sender is not FrameworkElement { Tag: DeviceAutomationRule rule }) return;
        _vm.Selected.DeviceAutomationRules.Remove(rule);
        RefreshListBindings();
        _vm.Save();
    }
}
