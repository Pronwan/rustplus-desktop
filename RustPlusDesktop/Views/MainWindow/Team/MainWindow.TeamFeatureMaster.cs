using System;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Auth;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _teamFeatureMasterBusy;
    private bool _chatFeaturesBlockedByMaster;
    private bool _isChatFeatureMaster;
    private string _chatFeatureMasterName = "";
    private string? _lastKnownTeamFeatureMasterId;
    private string? _lastMasterOfferKey;
    private string? _declinedMasterTeamKey;
    private DateTime _declinedMasterUntilUtc;
    private bool _playMasterOfferSound = true;
    private SoundPlayer? _masterOfferSoundPlayer;

    private static string TeamFeatureText(string key, string fallback)
        => Properties.Resources.ResourceManager.GetString(key) ?? fallback;

    private bool ChatFeaturesBlockedByMaster => _chatFeaturesBlockedByMaster;

    private async Task SyncTeamFeatureMasterAsync()
    {
        if (_teamFeatureMasterBusy) return;
        if (_vm?.Selected == null || TeamMembers.Count == 0) return;

        var serverKey = GetServerKey();
        if (string.IsNullOrWhiteSpace(serverKey)) return;

        var teamKey = BuildTeamFeatureKey();
        if (string.IsNullOrWhiteSpace(teamKey)) return;

        _teamFeatureMasterBusy = true;
        try
        {
            var mySteamId = _mySteamId.ToString();
            var myName = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId)?.Name
                ?? _vm.Selected?.Name
                ?? mySteamId;

            var wantsAlerts = TrackingService.AnnounceSpawnsMaster;
            var wantsCommands = _vm.Selected?.ChatCommandsEnabled ?? false;
            if (IsMasterOfferTemporarilyDeclined(teamKey))
            {
                wantsAlerts = false;
                wantsCommands = false;
            }

            var orderIndex = GetMyTeamOrderIndex();

            TeamFeatureMasterState? state;
            if (SupabaseAuthManager.IsDiscordAuthenticated || SupabaseAuthManager.IsEmailAuthenticated)
            {
                state = await SupabaseAuthManager.HeartbeatTeamFeaturePresenceAsync(
                    mySteamId,
                    myName,
                    serverKey,
                    _vm.Selected?.Name ?? "",
                    teamKey,
                    orderIndex,
                    wantsAlerts,
                    wantsCommands);
            }
            else
            {
                state = await SupabaseAuthManager.GetTeamFeatureMasterStateAsync(serverKey, teamKey);
            }

            await Dispatcher.InvokeAsync(() => ApplyTeamFeatureMasterState(state, teamKey));
        }
        finally
        {
            _teamFeatureMasterBusy = false;
        }
    }

    private string BuildTeamFeatureKey()
    {
        var ids = TeamMembers
            .Select(t => t.SteamId)
            .Where(id => id != 0)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => id.ToString())
            .ToArray();

        return ids.Length == 0 ? "" : string.Join("|", ids);
    }

    private int GetMyTeamOrderIndex()
    {
        for (int i = 0; i < TeamMembers.Count; i++)
        {
            if (TeamMembers[i].SteamId == _mySteamId)
                return i;
        }

        return 999;
    }

    private bool IsMasterOfferTemporarilyDeclined(string teamKey)
    {
        return _declinedMasterTeamKey == teamKey
            && _declinedMasterUntilUtc > DateTime.UtcNow;
    }

    private void ApplyTeamFeatureMasterState(TeamFeatureMasterState? state, string teamKey)
    {
        var previousBlocked = _chatFeaturesBlockedByMaster;
        var previousIsMaster = _isChatFeatureMaster;
        var mySteamId = _mySteamId.ToString();
        var hasActiveMaster = state != null
            && !string.IsNullOrWhiteSpace(state.MasterSteamId)
            && (!state.ExpiresAt.HasValue || state.ExpiresAt.Value.ToUniversalTime() > DateTime.UtcNow);

        _isChatFeatureMaster = hasActiveMaster && state!.MasterSteamId == mySteamId;
        _chatFeaturesBlockedByMaster = hasActiveMaster && !_isChatFeatureMaster;
        _chatFeatureMasterName = hasActiveMaster
            ? (string.IsNullOrWhiteSpace(state!.MasterName) ? state.MasterSteamId ?? "" : state.MasterName)
            : "";

        var currentMasterId = hasActiveMaster ? state!.MasterSteamId : null;
        if (_isChatFeatureMaster && (!previousIsMaster || _lastKnownTeamFeatureMasterId != currentMasterId))
        {
            var offerKey = $"{teamKey}:{state!.ElectedAt?.ToUniversalTime():O}";
            ShowChatMasterOffer(offerKey);
        }

        _lastKnownTeamFeatureMasterId = currentMasterId;
        ApplyChatFeatureMasterUiState();

        if (_chatFeaturesBlockedByMaster && !previousBlocked)
        {
            ShowInfoSnackbar(
                TeamFeatureText("ChatFeatureMasterOnlineTitle", "Chat Master online"),
                string.Format(
                    TeamFeatureText("ChatFeatureMasterBlockedMessage", "{0} is controlling Chat Alerts and Chat Commands for this team."),
                    _chatFeatureMasterName),
                WpfUi.ControlAppearance.Caution);
        }
    }

    private void ApplyChatFeatureMasterUiState()
    {
        var blocked = _chatFeaturesBlockedByMaster;
        var message = blocked
            ? string.Format(
                TeamFeatureText("ChatFeatureMasterBlockedShort", "Chat Master {0} online. Chat Alerts and Chat Commands are paused on this device."),
                _chatFeatureMasterName)
            : "";

        if (ChatAnnounce != null)
        {
            ChatAnnounce.IsEnabled = !blocked;
            ChatAnnounce.ToolTip = blocked ? message : FindResource("RightClickConfigure");
        }

        if (ChatAlertsConfigureButton != null)
        {
            ChatAlertsConfigureButton.IsEnabled = !blocked;
            ChatAlertsConfigureButton.ToolTip = blocked ? message : FindResource("Configure");
        }

        if (BtnOpenChatCommands != null)
            BtnOpenChatCommands.ToolTip = blocked ? message : FindResource("ChatCommandsSettings");

        if (ChatFeatureMasterWarningBadge != null)
            ChatFeatureMasterWarningBadge.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;

        if (ChatFeatureMasterWarningText != null)
            ChatFeatureMasterWarningText.Text = blocked
                ? TeamFeatureText("ChatFeatureMasterOnlineTitle", "Chat Master online")
                : "";

        ChatCommandsOverlay?.SetMasterBlocked(blocked, message);
    }

    private bool CanSendAutomatedTeamChat()
    {
        if (!_chatFeaturesBlockedByMaster) return true;

        AppendLog($"[ChatMaster] Automated team chat blocked by master: {_chatFeatureMasterName}");
        return false;
    }

    private bool CanProcessLocalChatCommands(bool isPromoteCommand = false)
    {
        return isPromoteCommand || !_chatFeaturesBlockedByMaster;
    }

    private void ShowChatMasterOffer(string offerKey)
    {
        if (_lastMasterOfferKey == offerKey) return;
        _lastMasterOfferKey = offerKey;

        if (_playMasterOfferSound)
            PlayChatMasterSound();

        if (RootSnackbar == null) return;

        var snackbar = new WpfUi.Snackbar(RootSnackbar)
        {
            Title = TeamFeatureText("ChatFeatureMasterAssignedTitle", "You are Chat Master"),
            Appearance = WpfUi.ControlAppearance.Success,
            Icon = new WpfUi.SymbolIcon(WpfUi.SymbolRegular.Info24),
            Timeout = TimeSpan.FromSeconds(20),
            MaxWidth = 380,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock
        {
            Text = TeamFeatureText("ChatFeatureMasterAssignedMessage", "This device is now controlling Chat Alerts and Chat Commands for your team."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var footer = new DockPanel { LastChildFill = false };
        var soundToggle = new CheckBox
        {
            IsChecked = _playMasterOfferSound,
            Content = "\uD83D\uDD0A",
            ToolTip = TeamFeatureText("AudioAlert", "Audio Alert"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        soundToggle.Checked += (_, _) => _playMasterOfferSound = true;
        soundToggle.Unchecked += (_, _) => _playMasterOfferSound = false;
        DockPanel.SetDock(soundToggle, Dock.Left);
        footer.Children.Add(soundToggle);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new WpfUi.Button
        {
            Content = TeamFeatureText("ChatFeatureMasterAccept", "Accept"),
            Appearance = WpfUi.ControlAppearance.Primary,
            Margin = new Thickness(0, 0, 8, 0)
        };
        accept.Click += (_, _) => snackbar.Visibility = Visibility.Collapsed;

        var deny = new WpfUi.Button
        {
            Content = TeamFeatureText("ChatFeatureMasterDeny", "Deny"),
            Appearance = WpfUi.ControlAppearance.Secondary
        };
        deny.Click += (_, _) =>
        {
            _declinedMasterTeamKey = BuildTeamFeatureKey();
            _declinedMasterUntilUtc = DateTime.UtcNow.AddMinutes(5);
            _isChatFeatureMaster = false;
            ApplyChatFeatureMasterUiState();
            snackbar.Visibility = Visibility.Collapsed;
            _ = SyncTeamFeatureMasterAsync();
        };

        buttons.Children.Add(accept);
        buttons.Children.Add(deny);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        stack.Children.Add(footer);
        snackbar.Content = stack;
        snackbar.Show();
    }

    private void PlayChatMasterSound()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icq-message.wav"));
            if (resource != null)
            {
                _masterOfferSoundPlayer = new SoundPlayer(resource.Stream);
                _masterOfferSoundPlayer.Load();
                _masterOfferSoundPlayer.Play();
                return;
            }

            var baseDir = AppContext.BaseDirectory;
            var path = System.IO.Path.Combine(baseDir, "Assets", "icq-message.wav");
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(baseDir, "icq-message.wav");
            if (!System.IO.File.Exists(path))
                return;

            _masterOfferSoundPlayer ??= new SoundPlayer(path);
            if (_masterOfferSoundPlayer.SoundLocation != path)
            {
                _masterOfferSoundPlayer.SoundLocation = path;
                _masterOfferSoundPlayer.LoadAsync();
            }
            _masterOfferSoundPlayer.Play();
        }
        catch
        {
            // Sound is nice-to-have only.
        }
    }
}
