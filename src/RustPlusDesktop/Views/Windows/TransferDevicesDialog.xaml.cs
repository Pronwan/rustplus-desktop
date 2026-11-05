using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows;

public partial class TransferDevicesDialog : FluentWindow
{
    private readonly ServerProfile _source;
    private readonly MainViewModel _vm;

    public bool HasTransferred { get; private set; }

    public TransferDevicesDialog(ServerProfile source, MainViewModel vm)
    {
        InitializeComponent();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));

        TxtSourceName.Text = _source.Name;
        int devCount = _source.Devices?.Count ?? 0;
        int camCount = _source.CameraIds?.Count ?? 0;
        int ruleCount = (_source.LogicRules?.Count ?? 0) + (_source.DeviceAutomationRules?.Count ?? 0);
        TxtSourceDetails.Text = $"{_source.Host}:{_source.Port} • {devCount} devices, {camCount} cameras, {ruleCount} rules";

        var targets = _vm.Servers.Where(s => s != _source).ToList();
        CmbTargetServers.ItemsSource = targets;

        // Smart pre-selection: pick a server on the same Host or with matching name
        var candidate = targets.FirstOrDefault(s => s.Host.Equals(_source.Host, StringComparison.OrdinalIgnoreCase))
                     ?? targets.FirstOrDefault(s => s.Name.Equals(_source.Name, StringComparison.OrdinalIgnoreCase))
                     ?? targets.FirstOrDefault();
        if (candidate != null)
        {
            CmbTargetServers.SelectedItem = candidate;
        }

        try
        {
            ApplicationThemeManager.Apply(this);
        }
        catch { }
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTargetServers.SelectedItem is not ServerProfile target)
        {
            ShowInfo("Please select a target server to transfer items to.", InfoBarSeverity.Warning);
            return;
        }

        bool copyDevices = ChkDevices.IsChecked == true;
        bool copyCameras = ChkCameras.IsChecked == true;
        bool copyRules = ChkRules.IsChecked == true;
        bool copyCommands = ChkCommands.IsChecked == true;
        bool verifyFirst = ChkVerify.IsChecked == true;
        bool deleteSource = ChkDeleteSource.IsChecked == true;

        if (!copyDevices && !copyCameras && !copyRules && !copyCommands)
        {
            ShowInfo("Please select at least one item type to transfer.", InfoBarSeverity.Warning);
            return;
        }

        // Verification step if requested
        if (verifyFirst && copyDevices && _source.Devices != null && _source.Devices.Any(d => !d.IsGroup && d.EntityId > 0))
        {
            PnlProgress.Visibility = Visibility.Visible;
            TxtProgressMessage.Text = $"Verifying devices on {target.Host}:{target.Port}...";
            BtnTransfer.IsEnabled = false;
            InfoResult.IsOpen = false;

            try
            {
                if (!ulong.TryParse(target.SteamId64, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId))
                {
                    ulong.TryParse(_vm.SteamId64, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
                }

                if (int.TryParse(target.PlayerToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var token))
                {
                    var entityIds = _source.Devices
                        .Where(d => !d.IsGroup && d.EntityId > 0)
                        .Select(d => d.EntityId)
                        .ToList();

                    var probeResult = await Task.Run(() => RustPlusConnectionVerifier.VerifyServerAndDevicesAsync(
                        target.Host,
                        target.Port,
                        steamId,
                        token,
                        entityIds,
                        target.UseFacepunchProxy,
                        TimeSpan.FromSeconds(6)));

                    if (probeResult.Reachable)
                    {
                        if (!probeResult.DevicesVerified && entityIds.Count > 0)
                        {
                            var confirm = System.Windows.MessageBox.Show(
                                $"The target server is online, but none of the {_source.Devices.Count} devices responded. (The server map may have wiped or has different entity IDs).\n\nDo you still want to transfer these devices and settings to '{target.Name}'?",
                                "Device Verification Notice",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Warning);

                            if (confirm != System.Windows.MessageBoxResult.Yes)
                            {
                                PnlProgress.Visibility = Visibility.Collapsed;
                                BtnTransfer.IsEnabled = true;
                                return;
                            }
                        }
                    }
                    else
                    {
                        var confirm = System.Windows.MessageBox.Show(
                            $"Target server ({target.Host}:{target.Port}) could not be reached ({probeResult.ErrorMessage}).\n\nDo you want to proceed with transferring devices and settings anyway?",
                            "Target Server Unreachable",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);

                        if (confirm != System.Windows.MessageBoxResult.Yes)
                        {
                            PnlProgress.Visibility = Visibility.Collapsed;
                            BtnTransfer.IsEnabled = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Probe error during transfer: {ex.Message}");
            }
            finally
            {
                PnlProgress.Visibility = Visibility.Collapsed;
                BtnTransfer.IsEnabled = true;
            }
        }

        // Perform actual transfer/merge
        int devicesTransferred = 0;
        int camerasTransferred = 0;
        int rulesTransferred = 0;

        // 1. Devices
        if (copyDevices && _source.Devices != null)
        {
            target.Devices ??= new ObservableCollection<SmartDevice>();
            var existingIds = new HashSet<uint>(target.Devices.Where(d => !d.IsGroup).Select(d => d.EntityId));

            foreach (var dev in _source.Devices)
            {
                if (!dev.IsGroup && !existingIds.Contains(dev.EntityId))
                {
                    target.Devices.Add(new SmartDevice
                    {
                        EntityId = dev.EntityId,
                        Name = dev.Name,
                        Kind = dev.Kind,
                        IsOn = dev.IsOn,
                        IsGroup = dev.IsGroup,
                        InGameAlarmTitle = dev.InGameAlarmTitle,
                        PairedX = dev.PairedX,
                        PairedY = dev.PairedY,
                        PairedBySteamId = dev.PairedBySteamId,
                        PairedLocationCapturedAt = dev.PairedLocationCapturedAt
                    });
                    existingIds.Add(dev.EntityId);
                    devicesTransferred++;
                }
            }
        }

        // 2. Cameras
        if (copyCameras)
        {
            target.CameraIds ??= new ObservableCollection<string>();
            target.CameraNames ??= new Dictionary<string, string>();

            if (_source.CameraIds != null)
            {
                foreach (var cam in _source.CameraIds)
                {
                    if (!target.CameraIds.Contains(cam, StringComparer.OrdinalIgnoreCase))
                    {
                        target.CameraIds.Add(cam);
                        camerasTransferred++;
                    }
                }
            }

            if (_source.CameraNames != null)
            {
                foreach (var kvp in _source.CameraNames)
                {
                    target.CameraNames[kvp.Key] = kvp.Value;
                }
            }
        }

        // 3. Logic & Automation Rules
        if (copyRules)
        {
            if (_source.LogicRules != null)
            {
                target.LogicRules ??= new List<LogicRule>();
                var existingRuleIds = new HashSet<string>(target.LogicRules.Select(r => r.Id));
                foreach (var r in _source.LogicRules)
                {
                    if (!existingRuleIds.Contains(r.Id))
                    {
                        target.LogicRules.Add(r);
                        rulesTransferred++;
                    }
                }
            }

            if (_source.DeviceAutomationRules != null)
            {
                target.DeviceAutomationRules ??= new List<DeviceAutomationRule>();
                var existingAutoIds = new HashSet<string>(target.DeviceAutomationRules.Select(r => r.Id));
                foreach (var r in _source.DeviceAutomationRules)
                {
                    if (!existingAutoIds.Contains(r.Id))
                    {
                        target.DeviceAutomationRules.Add(r);
                        rulesTransferred++;
                    }
                }
            }
        }

        // 4. Commands & Base Codes
        if (copyCommands)
        {
            if (_source.BaseCodes != null)
            {
                target.BaseCodes ??= new ObservableCollection<BaseCode>();
                var existingLabels = new HashSet<string>(target.BaseCodes.Select(b => b.Label ?? ""), StringComparer.OrdinalIgnoreCase);
                foreach (var code in _source.BaseCodes)
                {
                    if (!existingLabels.Contains(code.Label ?? ""))
                    {
                        target.BaseCodes.Add(new BaseCode
                        {
                            Label = code.Label ?? "",
                            Code = code.Code
                        });
                    }
                }
            }

            if (_source.SwitchCommandMappings != null)
            {
                target.SwitchCommandMappings ??= new ObservableCollection<ChatCommandMapping>();
                var existingCmds = new HashSet<string>(target.SwitchCommandMappings.Select(m => m.Command ?? ""), StringComparer.OrdinalIgnoreCase);
                foreach (var scm in _source.SwitchCommandMappings)
                {
                    if (!existingCmds.Contains(scm.Command ?? ""))
                    {
                        target.SwitchCommandMappings.Add(new ChatCommandMapping
                        {
                            Label = scm.Label ?? "",
                            Command = scm.Command ?? "",
                            EntityId = scm.EntityId
                        });
                    }
                }
            }
        }

        if (deleteSource)
        {
            _vm.Servers.Remove(_source);
            var source = _source;
            var remaining = _vm.Servers.ToList();
            _ = Task.Run(() => RustPlusDesk.Services.Data.ServerLocalDataCleanup.DeleteFor(source, remaining));
        }

        _vm.Save();
        HasTransferred = true;

        MainWindow.ShowInfoSnackbarOnMain(
            "Transfer Complete",
            $"Transferred {devicesTransferred} devices, {camerasTransferred} cameras, and {rulesTransferred} rules to '{target.Name}'.",
            ControlAppearance.Success);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        InfoResult.Message = message;
        InfoResult.Severity = severity;
        InfoResult.IsOpen = true;
    }
}
