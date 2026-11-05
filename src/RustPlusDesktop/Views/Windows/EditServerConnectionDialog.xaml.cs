using System;
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

public partial class EditServerConnectionDialog : FluentWindow
{
    private readonly ServerProfile _profile;
    private readonly MainViewModel _vm;

    public bool HasChanged { get; private set; }

    public EditServerConnectionDialog(ServerProfile profile, MainViewModel vm)
    {
        InitializeComponent();
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));

        TxtName.Text = _profile.Name;
        TxtHost.Text = _profile.Host;
        TxtPort.Text = _profile.Port.ToString(CultureInfo.InvariantCulture);

        try
        {
            ApplicationThemeManager.Apply(this);
        }
        catch { }
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        var host = (TxtHost.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowInfo("Please enter a valid Host / IP address.", InfoBarSeverity.Warning);
            return;
        }

        if (!int.TryParse(TxtPort.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0 || port > 65535)
        {
            ShowInfo("Please enter a valid Port between 1 and 65535.", InfoBarSeverity.Warning);
            return;
        }

        if (!ulong.TryParse(_profile.SteamId64, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId))
        {
            ulong.TryParse(_vm.SteamId64, NumberStyles.Integer, CultureInfo.InvariantCulture, out steamId);
        }

        if (!int.TryParse(_profile.PlayerToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerToken))
        {
            ShowInfo("Profile is missing a valid Player Token. Please re-pair from Rust.", InfoBarSeverity.Warning);
            return;
        }

        BtnTest.IsEnabled = false;
        TestProgress.Visibility = Visibility.Visible;
        InfoTestResult.IsOpen = false;

        try
        {
            var entityIds = _profile.Devices?
                .Where(d => !d.IsGroup && d.EntityId > 0)
                .Select(d => d.EntityId)
                .ToList();

            var result = await Task.Run(() => RustPlusConnectionVerifier.VerifyServerAndDevicesAsync(
                host,
                port,
                steamId,
                playerToken,
                entityIds,
                _profile.UseFacepunchProxy,
                TimeSpan.FromSeconds(6)));

            if (result.Reachable)
            {
                if (result.DevicesVerified)
                {
                    ShowInfo($"Connection successful! Found and verified existing devices on this server.", InfoBarSeverity.Success);
                }
                else if (entityIds != null && entityIds.Count > 0)
                {
                    ShowInfo($"Connected to server, but the {entityIds.Count} existing devices did not respond. (The map may have wiped).", InfoBarSeverity.Warning);
                }
                else
                {
                    ShowInfo($"Connection successful! Server responded to companion ping.", InfoBarSeverity.Success);
                }
            }
            else
            {
                ShowInfo($"Connection failed: {result.ErrorMessage ?? "Server unreachable or companion port closed."}", InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            ShowInfo($"Test error: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            BtnTest.IsEnabled = true;
            TestProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = (TxtName.Text ?? "").Trim();
        var host = (TxtHost.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowInfo("Host / IP address cannot be empty.", InfoBarSeverity.Warning);
            return;
        }

        if (!int.TryParse(TxtPort.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0 || port > 65535)
        {
            ShowInfo("Please enter a valid Port between 1 and 65535.", InfoBarSeverity.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            _profile.Name = name;
        }

        bool connectionChanged = !_profile.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || _profile.Port != port;

        _profile.Host = host;
        _profile.Port = port;

        _vm.Save();
        HasChanged = connectionChanged;

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
        InfoTestResult.Message = message;
        InfoTestResult.Severity = severity;
        InfoTestResult.IsOpen = true;
    }
}
