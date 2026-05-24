namespace RustPlusDesk.Properties {
    using System;
    
    public class Resources {
        private static global::System.Resources.ResourceManager resourceMan;
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        internal Resources() {
        }
        
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("RustPlusDesk.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        public static global::System.Globalization.CultureInfo Culture {
            get => resourceCulture;
            set => resourceCulture = value;
        }

        private static string GetString(string key)
        {
            var val = ResourceManager.GetString(key, resourceCulture);
            if (string.IsNullOrWhiteSpace(val))
            {
                var neutralVal = ResourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(neutralVal)) return neutralVal;
                return string.IsNullOrWhiteSpace(val) ? key : val;
            }
            return val;
        }

        public static string Identifier => GetString("Identifier");
        public static string FPS => GetString("FPS");
        public static string CameraFPSNote => GetString("CameraFPSNote");
        public static string AppTitle => GetString("AppTitle");
        public static string SteamAccount => GetString("SteamAccount");
        public static string NotLoggedIn => GetString("NotLoggedIn");
        public static string CheckForUpdates => GetString("CheckForUpdates");
        public static string Downloading => GetString("Downloading");
        public static string JoinDiscord => GetString("JoinDiscord");
        public static string PatchNotes => GetString("PatchNotes");
        public static string Settings => GetString("Settings");
        public static string PairedServers => GetString("PairedServers");
        public static string NoServersPaired => GetString("NoServersPaired");
        public static string PairingMaintenance => GetString("PairingMaintenance");
        public static string PairingIdle => GetString("PairingIdle");
        public static string RightClickOptions => GetString("RightClickOptions");
        public static string TrackingActive => GetString("TrackingActive");
        public static string LastPull => GetString("LastPull");
        public static string Donate => GetString("Donate");
        public static string SupportDeveloper => GetString("SupportDeveloper");
        public static string Reset => GetString("Reset");
        public static string ResetWebSocket => GetString("ResetWebSocket");
        public static string Players => GetString("Players");
        public static string ShowOnlinePlayers => GetString("ShowOnlinePlayers");
        public static string Queue => GetString("Queue");
        public static string DevicesTab => GetString("DevicesTab");
        public static string Hotkeys => GetString("Hotkeys");
        public static string ImportDevices => GetString("ImportDevices");
        public static string ExportDevices => GetString("ExportDevices");
        public static string Refresh => GetString("Refresh");
        public static string Delete => GetString("Delete");
        public static string DeleteMissingOnly => GetString("DeleteMissingOnly");
        public static string RenameDevice => GetString("RenameDevice");
        public static string Online => GetString("Online");
        public static string Offline => GetString("Offline");
        public static string Connect => GetString("Connect");
        public static string FullConnect => GetString("FullConnect");
        public static string LoadMapTeam => GetString("LoadMapTeam");
        public static string Disconnect => GetString("Disconnect");
        public static string ServerDetails => GetString("ServerDetails");
        public static string Listen => GetString("Listen");
        public static string Stop => GetString("Stop");
        public static string TryPairingEdge => GetString("TryPairingEdge");
        public static string ResetPairingConfig => GetString("ResetPairingConfig");
        public static string ResetListenPairing => GetString("ResetListenPairing");
        public static string ResetListenEdge => GetString("ResetListenEdge");
        public static string RelinkBattlemetrics => GetString("RelinkBattlemetrics");
        public static string DeleteServer => GetString("DeleteServer");
        public static string PleaseWait => GetString("PleaseWait");
        public static string NoTokenRegistered => GetString("NoTokenRegistered");
        public static string TokenExpired => GetString("TokenExpired");
        public static string ExpiresInDays => GetString("ExpiresInDays");
        public static string UntilNight => GetString("UntilNight");
        public static string UntilDay => GetString("UntilDay");
        public static string FollowMe => GetString("FollowMe");
        public static string FollowPlayer => GetString("FollowPlayer");
        public static string PairingListening => GetString("PairingListening");
        public static string PairingStopped => GetString("PairingStopped");
        public static string PairingFailed => GetString("PairingFailed");
        public static string AlarmActivated => GetString("AlarmActivated");
        public static string Server => GetString("Server");
        public static string SmartAlarm => GetString("SmartAlarm");
        public static string UnableToReadLink => GetString("UnableToReadLink");
        public static string DeletePairingConfigHeader => GetString("DeletePairingConfigHeader");
        public static string DeletePairingConfigBody => GetString("DeletePairingConfigBody");
        public static string ResetListenHeader => GetString("ResetListenHeader");
        public static string ResetListenBody => GetString("ResetListenBody");
        public static string ResetListenEdgeHeader => GetString("ResetListenEdgeHeader");
        public static string ResetListenEdgeBody => GetString("ResetListenEdgeBody");
        public static string UpdateQueryFailed => GetString("UpdateQueryFailed");
        public static string UpdateUpToDate => GetString("UpdateUpToDate");
        public static string UpdateAvailableHeader => GetString("UpdateAvailableHeader");
        public static string UpdateAvailableBody => GetString("UpdateAvailableBody");
        public static string DownloadAndInstallBody => GetString("DownloadAndInstallBody");
        public static string DownloadFailed => GetString("DownloadFailed");
        public static string UpdateCheckFailed => GetString("UpdateCheckFailed");
        public static string NoDevices => GetString("NoDevices");
        public static string ViewProfile => GetString("ViewProfile");
        public static string ChangeGroup => GetString("ChangeGroup");
        public static string Rename => GetString("Rename");
        public static string RemoveTracked => GetString("RemoveTracked");
        public static string View => GetString("View");
        public static string MoreOptions => GetString("MoreOptions");
        public static string RelinkBattlemetricsId => GetString("RelinkBattlemetricsId");
        public static string DeleteServerEllipsis => GetString("DeleteServerEllipsis");
        public static string RightClickRename => GetString("RightClickRename");
        public static string Toggle => GetString("Toggle");
        public static string PopupWindow => GetString("PopupWindow");
        public static string InAppAlert => GetString("InAppAlert");
        public static string AudioAlert => GetString("AudioAlert");
        public static string CustomAudioFile => GetString("CustomAudioFile");
        public static string ResetToDefault => GetString("ResetToDefault");
        public static string Snapshot => GetString("Snapshot");
        public static string ItemsUpkeep => GetString("ItemsUpkeep");
        public static string NoDevicesFound => GetString("NoDevicesFound");
        public static string PairDeviceToSee => GetString("PairDeviceToSee");
        public static string TeamTab => GetString("TeamTab");
        public static string CenterOnMap => GetString("CenterOnMap");
        public static string FollowOnMap => GetString("FollowOnMap");
        public static string OpenSteamProfile => GetString("OpenSteamProfile");
        public static string PromoteToLeader => GetString("PromoteToLeader");
        public static string KickFromTeam => GetString("KickFromTeam");
        public static string ProfileMarkers => GetString("ProfileMarkers");
        public static string DeathMarkers => GetString("DeathMarkers");
        public static string CamerasTab => GetString("CamerasTab");
        public static string AddCamera => GetString("AddCamera");
        public static string NoCamerasFound => GetString("NoCamerasFound");
        public static string PairCameraToSee => GetString("PairCameraToSee");
        public static string PlayersTab => GetString("PlayersTab");
        public static string HowToTrack => GetString("HowToTrack");
        public static string LearnHowTrackingWorks => GetString("LearnHowTrackingWorks");
        public static string OnlineTab => GetString("OnlineTab");
        public static string OnlinePlayers => GetString("OnlinePlayers");
        public static string SearchBM => GetString("SearchBM");
        public static string SearchServerBM => GetString("SearchServerBM");
        public static string Popout => GetString("Popout");
        public static string OpenPlayersSeparateWindow => GetString("OpenPlayersSeparateWindow");
        public static string Track => GetString("Track");
        public static string ManualTrackPlaceholder => GetString("ManualTrackPlaceholder");
        public static string FilterPlayersPlaceholder => GetString("FilterPlayersPlaceholder");
        public static string LoadingPlayers => GetString("LoadingPlayers");
        public static string TrackedTab => GetString("TrackedTab");
        public static string TrackedPlayers => GetString("TrackedPlayers");
        public static string ManageGroups => GetString("ManageGroups");
        public static string ViewFullAnalysisReport => GetString("ViewFullAnalysisReport");
        public static string ReadyToExploreMap => GetString("ReadyToExploreMap");
        public static string ConnectToLoadMap => GetString("ConnectToLoadMap");
        public static string ReadyToDominate => GetString("ReadyToDominate");
        public static string PairAccountBenefits => GetString("PairAccountBenefits");
        public static string LoginPairAccount => GetString("LoginPairAccount");
        public static string RequiresSteamLogin => GetString("RequiresSteamLogin");
        public static string DeleteServerHeader => GetString("DeleteServerHeader");
        public static string DeleteServerConfirm => GetString("DeleteServerConfirm");
        public static string Cancel => GetString("Cancel");
        public static string DeleteAction => GetString("DeleteAction");
        public static string AutoHideAfter3s => GetString("AutoHideAfter3s");
        public static string MapFilters => GetString("MapFilters");
        public static string Grid => GetString("Grid");
        public static string Monuments => GetString("Monuments");
        public static string Shops => GetString("Shops");
        public static string ChatAlerts => GetString("ChatAlerts");
        public static string Alerts => GetString("Alerts");
        public static string Configure => GetString("Configure");
        public static string SelectAll => GetString("SelectAll");
        public static string DeselectAll => GetString("DeselectAll");
        public static string Events => GetString("Events");
        public static string CargoShip => GetString("CargoShip");
        public static string CargoSpawn => GetString("CargoSpawn");
        public static string CargoDock => GetString("CargoDock");
        public static string CargoEgress => GetString("CargoEgress");
        public static string CargoArrival => GetString("CargoArrival");
        public static string Heli => GetString("Heli");
        public static string Chinook => GetString("Chinook");
        public static string Vendor => GetString("Vendor");
        public static string OilRig => GetString("OilRig");
        public static string DeepSea => GetString("DeepSea");
        public static string SmartAlerts => GetString("SmartAlerts");
        public static string PlayerOnline => GetString("PlayerOnline");
        public static string PlayerOffline => GetString("PlayerOffline");
        public static string AnnounceTracking => GetString("AnnounceTracking");
        public static string PlayerDeathSelf => GetString("PlayerDeathSelf");
        public static string PlayerDeathTeam => GetString("PlayerDeathTeam");
        public static string PlayerRespawnSelf => GetString("PlayerRespawnSelf");
        public static string PlayerRespawnTeam => GetString("PlayerRespawnTeam");
        public static string NewShops => GetString("NewShops");
        public static string SuspiciousShops => GetString("SuspiciousShops");
        public static string TradeAlerts => GetString("TradeAlerts");
        public static string CrosshairStyle => GetString("CrosshairStyle");
        public static string GreenDot => GetString("GreenDot");
        public static string MiniGreen => GetString("MiniGreen");
        public static string OpenCrosshairRG => GetString("OpenCrosshairRG");
        public static string ThinRedCircle => GetString("ThinRedCircle");
        public static string SquareDot => GetString("SquareDot");
        public static string MagentaDot => GetString("MagentaDot");
        public static string MagentaOpenCross => GetString("MagentaOpenCross");
        public static string RangeLineTicks => GetString("RangeLineTicks");
        public static string DrawCrosshair => GetString("DrawCrosshair");
        public static string ChooseMonitor => GetString("ChooseMonitor");
        public static string DrawOverlay => GetString("DrawOverlay");
        public static string DrawToolTip => GetString("DrawToolTip");
        public static string PlaceTextToolTip => GetString("PlaceTextToolTip");
        public static string PlaceIconToolTip => GetString("PlaceIconToolTip");
        public static string EraserToolTip => GetString("EraserToolTip");
        public static string DeleteOverlayToolTip => GetString("DeleteOverlayToolTip");
        public static string UploadOverlayToolTip => GetString("UploadOverlayToolTip");
        public static string ProfitTrades => GetString("ProfitTrades");
        public static string ArbitrageOpportunities => GetString("ArbitrageOpportunities");
        public static string BuyXForY => GetString("BuyXForY");
        public static string Analyze => GetString("Analyze");
        public static string IWantToGet => GetString("IWantToGet");
        public static string ICanPayWith => GetString("ICanPayWith");
        public static string TeamChat => GetString("TeamChat");
        public static string Send => GetString("Send");
        public static string FcmCredentials => GetString("FcmCredentials");
        public static string RightClickConfigure => GetString("RightClickConfigure");
        public static string ResetMap => GetString("ResetMap");
        public static string ResetMapToolTip => GetString("ResetMapToolTip");
        public static string MiniMap => GetString("MiniMap");
        public static string CrosshairToolTip => GetString("CrosshairToolTip");
        public static string TrackPlayer => GetString("TrackPlayer");
        public static string CloseBMBrowser => GetString("CloseBMBrowser");
        public static string ShopSearchTitle => GetString("ShopSearchTitle");
        public static string CloseSearch => GetString("CloseSearch");
        public static string ActivateShopsFirst => GetString("ActivateShopsFirst");
        public static string ActivateShopPolling => GetString("ActivateShopPolling");
        public static string TeamChatTitle => GetString("TeamChatTitle");
        public static string CloseChat => GetString("CloseChat");
        public static string ChatCommandsSettings => GetString("ChatCommandsSettings");
        public static string ChatCommands => GetString("ChatCommands");
        public static string Back => GetString("Back");
        public static string Forward => GetString("Forward");
        public static string OpenInExternalBrowser => GetString("OpenInExternalBrowser");
        public static string SearchBMPlaceholder => GetString("SearchBMPlaceholder");
        public static string Search => GetString("Search");
        public static string SelectAServer => GetString("SelectAServer");
        public static string StatusDisconnected => GetString("StatusDisconnected");
        public static string StatusConnecting => GetString("StatusConnecting");
        public static string StatusInitializing => GetString("StatusInitializing");
        public static string StatusDevicesActive => GetString("StatusDevicesActive");
        public static string StatusFullyConnected => GetString("StatusFullyConnected");
        public static string StatusUnknown => GetString("StatusUnknown");
        public static string MayTakeAMoment => GetString("MayTakeAMoment");
        public static string AppSettingsTitle => GetString("AppSettingsTitle");
        public static string General => GetString("General");
        public static string LaunchWithWindows => GetString("LaunchWithWindows");
        public static string StartMinimizedAlways => GetString("StartMinimizedAlways");
        public static string AutoConnectLastServer => GetString("AutoConnectLastServer");
        public static string AutoConnectToolTip => GetString("AutoConnectToolTip");
        public static string Behavior => GetString("Behavior");
        public static string MinimizeToTray => GetString("MinimizeToTray");
        public static string BackgroundTracking => GetString("BackgroundTracking");
        public static string BackgroundTrackingToolTip => GetString("BackgroundTrackingToolTip");
        public static string HideSystemConsole => GetString("HideSystemConsole");
        public static string HideConsoleToolTip => GetString("HideConsoleToolTip");
        public static string MapSettings => GetString("MapSettings");
        public static string AutoLoadShops => GetString("AutoLoadShops");
        public static string AutoLoadShopsToolTip => GetString("AutoLoadShopsToolTip");
        public static string UseMonumentNames => GetString("UseMonumentNames");
        public static string UseMonumentNamesToolTip => GetString("UseMonumentNamesToolTip");
        public static string MonumentStyle => GetString("MonumentStyle");
        public static string MonumentScale => GetString("MonumentScale");
        public static string MonumentOpacity => GetString("MonumentOpacity");
        public static string MonumentStyleIcons => GetString("MonumentStyleIcons");
        public static string MonumentStyleText => GetString("MonumentStyleText");
        public static string CreditsTitle => GetString("CreditsTitle");
        public static string CreditsIconsPrefix => GetString("CreditsIconsPrefix");
        public static string CreditsIconsSuffix => GetString("CreditsIconsSuffix");
        public static string CreditsIconsNote => GetString("CreditsIconsNote");
        public static string Chat => GetString("Chat");
        public static string ModifyChatAlerts => GetString("ModifyChatAlerts");
        public static string ManageSteamAccountInfo => GetString("ManageSteamAccountInfo");
        public static string Close => GetString("Close");
        public static string IncomingAlarms => GetString("IncomingAlarms");
        public static string Wipe => GetString("Wipe");
        public static string PlayersOnly => GetString("PlayersOnly");
        public static string PlayersHeader => GetString("PlayersHeader");
        public static string IWantToGetToolTip => GetString("IWantToGetToolTip");
        public static string ICanPayWithToolTip => GetString("ICanPayWithToolTip");
        public static string DeleteServerConfirmFormatted => GetString("DeleteServerConfirmFormatted");
        public static string PairingError => GetString("PairingError");
        public static string PairingStarting => GetString("PairingStarting");
        public static string StartingPairingListener => GetString("StartingPairingListener");
        public static string LoggedIn => GetString("LoggedIn");
        public static string Stock => GetString("Stock");
        public static string Price => GetString("Price");
        public static string On => GetString("On");
        public static string Off => GetString("Off");
        public static string Active => GetString("Active");
        public static string Inactive => GetString("Inactive");
        public static string Group => GetString("Group");
        public static string ImportDevicesTitle => GetString("ImportDevicesTitle");
        public static string ImportDevicesHeader => GetString("ImportDevicesHeader");
        public static string ImportDevicesSelectPrompt => GetString("ImportDevicesSelectPrompt");
        public static string CheckDeviceStatus => GetString("CheckDeviceStatus");
        public static string ImportSelected => GetString("ImportSelected");
        public static string BmShortcuts => GetString("BmShortcuts");
        public static string PairingConfigDeleted => GetString("PairingConfigDeleted");
        public static string TrayIconDefault => GetString("TrayIconDefault");
        public static string TrayIconTracking => GetString("TrayIconTracking");
        public static string RenameGroup => GetString("RenameGroup");
        public static string NewNameForGroup => GetString("NewNameForGroup");
        public static string NewNameForDevice => GetString("NewNameForDevice");
        public static string EnterBattlemetricsIdFor => GetString("EnterBattlemetricsIdFor");
        public static string TrayTrackingStatus => GetString("TrayTrackingStatus");
        public static string TrayLastUpdate => GetString("TrayLastUpdate");
        public static string OpenRustPlusDesk => GetString("OpenRustPlusDesk");
        public static string Exit => GetString("Exit");
        public static string Description => GetString("Description");
        public static string Camera => GetString("Camera");
        public static string Alarms => GetString("Alarms");
        public static string Pixel => GetString("Pixel");
        public static string Pen => GetString("Pen");
        public static string Line => GetString("Line");
        public static string Square => GetString("Square");
        public static string Circle => GetString("Circle");
        public static string Thickness => GetString("Thickness");
        public static string Opacity => GetString("Opacity");
        public static string UploadPNG => GetString("UploadPNG");
        public static string Clear => GetString("Clear");
        public static string Save => GetString("Save");
        public static string AssignGlobalHotkeys => GetString("AssignGlobalHotkeys");
        public static string Device => GetString("Device");
        public static string EntityId => GetString("EntityId");
        public static string Hotkey => GetString("Hotkey");
        public static string Set => GetString("Set");
        public static string CloseDeactivate => GetString("CloseDeactivate");
        public static string CloseActivate => GetString("CloseActivate");
        public static string UpkeepMinimapTrackingUpdate => GetString("UpkeepMinimapTrackingUpdate");
        public static string FeatureSummaryAdjustments => GetString("FeatureSummaryAdjustments");
        public static string DontShowAgain => GetString("DontShowAgain");
        public static string AwesomeLetsGo => GetString("AwesomeLetsGo");
        public static string MiniMapSettings => GetString("MiniMapSettings");
        public static string Shape => GetString("Shape");
        public static string Rectangle => GetString("Rectangle");
        public static string OpacityLabel => GetString("OpacityLabel");
        public static string SizeLabel => GetString("SizeLabel");
        public static string ShowServerTime => GetString("ShowServerTime");
        public static string ServerInformation => GetString("ServerInformation");
        public static string NoDescriptionAvailable => GetString("NoDescriptionAvailable");
        public static string CurrentVersion => GetString("CurrentVersion");
        public static string PatchNotesHeader => GetString("PatchNotesHeader");
        public static string EnableChatCommands => GetString("EnableChatCommands");
        public static string Prefix => GetString("Prefix");
        public static string StandardCommands => GetString("StandardCommands");
        public static string Population => GetString("Population");
        public static string Time => GetString("Time");
        public static string Promote => GetString("Promote");
        public static string DeepSeaCommand => GetString("DeepSeaCommand");
        public static string CargoShipCommand => GetString("CargoShipCommand");
        public static string OilRigCommand => GetString("OilRigCommand");
        public static string PatrolHeliCommand => GetString("PatrolHeliCommand");
        public static string TravellingVendorCommand => GetString("TravellingVendorCommand");
        public static string UpkeepDetails => GetString("UpkeepDetails");
        public static string CommandDelay => GetString("CommandDelay");
        public static string Language => GetString("Language");
        public static string SelectLanguage => GetString("SelectLanguage");
        public static string RestartRequired => GetString("RestartRequired");
        public static string SmartSwitches => GetString("SmartSwitches");
        public static string UpkeepTcMonitors => GetString("UpkeepTcMonitors");
        public static string NoTcMonitorsFound => GetString("NoTcMonitorsFound");
        public static string SaveAndClose => GetString("SaveAndClose");
        public static string ToggleServerArea => GetString("ToggleServerArea");
        public static string StreamerMode => GetString("StreamerMode");
        public static string ScaleLabel => GetString("ScaleLabel");
        public static string EnableStreamerMode => GetString("EnableStreamerMode");
        public static string StreamerModeToolTip => GetString("StreamerModeToolTip");
        public static string NoPositionAvailable => GetString("NoPositionAvailable");
        public static string SaveGroup => GetString("SaveGroup");
        public static string EnterGroupName => GetString("EnterGroupName");
        public static string MessageNotSentError => GetString("MessageNotSentError");
        public static string ErrorPrefix => GetString("ErrorPrefix");
        public static string AssignGroup => GetString("AssignGroup");
        public static string GroupSettingsTitle => GetString("GroupSettingsTitle");
        public static string GroupName => GetString("GroupName");
        public static string EnterGroupNamePlaceholder => GetString("EnterGroupNamePlaceholder");
        public static string GroupColor => GetString("GroupColor");
        public static string SaveChanges => GetString("SaveChanges");
        public static string PlayerAnalyticsTitle => GetString("PlayerAnalyticsTitle");
        public static string PlayersTitle => GetString("PlayersTitle");
        public static string AddPlayersToStartTracking => GetString("AddPlayersToStartTracking");
        public static string TrackingActiveStatus => GetString("TrackingActiveStatus");
        public static string TrackingIdleStatus => GetString("TrackingIdleStatus");
        public static string FilterPlayers => GetString("FilterPlayers");
        public static string ConnectToLoadPlayers => GetString("ConnectToLoadPlayers");
        public static string FetchingPlayersSteam => GetString("FetchingPlayersSteam");
        public static string TrackID => GetString("TrackID");
        public static string ManualTrackNamePlaceholder => GetString("ManualTrackNamePlaceholder");
        public static string StreamerModeMiniMapOverhaul => GetString("StreamerModeMiniMapOverhaul");
        public static string PlayerMarkerScalingTitle => GetString("PlayerMarkerScalingTitle");
        public static string MiniMapPolishTitle => GetString("MiniMapPolishTitle");

        public static string ActionRequiredTitle => GetString("ActionRequiredTitle");

        public static string BattleMetricsRemovalTitle => GetString("BattleMetricsRemovalTitle");

        public static string PlayerTrackingOverhaulTitle => GetString("PlayerTrackingOverhaulTitle");

        public static string HowPlayerTrackingWorks => GetString("HowPlayerTrackingWorks");

        public static string StreamerModeTitle => GetString("StreamerModeTitle");

        public static string ChatMessagePlaceholder => GetString("ChatMessagePlaceholder");
        public static string ChatErrorPlaceholder => GetString("ChatErrorPlaceholder");

        public static string HeliEventName => GetString("HeliEventName");
        public static string HeliActiveRunningFor => GetString("HeliActiveRunningFor");
        public static string ConnectedMidEventSpawnUnknown => GetString("ConnectedMidEventSpawnUnknown");
        public static string HeliActiveSpawnUnknown => GetString("HeliActiveSpawnUnknown");
        public static string HeliShotDownAgo => GetString("HeliShotDownAgo");
        public static string HeliLeftMapAgo => GetString("HeliLeftMapAgo");
        public static string CargoDockedDepartsIn => GetString("CargoDockedDepartsIn");
        public static string HarborFallback => GetString("HarborFallback");
        public static string CargoAlreadyDockedOnConnect => GetString("CargoAlreadyDockedOnConnect");
        public static string CargoDockedDurationNotLearned => GetString("CargoDockedDurationNotLearned");
        public static string CargoTimeRemainingOnMap => GetString("CargoTimeRemainingOnMap");
        public static string CargoConnectedMidRouteTimeUnknown => GetString("CargoConnectedMidRouteTimeUnknown");
        public static string CargoConnectedMidRouteTimeUnknownFormatted => GetString("CargoConnectedMidRouteTimeUnknownFormatted");
        public static string CargoDespawnedAgo => GetString("CargoDespawnedAgo");
        public static string VendorActiveRunningFor => GetString("VendorActiveRunningFor");
        public static string VendorActiveSpawnUnknown => GetString("VendorActiveSpawnUnknown");
        public static string VendorDespawnedAgo => GetString("VendorDespawnedAgo");
        public static string DeepSeaActiveRunningFor => GetString("DeepSeaActiveRunningFor");
        public static string DeepSeaActiveSpawnUnknown => GetString("DeepSeaActiveSpawnUnknown");
        public static string DeepSeaEndedAgo => GetString("DeepSeaEndedAgo");
        public static string TimerHoursMinutes => GetString("TimerHoursMinutes");
        public static string TimerMinutesSeconds => GetString("TimerMinutesSeconds");
        public static string AgoHoursMinutes => GetString("AgoHoursMinutes");
        public static string AgoMinutes => GetString("AgoMinutes");
        public static string DurationMinutesSeconds => GetString("DurationMinutesSeconds");
        public static string DurationSeconds => GetString("DurationSeconds");
        public static string ChatCmdPopResponse => GetString("ChatCmdPopResponse");
        public static string ChatCmdPopQueue => GetString("ChatCmdPopQueue");
        public static string ChatCmdTimeResponse => GetString("ChatCmdTimeResponse");
        public static string ChatCmdPromoteResponse => GetString("ChatCmdPromoteResponse");
        public static string ChatCmdDeepSeaActive => GetString("ChatCmdDeepSeaActive");
        public static string ChatCmdDeepSeaActiveMidEvent => GetString("ChatCmdDeepSeaActiveMidEvent");
        public static string ChatCmdDeepSeaEndedMinutesAgo => GetString("ChatCmdDeepSeaEndedMinutesAgo");
        public static string ChatCmdDeepSeaStatusUnknown => GetString("ChatCmdDeepSeaStatusUnknown");
        public static string ChatCmdCargoNotActive => GetString("ChatCmdCargoNotActive");
        public static string ChatCmdCargoDockedDeparts => GetString("ChatCmdCargoDockedDeparts");
        public static string ChatCmdCargoDockedPreparingDepart => GetString("ChatCmdCargoDockedPreparingDepart");
        public static string ChatCmdCargoDockedUnknown => GetString("ChatCmdCargoDockedUnknown");
        public static string ChatCmdCargoActiveLeaves => GetString("ChatCmdCargoActiveLeaves");
        public static string ChatCmdCargoActivePreparingLeave => GetString("ChatCmdCargoActivePreparingLeave");
        public static string ChatCmdCargoActiveDurationNotLearned => GetString("ChatCmdCargoActiveDurationNotLearned");
        public static string ChatCmdCargoActiveMidRoute => GetString("ChatCmdCargoActiveMidRoute");
        public static string ChatCmdCargoDespawnedMinutesAgo => GetString("ChatCmdCargoDespawnedMinutesAgo");
        public static string ChatCmdOilRigCrateIn => GetString("ChatCmdOilRigCrateIn");
        public static string ChatCmdOilRigLastCalledAgo => GetString("ChatCmdOilRigLastCalledAgo");
        public static string ChatCmdOilRigNotCalled => GetString("ChatCmdOilRigNotCalled");
        public static string ChatCmdHeliActive => GetString("ChatCmdHeliActive");
        public static string ChatCmdHeliActiveMidEvent => GetString("ChatCmdHeliActiveMidEvent");
        public static string ChatCmdHeliNotActiveAgo => GetString("ChatCmdHeliNotActiveAgo");
        public static string ChatCmdHeliReasonShotDown => GetString("ChatCmdHeliReasonShotDown");
        public static string ChatCmdHeliReasonLeftMap => GetString("ChatCmdHeliReasonLeftMap");
        public static string ChatCmdHeliStatusUnknown => GetString("ChatCmdHeliStatusUnknown");
        public static string ChatCmdVendorActive => GetString("ChatCmdVendorActive");
        public static string ChatCmdVendorActiveMidEvent => GetString("ChatCmdVendorActiveMidEvent");
        public static string ChatCmdVendorDespawnedAgo => GetString("ChatCmdVendorDespawnedAgo");
        public static string ChatCmdVendorStatusUnknown => GetString("ChatCmdVendorStatusUnknown");
        public static string ChatCmdUpkeepNoTcMapped => GetString("ChatCmdUpkeepNoTcMapped");
        public static string ChatCmdUpkeepTcEmptyExpired => GetString("ChatCmdUpkeepTcEmptyExpired");
        public static string ChatCmdUpkeepNeed24h => GetString("ChatCmdUpkeepNeed24h");
        public static string ChatCmdUpkeepTcTime => GetString("ChatCmdUpkeepTcTime");
        public static string ChatCmdUpkeepDays => GetString("ChatCmdUpkeepDays");
        public static string ChatCmdUpkeepHours => GetString("ChatCmdUpkeepHours");
        public static string ChatCmdUpkeepMinutes => GetString("ChatCmdUpkeepMinutes");
        public static string ChatCmdUpkeepEmptyExpiredShort => GetString("ChatCmdUpkeepEmptyExpiredShort");
        public static string ChatCmdUpkeepTimeShort => GetString("ChatCmdUpkeepTimeShort");
        public static string ChatCmdUpkeepHeader => GetString("ChatCmdUpkeepHeader");
        public static string ChatCmdUpkeepNotPaired => GetString("ChatCmdUpkeepNotPaired");
        public static string ChatCmdUpkeepNotPairedSingle => GetString("ChatCmdUpkeepNotPairedSingle");
        public static string ChatCmdSwitchToggled => GetString("ChatCmdSwitchToggled");
        public static string ChatCmdSwitchStateOn => GetString("ChatCmdSwitchStateOn");
        public static string ChatCmdSwitchStateOff => GetString("ChatCmdSwitchStateOff");
        public static string ChatCmdSwitchNotPaired => GetString("ChatCmdSwitchNotPaired");
        public static string MaterialWood => GetString("MaterialWood");
        public static string MaterialStone => GetString("MaterialStone");
        public static string MaterialMetal => GetString("MaterialMetal");
        public static string MaterialHQM => GetString("MaterialHQM");

        public static string SmallOilRig => GetString("SmallOilRig");
        public static string LargeOilRig => GetString("LargeOilRig");
        public static string ShopWord => GetString("ShopWord");
        public static string EventCargoShip => GetString("EventCargoShip");
        public static string EventTravellingVendor => GetString("EventTravellingVendor");
        public static string EventCH47 => GetString("EventCH47");
        public static string EventPatrolHelicopter => GetString("EventPatrolHelicopter");
        public static string EventOilrigCrate => GetString("EventOilrigCrate");
        public static string EventExplosion => GetString("EventExplosion");
        public static string EventBuildingBlocked => GetString("EventBuildingBlocked");
        public static string EventGeneric => GetString("EventGeneric");
        public static string AlertOilRigTriggered => GetString("AlertOilRigTriggered");
        public static string AlertAlarmTriggered => GetString("AlertAlarmTriggered");
        public static string AlertCrateUnlocksIn10Min => GetString("AlertCrateUnlocksIn10Min");
        public static string AlertCrateUnlocksIn5Min => GetString("AlertCrateUnlocksIn5Min");
        public static string AlertDeepSeaUp => GetString("AlertDeepSeaUp");
        public static string AlertNewShop => GetString("AlertNewShop");
        public static string AlertSuspiciousShop => GetString("AlertSuspiciousShop");
        public static string AlertCargoSpawned => GetString("AlertCargoSpawned");
        public static string CargoFarOutAtSea => GetString("CargoFarOutAtSea");
        public static string AlertCargoDocked => GetString("AlertCargoDocked");
        public static string AlertCargoExpectedDock => GetString("AlertCargoExpectedDock");
        public static string AlertCargoDeparting => GetString("AlertCargoDeparting");
        public static string AlertHeliCrashFalseAlarm => GetString("AlertHeliCrashFalseAlarm");
        public static string AlertEventSpawned => GetString("AlertEventSpawned");
        public static string AlertHeliShotDown => GetString("AlertHeliShotDown");
        public static string Unknown => GetString("Unknown");
        public static string AlertPlayerOnlineWithPos => GetString("AlertPlayerOnlineWithPos");
        public static string AlertPlayerOffline => GetString("AlertPlayerOffline");
        public static string AlertPlayerDied => GetString("AlertPlayerDied");
        public static string AlertPlayerRespawned => GetString("AlertPlayerRespawned");
        public static string AlertTrackingOnline => GetString("AlertTrackingOnline");
        public static string AlertTrackingOffline => GetString("AlertTrackingOffline");
        public static string AlertTrackingRenamed => GetString("AlertTrackingRenamed");
        public static string AlertShopSells => GetString("AlertShopSells");
        public static string AlertShopBuys => GetString("AlertShopBuys");
        public static string AlertShopLabelFallback => GetString("AlertShopLabelFallback");
        public static string AlertShopMatch => GetString("AlertShopMatch");
        public static string ListCommandsCommand => GetString("ListCommandsCommand");
        public static string ChatCmdListHeader => GetString("ChatCmdListHeader");
        
        /// <summary>
        ///   Looks up a localized string similar to Your base is under attack!.
        /// </summary>
        public static string YourBaseIsUnderAttack => GetString("YourBaseIsUnderAttack");
        
        /// <summary>
        ///   Looks up a localized string similar to Please connect to a server first..
        /// </summary>
        public static string PleaseConnectFirst => GetString("PleaseConnectFirst");
        
        /// <summary>
        ///   Looks up a localized string similar to You are not connected to any server..
        /// </summary>
        public static string NotConnectedError => GetString("NotConnectedError");
        
        /// <summary>
        ///   Looks up a localized string similar to Chat is not available right now..
        /// </summary>
        public static string ChatNotAvailable => GetString("ChatNotAvailable");
        
        /// <summary>
        ///   Looks up a localized string similar to Please select a server first..
        /// </summary>
        public static string PleaseSelectServerFirst => GetString("PleaseSelectServerFirst");
        
        /// <summary>
        ///   Looks up a localized string similar to Connection.
        /// </summary>
        public static string SnackbarTitleConnection => GetString("SnackbarTitleConnection");
        
        /// <summary>
        ///   Looks up a localized string similar to Chat.
        /// </summary>
        public static string SnackbarTitleChat => GetString("SnackbarTitleChat");
        
        /// <summary>
        ///   Looks up a localized string similar to How to Track Players.
        /// </summary>
        public static string HowToTrackPlayersTitle => GetString("HowToTrackPlayersTitle");
        
        /// <summary>
        ///   Looks up a localized string similar to Native UDP tracking.
        /// </summary>
        public static string NativeUDPTrackingSubtitle => GetString("NativeUDPTrackingSubtitle");
        
        /// <summary>
        ///   Looks up a localized string similar to The tracking guide is currently only available in English, as it will undergo changes in the near future. We ask for your patience..
        /// </summary>
        public static string TrackingGuideEnglishOnlyNotice => GetString("TrackingGuideEnglishOnlyNotice");
        
        /// <summary>
        ///   Looks up a localized string similar to Close this window at any time — you can reopen it with the  ❓ How to Track  button..
        /// </summary>
        public static string HowToTrackCloseHint => GetString("HowToTrackCloseHint");

        public static string MaintenanceTitle => GetString("MaintenanceTitle");
        public static string MaintenanceDesc => GetString("MaintenanceDesc");
        public static string ResetAppData => GetString("ResetAppData");
        public static string BackupData => GetString("BackupData");
        public static string RestoreData => GetString("RestoreData");
        public static string ResetDataTitle => GetString("ResetDataTitle");
        public static string ResetDataDesc => GetString("ResetDataDesc");
        public static string ResetOptionConnection => GetString("ResetOptionConnection");
        public static string ResetOptionProfiles => GetString("ResetOptionProfiles");
        public static string ResetOptionSteam => GetString("ResetOptionSteam");
        public static string ResetOptionPairing => GetString("ResetOptionPairing");
        public static string ResetOptionCrosshairs => GetString("ResetOptionCrosshairs");
        public static string ResetOptionCache => GetString("ResetOptionCache");
        public static string ResetDataWarning => GetString("ResetDataWarning");
        public static string ConfirmReset => GetString("ConfirmReset");
        public static string BackupPasswordTitle => GetString("BackupPasswordTitle");
        public static string BackupProtectionTitle => GetString("BackupProtectionTitle");
        public static string BackupProtectionDesc => GetString("BackupProtectionDesc");
        public static string RestoreBackupTitle => GetString("RestoreBackupTitle");
        public static string RestoreBackupDesc => GetString("RestoreBackupDesc");
        public static string Encrypt => GetString("Encrypt");
        public static string Decrypt => GetString("Decrypt");
        public static string OK => GetString("OK");
        public static string BackupApplicationDataTitle => GetString("BackupApplicationDataTitle");
        public static string BackupSuccessLog => GetString("BackupSuccessLog");
        public static string BackupSuccessMessage => GetString("BackupSuccessMessage");
        public static string BackupSuccessTitle => GetString("BackupSuccessTitle");
        public static string BackupErrorLog => GetString("BackupErrorLog");
        public static string BackupErrorMessage => GetString("BackupErrorMessage");
        public static string BackupFailedTitle => GetString("BackupFailedTitle");
        public static string RestoreConfirmMessage => GetString("RestoreConfirmMessage");
        public static string RestoreConfirmTitle => GetString("RestoreConfirmTitle");
        public static string RestoreApplicationDataTitle => GetString("RestoreApplicationDataTitle");
        public static string RestoreSuccessMessage => GetString("RestoreSuccessMessage");
        public static string RestoreSuccessTitle => GetString("RestoreSuccessTitle");
        public static string RestorePasswordErrorLog => GetString("RestorePasswordErrorLog");
        public static string RestorePasswordErrorMessage => GetString("RestorePasswordErrorMessage");
        public static string RestoreFailedTitle => GetString("RestoreFailedTitle");
        public static string RestoreErrorLog => GetString("RestoreErrorLog");
        public static string RestoreErrorMessage => GetString("RestoreErrorMessage");
        public static string InvalidBackupFormat => GetString("InvalidBackupFormat");
        public static string WipeLogStart => GetString("WipeLogStart");
        public static string WipeLogConnStart => GetString("WipeLogConnStart");
        public static string WipeLogConnEnd => GetString("WipeLogConnEnd");
        public static string WipeLogProfilesStart => GetString("WipeLogProfilesStart");
        public static string WipeLogProfilesEnd => GetString("WipeLogProfilesEnd");
        public static string WipeLogSteamStart => GetString("WipeLogSteamStart");
        public static string WipeLogSteamEnd => GetString("WipeLogSteamEnd");
        public static string WipeLogPairingStart => GetString("WipeLogPairingStart");
        public static string WipeLogPairingEnd => GetString("WipeLogPairingEnd");
        public static string WipeLogCrosshairsStart => GetString("WipeLogCrosshairsStart");
        public static string WipeLogCrosshairsError => GetString("WipeLogCrosshairsError");
        public static string WipeLogCrosshairsEnd => GetString("WipeLogCrosshairsEnd");
        public static string WipeLogCacheStart => GetString("WipeLogCacheStart");
        public static string WipeLogCacheError => GetString("WipeLogCacheError");
        public static string WipeLogCacheEnd => GetString("WipeLogCacheEnd");
        public static string WipeLogPairingRestart => GetString("WipeLogPairingRestart");
        public static string WipeLogComplete => GetString("WipeLogComplete");
        public static string LoopAudioTillPopupClose => GetString("LoopAudioTillPopupClose");
        public static string LoopAudioPrompt => GetString("LoopAudioPrompt");
        public static string LoopAudioGlobalPrompt => GetString("LoopAudioGlobalPrompt");
        public static string TurnOffNow => GetString("TurnOffNow");
        public static string KeepActive => GetString("KeepActive");
    }
}
