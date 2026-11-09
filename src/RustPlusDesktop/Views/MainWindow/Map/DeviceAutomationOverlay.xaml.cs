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

        // Edits saved only on close with the X, so Cloud 24/7 (which follows every save) ran an
        // automation as it was before. Leaving a field saves, and so does closing by any route.
        AddHandler(LostKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((_, _) => SaveSoon()), true);
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) SaveSoon(); };
    }

    /// <summary>After the binding has written the field's value back, not before.</summary>
    private void SaveSoon()
        => Dispatcher.BeginInvoke(new System.Action(() => _vm?.Save()), System.Windows.Threading.DispatcherPriority.Background);

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
        Ach.Unlock(Ach.Automation);
        _vm.Save();
        RefreshListBindings();
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Selected == null || sender is not FrameworkElement { Tag: DeviceAutomationRule rule }) return;
        _vm.Selected.DeviceAutomationRules.Remove(rule);
        RefreshListBindings();
        _vm.Save();
    }

    private void BtnToggleRuleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DeviceAutomationRule rule })
            rule.IsExpanded = !rule.IsExpanded;
    }
}
