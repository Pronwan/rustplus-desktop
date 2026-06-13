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
    private bool _teamFeatureMasterSyncPending;
    private bool _chatFeaturesBlockedByMaster;
    private bool _isChatFeatureMaster;
    private string _chatFeatureMasterName = "";
    private string? _lastKnownTeamFeatureMasterId;
    private string? _lastKnownPremiumSponsorId;
    private string? _lastMasterOfferKey;
    private string? _declinedMasterTeamKey;
    private DateTime _declinedMasterUntilUtc;
    private string? _lastTeamFeatureMasterSyncSignature;
    private bool _playMasterOfferSound = TrackingService.ChatMasterOfferSoundEnabled;
    private SoundPlayer? _masterOfferSoundPlayer;
    private System.Windows.Threading.DispatcherTimer? _teamFeatureMasterWatchTimer;
    private bool _teamFeatureMasterWatchBusy;
    private bool _teamFeatureShutdownSent;
    private string? _lastTeamFeatureDisconnectReleaseSignature;
    private DateTime _lastHeartbeatTime = DateTime.MinValue;
    private bool? _lastWantsAlerts;
    private bool? _lastWantsCommands;

    private static string TeamFeatureText(string key, string fallback)
        => Properties.Resources.ResourceManager.GetString(key) ?? fallback;

    private bool ChatFeaturesBlockedByMaster => _chatFeaturesBlockedByMaster;

    private void ResetTeamFeatureMasterSyncState()
    {
        _lastTeamFeatureMasterSyncSignature = null;
    }

    private void RequestTeamFeatureMasterSync()
    {
        ResetTeamFeatureMasterSyncState();
        _ = SyncTeamFeatureMasterAsync();
    }

    private bool ShouldSyncTeamFeatureMasterForCurrentState(string cloudPresenceSignature)
    {
        if (_vm?.Selected == null || TeamMembers.Count == 0) return false;

        var teamKey = BuildTeamFeatureKey();
        if (string.IsNullOrWhiteSpace(teamKey)) return false;

        var declined = IsMasterOfferTemporarilyDeclined(teamKey);
        var signature = string.Join("#",
            cloudPresenceSignature,
            teamKey,
            GetMyTeamOrderIndex().ToString(System.Globalization.CultureInfo.InvariantCulture),
            TrackingService.AnnounceSpawnsMaster ? "alerts:1" : "alerts:0",
            _vm.Selected.ChatCommandsEnabled ? "commands:1" : "commands:0",
            declined ? "declined:1" : "declined:0");

        if (signature == _lastTeamFeatureMasterSyncSignature)
            return false;

        _lastTeamFeatureMasterSyncSignature = signature;
        return true;
    }

    private async Task SyncTeamFeatureMasterAsync()
    {
        if (_teamFeatureMasterBusy)
        {
            _teamFeatureMasterSyncPending = true;
            return;
        }

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
            if (wantsAlerts || wantsCommands)
            {
                _lastTeamFeatureDisconnectReleaseSignature = null;
            }

            var orderIndex = GetMyTeamOrderIndex();

            TeamFeatureMasterState? state;
            if (SupabaseAuthManager.IsDiscordAuthenticated || SupabaseAuthManager.IsEmailAuthenticated)
            {
                var isCriticalChange = wantsAlerts != _lastWantsAlerts || wantsCommands != _lastWantsCommands || (!wantsAlerts && !wantsCommands);
                var timeSinceLast = DateTime.UtcNow - _lastHeartbeatTime;

                if (!isCriticalChange && timeSinceLast.TotalSeconds < 15)
                {
                    return; // Skip heartbeat to save server bandwidth
                }

                state = await SupabaseAuthManager.HeartbeatTeamFeaturePresenceAsync(
                    mySteamId,
                    myName,
                    serverKey,
                    _vm.Selected?.Name ?? "",
                    teamKey,
                    orderIndex,
                    wantsAlerts,
                    wantsCommands);

                _lastHeartbeatTime = DateTime.UtcNow;
                _lastWantsAlerts = wantsAlerts;
                _lastWantsCommands = wantsCommands;
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
            if (_teamFeatureMasterSyncPending)
            {
                _teamFeatureMasterSyncPending = false;
                _ = SyncTeamFeatureMasterAsync();
            }
        }
    }

    private async Task RefreshTeamFeatureMasterStateAsync()
    {
        if (_vm?.Selected == null || TeamMembers.Count == 0) return;

        var serverKey = GetServerKey();
        if (string.IsNullOrWhiteSpace(serverKey)) return;

        var teamKey = BuildTeamFeatureKey();
        if (string.IsNullOrWhiteSpace(teamKey)) return;

        var state = await SupabaseAuthManager.GetTeamFeatureMasterStateAsync(serverKey, teamKey);
        await Dispatcher.InvokeAsync(() => ApplyTeamFeatureMasterState(state, teamKey));
    }

    private void UpdateTeamFeatureMasterWatch()
    {
        if (TeamMembers.Count <= 1 || _isChatFeatureMaster || TeamSyncWebSocketService.IsActive)
        {
            StopTeamFeatureMasterWatch();
            return;
        }

        if (_chatFeaturesBlockedByMaster || HasLocalChatFeatureIntent())
        {
            int intervalSeconds;
            if (SupabaseAuthManager.IsPremium)
            {
                intervalSeconds = 15;
            }
            else if (!string.IsNullOrEmpty(_lastKnownPremiumSponsorId))
            {
                intervalSeconds = 60;
            }
            else
            {
                intervalSeconds = 300;
            }

            if (_teamFeatureMasterWatchTimer != null)
            {
                if (_teamFeatureMasterWatchTimer.Interval.TotalSeconds != intervalSeconds)
                {
                    _teamFeatureMasterWatchTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
                }
                return;
            }

            _teamFeatureMasterWatchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(intervalSeconds)
            };
            _teamFeatureMasterWatchTimer.Tick += TeamFeatureMasterWatchTimer_Tick;
            _teamFeatureMasterWatchTimer.Start();
        }
        else
        {
            StopTeamFeatureMasterWatch();
        }
    }

    private void StopTeamFeatureMasterWatch()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(StopTeamFeatureMasterWatch);
            return;
        }
        var timer = _teamFeatureMasterWatchTimer;
        if (timer == null) return;

        timer.Tick -= TeamFeatureMasterWatchTimer_Tick;
        timer.Stop();
        _teamFeatureMasterWatchTimer = null;
        _teamFeatureMasterWatchBusy = false;
    }

    private async void TeamFeatureMasterWatchTimer_Tick(object? sender, EventArgs e)
    {
        if (_teamFeatureMasterWatchBusy) return;

        _teamFeatureMasterWatchBusy = true;
        try
        {
            await RefreshTeamFeatureMasterStateAsync();
        }
        finally
        {
            _teamFeatureMasterWatchBusy = false;
        }
    }

    private async Task NotifyTeamFeatureAppClosingAsync()
    {
        if (_teamFeatureShutdownSent) return;
        _teamFeatureShutdownSent = true;

        try
        {
            StopTeamFeatureMasterWatch();
            DiscordBotListenerService.Instance.StopListening();
            await SupabaseAuthManager.MarkAppOfflineAsync();

            if (_vm?.Selected == null || TeamMembers.Count == 0) return;
            if (!SupabaseAuthManager.IsDiscordAuthenticated && !SupabaseAuthManager.IsEmailAuthenticated) return;

            var serverKey = GetServerKey();
            var teamKey = BuildTeamFeatureKey();
            if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(teamKey)) return;

            var mySteamId = _mySteamId.ToString();
            var myName = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId)?.Name
                ?? _vm.Selected?.Name
                ?? mySteamId;

            await SupabaseAuthManager.HeartbeatTeamFeaturePresenceAsync(
                mySteamId,
                myName,
                serverKey,
                _vm.Selected?.Name ?? "",
                teamKey,
                GetMyTeamOrderIndex(),
                wantsChatAlerts: false,
                wantsChatCommands: false);
        }
        catch
        {
            // Best effort on shutdown.
        }
    }

    private void NotifyTeamFeatureServerDisconnected()
    {
        _ = NotifyTeamFeatureServerDisconnectedAsync();
    }

    private async Task NotifyTeamFeatureServerDisconnectedAsync()
    {
        try
        {
            StopTeamFeatureMasterWatch();

            if (_vm?.Selected == null || TeamMembers.Count == 0) return;
            if (!SupabaseAuthManager.IsDiscordAuthenticated && !SupabaseAuthManager.IsEmailAuthenticated) return;

            var serverKey = GetServerKey();
            var teamKey = BuildTeamFeatureKey();
            if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(teamKey)) return;

            var mySteamId = _mySteamId.ToString();
            var releaseSignature = $"{serverKey}#{teamKey}#{mySteamId}";
            if (_lastTeamFeatureDisconnectReleaseSignature == releaseSignature) return;
            _lastTeamFeatureDisconnectReleaseSignature = releaseSignature;

            var myName = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId)?.Name
                ?? _vm.Selected?.Name
                ?? mySteamId;

            await SupabaseAuthManager.HeartbeatTeamFeaturePresenceAsync(
                mySteamId,
                myName,
                serverKey,
                _vm.Selected?.Name ?? "",
                teamKey,
                GetMyTeamOrderIndex(),
                wantsChatAlerts: false,
                wantsChatCommands: false);
        }
        catch
        {
            // Best effort on server disconnect.
        }
    }

    public string BuildTeamFeatureKey()
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

    public void ApplyTeamFeatureMasterState(TeamFeatureMasterState? state, string teamKey)
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
        _lastKnownPremiumSponsorId = state?.PremiumSponsorSteamId;
        ApplyChatFeatureMasterUiState();
        UpdateTeamFeatureMasterWatch();

        if (previousBlocked && !_chatFeaturesBlockedByMaster && HasLocalChatFeatureIntent())
        {
            RequestTeamFeatureMasterSync();
        }

        // Update Discord Bot Listener subscription state
        var teamSteamIds = TeamMembers.Select(tm => tm.SteamId.ToString()).ToList();
        _ = DiscordBotListenerService.Instance.UpdateSubscriptionStateAsync(_isChatFeatureMaster, teamSteamIds);

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

    private bool HasLocalChatFeatureIntent()
    {
        return TrackingService.AnnounceSpawnsMaster || (_vm.Selected?.ChatCommandsEnabled ?? false);
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
            Timeout = TimeSpan.FromSeconds(10),
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
        var countdownText = new TextBlock
        {
            Text = string.Format(TeamFeatureText("ChatFeatureMasterAutoCloseCountdown", "Closes in {0}s"), 10),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.72,
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(countdownText, Dock.Left);
        footer.Children.Add(countdownText);

        var soundToggle = new CheckBox
        {
            IsChecked = _playMasterOfferSound,
            Content = "\uD83D\uDD0A",
            ToolTip = TeamFeatureText("AudioAlert", "Audio Alert"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        soundToggle.Checked += (_, _) =>
        {
            _playMasterOfferSound = true;
            TrackingService.ChatMasterOfferSoundEnabled = true;
        };
        soundToggle.Unchecked += (_, _) =>
        {
            _playMasterOfferSound = false;
            TrackingService.ChatMasterOfferSoundEnabled = false;
        };
        DockPanel.SetDock(soundToggle, Dock.Left);
        footer.Children.Add(soundToggle);

        var remainingSeconds = 10;
        var countdownTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        countdownTimer.Tick += (_, _) =>
        {
            remainingSeconds--;
            if (remainingSeconds <= 0)
            {
                countdownTimer.Stop();
                return;
            }

            countdownText.Text = string.Format(
                TeamFeatureText("ChatFeatureMasterAutoCloseCountdown", "Closes in {0}s"),
                remainingSeconds);
        };
        countdownTimer.Start();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
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
            countdownTimer.Stop();
            snackbar.Visibility = Visibility.Collapsed;
            _ = SyncTeamFeatureMasterAsync();
        };

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
