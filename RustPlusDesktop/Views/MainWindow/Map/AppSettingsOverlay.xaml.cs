using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Net.Http;
using System.Text.Json;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    public partial class AppSettingsOverlay : UserControl
    {
        public MainWindow? ParentWindow { get; set; }
        private bool _isSettingsInitialized = false;
        private IReadOnlyList<SettingsSectionDefinition> _settingsSections = Array.Empty<SettingsSectionDefinition>();
        private IReadOnlyList<SettingsOptionDefinition> _settingsOptions = Array.Empty<SettingsOptionDefinition>();
        private string _activeSettingsCategory = "general";
        private bool _isShowingSearchResults;
        private bool _returnToCategoryPageAfterSearch;
        private readonly List<(AdornerLayer Layer, Adorner Adorner)> _settingsHighlights = new();
        private int _settingsHighlightGeneration;

        private sealed class SettingsSectionDefinition
        {
            public required string Id { get; init; }
            public required string Category { get; init; }
            public required string Title { get; init; }
            public required string Keywords { get; init; }
            public required FrameworkElement Element { get; init; }
        }

        private sealed class SettingsSearchResult
        {
            public required string Id { get; init; }
            public required string Category { get; init; }
            public required string CategoryTitle { get; init; }
            public required string Title { get; init; }
        }

        private sealed class SettingsOptionDefinition
        {
            public required string SectionId { get; init; }
            public required string SectionTitle { get; init; }
            public required string Category { get; init; }
            public required string Title { get; init; }
            public required string SearchText { get; init; }
            public required Control Target { get; init; }
        }

        private sealed class SettingsOptionResult
        {
            public required string SectionId { get; init; }
            public required string SectionTitle { get; init; }
            public required string Category { get; init; }
            public required string CategoryTitle { get; init; }
            public required string BeforeMatch { get; init; }
            public required string Match { get; init; }
            public required string AfterMatch { get; init; }
            public required Control Target { get; init; }
        }

        private sealed class SettingsMatchAdorner(UIElement adornedElement) : Adorner(adornedElement)
        {
            protected override void OnRender(DrawingContext drawingContext)
            {
                var bounds = new Rect(0, 0, AdornedElement.RenderSize.Width, AdornedElement.RenderSize.Height);
                drawingContext.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(0x24, 0x60, 0xCD, 0xFF)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)), 1.5),
                    bounds,
                    5,
                    5);
            }
        }

        public class LanguageOption
        {
            public string Name { get; set; } = "";
            public string Code { get; set; } = "";
            public string? ImagePath { get; set; }
        }

        public AppSettingsOverlay()
        {
            InitializeComponent();
            InitializeSettingsNavigation();
            Loaded += AppSettingsOverlay_Loaded;
            IsVisibleChanged += AppSettingsOverlay_IsVisibleChanged;
        }

        private void AppSettingsOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SettingsSearchBox.Focus();
                    Keyboard.Focus(SettingsSearchBox);
                    SettingsSearchBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
                return;
            }

            _isShowingSearchResults = false;
            _returnToCategoryPageAfterSearch = false;
            SettingsSearchBox.Clear();
            ClearSettingsHighlights();
            ShowSettingsCategoryList();
        }

        private static string T(string key, string fallback)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void InitializeSettingsNavigation()
        {
            _settingsSections = new[]
            {
                Section("general", "general", T("General", "General"), "language startup launch windows minimized auto connect server", SectionGeneral),
                Section("behavior", "general", T("Behavior", "Behavior"), "tray closing streamer privacy background tracking console cloud sync upload", SectionBehavior),
                Section("offline-death", "alerts", T("OfflineDeathNotifications", "Offline Death Notifications"), "offline death raid alerts sound loop discord log", SectionOfflineDeath),
                Section("notifications", "alerts", T("NotificationCenterSettings", "Notification Center"), "toast sound alerts retention days muted servers notification center", SectionNotifications),
                Section("map-performance", "map", "Map Performance & Quality", "image scaling quality gpu bitmap cache rendering scale anti aliasing performance", SectionMapPerformance),
                Section("team-markers", "map", T("TeamMarkersSettings", "Team Markers"), "profile player direction arrows death markers streamer icon scale", SectionTeamMarkers),
                Section("3d-map", "map", T("ThreeDMapSectionTitle", "3D Map"), "3d map delete data parse manually quality", SectionThreeDMap),
                Section("cloud", "connected", "Cloud Account & Sync", "cloud account discord email supporter webhook fcm alexa smart home bot channels sync", SectionCloud),
                Section("chat", "connected", T("Chat", "Chat"), "chat alerts commands modify", SectionChat),
                Section("steam", "connected", T("SteamAccount", "Steam Account"), "steam account companion pairing manage", SectionSteamAccount),
                Section("maintenance", "system", T("MaintenanceTitle", "Maintenance"), "reset app data backup restore maintenance", SectionMaintenance),
                Section("credits", "system", T("CreditsTitle", "Credits"), "credits rustmaps icons legal", SectionCredits)
            };

            ShowSettingsCategoryList();
        }

        private static SettingsSectionDefinition Section(string id, string category, string title, string keywords, FrameworkElement element) =>
            new() { Id = id, Category = category, Title = title, Keywords = keywords, Element = element };

        private void BuildSettingsOptionIndex()
        {
            _settingsOptions = _settingsSections
                .SelectMany(section => EnumerateControls(section.Element)
                    .Where(IsSearchableSettingControl)
                    .Select(control => new SettingsOptionDefinition
                    {
                        SectionId = section.Id,
                        SectionTitle = section.Title,
                        Category = section.Category,
                        Title = GetControlTitle(control),
                        SearchText = $"{GetControlTitle(control)} {control.ToolTip} {section.Title}",
                        Target = control
                    }))
                .Where(option => option.Title.Length > 1)
                .DistinctBy(option => $"{option.SectionId}|{option.Title}", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSearchableSettingControl(Control control) =>
            control is CheckBox or ComboBox or Slider or TextBox or ButtonBase or Expander;

        private static IEnumerable<Control> EnumerateControls(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is Control control)
                {
                    yield return control;
                }

                foreach (var descendant in EnumerateControls(child))
                {
                    yield return descendant;
                }
            }
        }

        private static string GetControlTitle(Control control)
        {
            var automationName = System.Windows.Automation.AutomationProperties.GetName(control);
            if (!string.IsNullOrWhiteSpace(automationName))
            {
                return automationName.Trim();
            }

            object? label = control switch
            {
                HeaderedContentControl headered => headered.Header,
                ContentControl content => content.Content,
                _ => null
            };
            var contentText = ExtractText(label);
            return string.IsNullOrWhiteSpace(contentText) ? HumanizeControlName(control.Name) : contentText;
        }

        private static string ExtractText(object? value)
        {
            if (value is string text)
            {
                return text.Trim();
            }

            if (value is TextBlock textBlock)
            {
                return textBlock.Text.Trim();
            }

            if (value is not DependencyObject element)
            {
                return "";
            }

            return string.Join(" ", LogicalTreeHelper.GetChildren(element)
                .Cast<object>()
                .Select(ExtractText)
                .Where(text => text.Length > 0));
        }

        private static string HumanizeControlName(string name)
        {
            foreach (var prefix in new[] { "Chk", "Cmb", "Slider", "Txt", "Btn" })
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    name = name[prefix.Length..];
                    break;
                }
            }

            var words = new System.Text.StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    words.Append(' ');
                }
                words.Append(name[i]);
            }
            return words.ToString().Replace("Url", "URL", StringComparison.Ordinal).Trim();
        }

        private void SettingsCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsCategoryList.SelectedItem is not ListBoxItem { Tag: string category })
            {
                return;
            }

            SettingsCategoryList.SelectedItem = null;
            SettingsSearchBox.Clear();
            ShowSettingsCategory(category);
        }

        private void ShowSettingsCategoryList()
        {
            SettingsCategoryPage.Visibility = Visibility.Visible;
            SettingsDetailPage.Visibility = Visibility.Collapsed;
            SettingsCategoryList.SelectedItem = null;
        }

        private void ShowSettingsCategory(string category, string? selectedSectionId = null)
        {
            _activeSettingsCategory = category;
            var sections = _settingsSections.Where(section => section.Category == category).ToList();
            foreach (var section in _settingsSections)
            {
                section.Element.Visibility = section.Category == category ? Visibility.Visible : Visibility.Collapsed;
            }

            var (title, description) = GetSettingsCategoryText(category);
            SettingsDetailTitle.Text = title;
            SettingsDetailSubtitle.Text = description;
            SettingsCategoryPage.Visibility = Visibility.Collapsed;
            SettingsDetailPage.Visibility = Visibility.Visible;
            var submenuItems = sections.Select(section => new SettingsSearchResult
            {
                Id = section.Id,
                Category = section.Category,
                CategoryTitle = title,
                Title = section.Title
            }).ToList();
            SettingsSectionList.ItemsSource = submenuItems;
            SettingsSectionList.SelectedItem = selectedSectionId == null
                ? null
                : submenuItems.FirstOrDefault(item => item.Id == selectedSectionId);
            SettingsSectionList.Visibility = Visibility.Visible;
            SettingsSearchResultsScroller.Visibility = Visibility.Collapsed;
            SettingsScrollViewer.Visibility = Visibility.Visible;
            SettingsScrollViewer.ScrollToTop();

            if (string.IsNullOrWhiteSpace(SettingsSearchBox.Text))
            {
                ClearSettingsHighlights();
            }
            else
            {
                HighlightMatchingSettings(SettingsSearchBox.Text, category);
            }
        }

        private void HighlightMatchingSettings(string query, string category)
        {
            ClearSettingsHighlights();
            var targets = _settingsOptions
                .Where(option => option.Category == category && SettingsSearchMatcher.Matches(query, option.Title, option.SearchText))
                .Select(option => option.Target)
                .Distinct()
                .ToList();
            var generation = _settingsHighlightGeneration;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _settingsHighlightGeneration)
                {
                    return;
                }

                foreach (var target in targets)
                {
                    var layer = AdornerLayer.GetAdornerLayer(target);
                    if (layer == null)
                    {
                        continue;
                    }

                    var adorner = new SettingsMatchAdorner(target) { IsHitTestVisible = false };
                    layer.Add(adorner);
                    _settingsHighlights.Add((layer, adorner));
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ClearSettingsHighlights()
        {
            _settingsHighlightGeneration++;
            foreach (var (layer, adorner) in _settingsHighlights)
            {
                layer.Remove(adorner);
            }
            _settingsHighlights.Clear();
        }

        private void SettingsSectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsSectionList.SelectedItem is not SettingsSearchResult selected)
            {
                return;
            }

            var section = _settingsSections.First(candidate => candidate.Id == selected.Id);
            ScrollToSettingsElement(section.Element);
        }

        private void ScrollToSettingsElement(FrameworkElement target, bool focus = false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExpandAncestorSettingsGroups(target);
                SettingsScrollViewer.UpdateLayout();
                target.UpdateLayout();

                try
                {
                    var top = target.TransformToAncestor(SettingsSectionsPanel).Transform(new Point()).Y;
                    SettingsScrollViewer.ScrollToVerticalOffset(Math.Clamp(top - 12, 0, SettingsScrollViewer.ScrollableHeight));
                }
                catch (InvalidOperationException)
                {
                    target.BringIntoView();
                }

                if (focus)
                {
                    target.Focus();
                    Keyboard.Focus(target);
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private static void ExpandAncestorSettingsGroups(DependencyObject element)
        {
            for (var parent = GetSettingsParent(element); parent != null; parent = GetSettingsParent(parent))
            {
                if (parent is Expander expander)
                {
                    expander.IsExpanded = true;
                }
            }
        }

        private static DependencyObject? GetSettingsParent(DependencyObject element) =>
            LogicalTreeHelper.GetParent(element) ?? (element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : null);

        private static (string Title, string Description) GetSettingsCategoryText(string category) => category switch
        {
            "alerts" => ("Alerts", "Notifications, sounds, and offline death alerts"),
            "map" => ("Map", "Performance, markers, and 3D map data"),
            "connected" => ("Connected Services", "Cloud, integrations, chat, and account connections"),
            "system" => ("System", "Maintenance, backup, reset, and application information"),
            _ => ("General", "Language, startup, and application behavior")
        };

        private void SettingsBack_Click(object sender, RoutedEventArgs e)
        {
            _isShowingSearchResults = false;
            SettingsSearchBox.Clear();
            ShowSettingsCategoryList();
        }

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SettingsSearchBox.Text.Trim();
            if (query.Length == 0)
            {
                ClearSettingsHighlights();
                if (!_isShowingSearchResults)
                {
                    return;
                }

                _isShowingSearchResults = false;
                if (_returnToCategoryPageAfterSearch)
                {
                    ShowSettingsCategoryList();
                }
                else
                {
                    ShowSettingsCategory(_activeSettingsCategory);
                }
                return;
            }

            ClearSettingsHighlights();
            if (!_isShowingSearchResults)
            {
                _returnToCategoryPageAfterSearch = SettingsCategoryPage.Visibility == Visibility.Visible;
            }
            _isShowingSearchResults = true;

            if (_settingsOptions.Count == 0)
            {
                BuildSettingsOptionIndex();
            }

            var matches = _settingsOptions
                .Where(option => SettingsSearchMatcher.Matches(query, option.Title, option.SearchText))
                .OrderBy(option => option.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(option => option.Title)
                .ToList();
            SettingsSearchResults.ItemsSource = matches.Select(option => CreateSettingsOptionResult(option, query)).ToList();
            SettingsDetailTitle.Text = "Search results";
            SettingsDetailSubtitle.Text = matches.Count == 0
                ? $"No settings found for “{query}”"
                : $"{matches.Count} setting{(matches.Count == 1 ? "" : "s")} found for “{query}”";
            SettingsCategoryPage.Visibility = Visibility.Collapsed;
            SettingsDetailPage.Visibility = Visibility.Visible;
            SettingsSectionList.Visibility = Visibility.Collapsed;
            SettingsScrollViewer.Visibility = Visibility.Collapsed;
            SettingsSearchResultsScroller.Visibility = Visibility.Visible;
        }

        private static SettingsOptionResult CreateSettingsOptionResult(SettingsOptionDefinition option, string query)
        {
            var matchingTerm = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(term => option.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (matchingTerm == null)
            {
                return new SettingsOptionResult
                {
                    SectionId = option.SectionId,
                    SectionTitle = option.SectionTitle,
                    Category = option.Category,
                    CategoryTitle = GetSettingsCategoryText(option.Category).Title,
                    BeforeMatch = option.Title,
                    Match = "",
                    AfterMatch = "",
                    Target = option.Target
                };
            }

            var matchIndex = option.Title.IndexOf(matchingTerm, StringComparison.OrdinalIgnoreCase);
            return new SettingsOptionResult
            {
                SectionId = option.SectionId,
                SectionTitle = option.SectionTitle,
                Category = option.Category,
                CategoryTitle = GetSettingsCategoryText(option.Category).Title,
                BeforeMatch = option.Title[..matchIndex],
                Match = option.Title.Substring(matchIndex, matchingTerm.Length),
                AfterMatch = option.Title[(matchIndex + matchingTerm.Length)..],
                Target = option.Target
            };
        }

        private void SettingsSearchResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfUi.Button { Tag: SettingsOptionResult result })
            {
                return;
            }

            _isShowingSearchResults = false;
            _returnToCategoryPageAfterSearch = false;
            ShowSettingsCategory(result.Category, result.SectionId);
            ScrollToSettingsElement(result.Target, focus: true);
        }

        private void AppSettingsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSettingsInitialized) return;
            
            PopulateLanguages();
            LoadSettings();
            BuildSettingsOptionIndex();
            _isSettingsInitialized = true;
        }

        private void PopulateLanguages()
        {
            var langs = new List<LanguageOption>
            {
                new() { Name = "System Default", Code = "", ImagePath = null },
                // Region-specific codes matching %locale% Crowdin-generated folders
                new() { Name = "English",           Code = "en-US",  ImagePath = "pack://application:,,,/Assets/Flags/en.png" },
                new() { Name = "Deutsch",            Code = "de-DE",  ImagePath = "pack://application:,,,/Assets/Flags/de.png" },
                new() { Name = "Français",           Code = "fr-FR",  ImagePath = "pack://application:,,,/Assets/Flags/fr.png" },
                new() { Name = "Español",            Code = "es-ES",  ImagePath = "pack://application:,,,/Assets/Flags/es-ES.png" },
                new() { Name = "Italiano",           Code = "it-IT",  ImagePath = "pack://application:,,,/Assets/Flags/it.png" },
                new() { Name = "Polski",             Code = "pl-PL",  ImagePath = "pack://application:,,,/Assets/Flags/pl.png" },
                new() { Name = "Русский",            Code = "ru-RU",  ImagePath = "pack://application:,,,/Assets/Flags/ru.png" },
                new() { Name = "Türkçe",             Code = "tr-TR",  ImagePath = "pack://application:,,,/Assets/Flags/tr.png" },
                new() { Name = "Português (BR)",     Code = "pt-BR",  ImagePath = "pack://application:,,,/Assets/Flags/pt-BR.png" },
                new() { Name = "Português (PT)",     Code = "pt-PT",  ImagePath = "pack://application:,,,/Assets/Flags/pt-PT.png" },
                new() { Name = "Nederlands",         Code = "nl-NL",  ImagePath = "pack://application:,,,/Assets/Flags/nl.png" },
                new() { Name = "Dansk",              Code = "da-DK",  ImagePath = "pack://application:,,,/Assets/Flags/da.png" },
                new() { Name = "Norsk",              Code = "no-NO",  ImagePath = "pack://application:,,,/Assets/Flags/no.png" },
                new() { Name = "Svenska",            Code = "sv-SE",  ImagePath = "pack://application:,,,/Assets/Flags/sv-SE.png" },
                new() { Name = "Suomi",              Code = "fi-FI",  ImagePath = "pack://application:,,,/Assets/Flags/fi.png" },
                new() { Name = "Čeština",            Code = "cs-CZ",  ImagePath = "pack://application:,,,/Assets/Flags/cs.png" },
                new() { Name = "Magyar",             Code = "hu-HU",  ImagePath = "pack://application:,,,/Assets/Flags/hu.png" },
                new() { Name = "Română",             Code = "ro-RO",  ImagePath = "pack://application:,,,/Assets/Flags/ro.png" },
                new() { Name = "Srpski",             Code = "sr-SP",  ImagePath = "pack://application:,,,/Assets/Flags/sr.png" },
                new() { Name = "Ελληνικά",           Code = "el-GR",  ImagePath = "pack://application:,,,/Assets/Flags/el.png" },
                new() { Name = "Українська",         Code = "uk-UA",  ImagePath = "pack://application:,,,/Assets/Flags/uk.png" },
                new() { Name = "Tiếng Việt",         Code = "vi-VN",  ImagePath = "pack://application:,,,/Assets/Flags/vi.png" },
                new() { Name = "العربية",             Code = "ar-SA",  ImagePath = "pack://application:,,,/Assets/Flags/ar.png" },
                new() { Name = "עברית",              Code = "he-IL",  ImagePath = "pack://application:,,,/Assets/Flags/he.png" },
                new() { Name = "日本語",              Code = "ja-JP",  ImagePath = "pack://application:,,,/Assets/Flags/ja.png" },
                new() { Name = "한국어",              Code = "ko-KR",  ImagePath = "pack://application:,,,/Assets/Flags/ko.png" },
                new() { Name = "简体中文",            Code = "zh-CN",  ImagePath = "pack://application:,,,/Assets/Flags/zh-CN.png" },
                new() { Name = "繁體中文",            Code = "zh-TW",  ImagePath = "pack://application:,,,/Assets/Flags/zh-TW.png" },
                new() { Name = "简体中文 (Hans)",     Code = "zh-Hans", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hans.png" },
                new() { Name = "繁體中文 (Hant)",     Code = "zh-Hant", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hant.png" },
                new() { Name = "Català",             Code = "ca-ES",  ImagePath = "pack://application:,,,/Assets/Flags/ca.png" },
                new() { Name = "Afrikaans",          Code = "af-ZA",  ImagePath = "pack://application:,,,/Assets/Flags/af.png" },
            };

            CmbLanguage.ItemsSource = langs.OrderBy(l => l.Code == "" ? 1 : 0).ThenBy(l => l.Name).ToList();
        }

        public void LoadSettings()
        {
            CmbLanguage.SelectedValue = TrackingService.SelectedLanguage;
            
            ChkAutoStart.IsChecked = TrackingService.AutoStartEnabled;
            ChkStartMinimized.IsChecked = TrackingService.StartMinimizedEnabled;
            ChkAutoConnect.IsChecked = TrackingService.AutoConnectEnabled;
            ChkCloseToTray.IsChecked = TrackingService.CloseToTrayEnabled;
            ChkBackgroundTracking.IsChecked = TrackingService.IsBackgroundTrackingEnabled;
            ChkHideConsole.IsChecked = TrackingService.HideConsole;
            ChkStreamerMode.IsChecked = TrackingService.MapAbbreviateNames;
            
            TxtDiscordWebhookUrl.Text = TrackingService.DiscordWebhookUrl;
            var fcmMention = TrackingService.DiscordWebhookMention ?? "";
            ChkFcmMentionEveryone.IsChecked = fcmMention.Contains("@everyone");
            ChkFcmMentionHere.IsChecked = fcmMention.Contains("@here");
            TxtSmartHomeWebhookUrl.Text = TrackingService.SmartHomeWebhookUrl;
            
            // Load Telegram State
            TxtTelegramUser.Text = TrackingService.TelegramCallUser;
            TxtTelegramMsg.Text = TrackingService.TelegramCallMsg;
            if (string.IsNullOrEmpty(TxtTelegramMsg.Text)) TxtTelegramMsg.Text = "Alarm ausgeloest!";
            
            foreach (ComboBoxItem item in CmbTelegramLang.Items)
            {
                if (item.Tag?.ToString() == TrackingService.TelegramCallLang)
                {
                    CmbTelegramLang.SelectedItem = item;
                    break;
                }
            }
            
            ChkTelegramIncTitle.IsChecked = TrackingService.TelegramCallIncTitle;
            ChkTelegramIncMsg.IsChecked = TrackingService.TelegramCallIncMsg;
            ChkTelegramIncType.IsChecked = TrackingService.TelegramCallIncType;

            var telegramUrl = TrackingService.TelegramCallWebhookUrl;
            if (!string.IsNullOrEmpty(telegramUrl))
            {
                TxtGeneratedTelegramUrl.Text = telegramUrl;
                TxtGeneratedTelegramUrl.Visibility = Visibility.Visible;
                BtnTestTelegramUrl.Visibility = Visibility.Visible;
                BtnRevokeTelegramUrl.Visibility = Visibility.Visible;
            }

            // Map performance settings
            CmbMapScalingMode.SelectedIndex = Math.Clamp(TrackingService.MapBitmapScalingMode, 0, 2);
            ChkMapUseCacheMode.IsChecked = TrackingService.MapUseCacheMode;
            
            double scale = TrackingService.MapRenderScale;
            int renderScaleIdx = 2; // Default to 1.0 (Native)
            if (Math.Abs(scale - 0.5) < 0.01) renderScaleIdx = 0;
            else if (Math.Abs(scale - 0.75) < 0.01) renderScaleIdx = 1;
            else if (Math.Abs(scale - 1.0) < 0.01) renderScaleIdx = 2;
            else if (Math.Abs(scale - 1.25) < 0.01) renderScaleIdx = 3;
            else if (Math.Abs(scale - 1.5) < 0.01) renderScaleIdx = 4;
            else if (Math.Abs(scale - 2.0) < 0.01) renderScaleIdx = 5;
            CmbMapRenderScale.SelectedIndex = renderScaleIdx;

            ChkMapUseAliasedEdgeMode.IsChecked = TrackingService.MapUseAliasedEdgeMode;

            // Cloud Sync Setting load
            ChkCloudSync.IsChecked = TrackingService.CloudSyncEnabled;

            // Team marker settings
            ChkShowProfileMarkers.IsChecked  = TrackingService.MapShowSteamMarkers;
            ChkShowPlayerArrows.IsChecked    = TrackingService.MapShowPlayerArrows;
            ChkShowDeathMarkers.IsChecked    = TrackingService.MapShowDeathTags;
            ChkStreamerModeMarkers.IsChecked  = TrackingService.MapAbbreviateNames;
            SliderPlayerIconScaleOverlay.Value = TrackingService.MapPlayerIconScale;

            // Offline Death
            ChkOfflineDeathAlerts.IsChecked = TrackingService.OfflineDeathAlertsEnabled;
            TxtOfflineDeathSoundPath.Text = string.IsNullOrEmpty(TrackingService.OfflineDeathSoundPath) ? Properties.Resources.DefaultSoundLabel : System.IO.Path.GetFileName(TrackingService.OfflineDeathSoundPath);
            ChkOfflineDeathSoundLoop.IsChecked = TrackingService.OfflineDeathSoundLoopEnabled;
            ChkOfflineDeathDiscord.IsChecked = TrackingService.OfflineDeathDiscordEnabled;

            // Notification Center Settings
            ChkNotificationsToast.IsChecked = TrackingService.NotificationsToastEnabled;
            ChkNotificationsSounds.IsChecked = TrackingService.NotificationsSoundsEnabled;
            SliderNotificationsRetention.Value = TrackingService.NotificationsRetentionDays;
            TxtRetentionDays.Text = string.Format(T("NotificationRetentionDays", "{0} days"), (int)SliderNotificationsRetention.Value);
            PopulateMutedServers();


            // Auth connection state
            bool isDiscord = Services.Auth.SupabaseAuthManager.IsDiscordAuthenticated;
            bool isEmail   = Services.Auth.SupabaseAuthManager.IsEmailAuthenticated;
            bool connected = isDiscord || isEmail;
            bool isPremium = Services.Auth.SupabaseAuthManager.IsPremium;

            if (isDiscord)
            {
                TxtDiscordBtnLabel.Text = T("AuthDiscordDisconnectButton", "Disconnect Discord");
                BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Caution;

                int maxBytes = Services.Auth.SupabaseAuthManager.GetMaxOverlayBytes();
                string maxOverlay = maxBytes == int.MaxValue ? "unlimited" : $"{maxBytes / 1024} KB";
                int maxDevices = Services.Auth.SupabaseAuthManager.GetMaxDevices();
                string maxDevs = maxDevices == int.MaxValue ? "unlimited" : maxDevices.ToString();
                int maxBases = Services.Auth.SupabaseAuthManager.GetMaxBases();
                string maxBs = maxBases == int.MaxValue ? "unlimited" : maxBases.ToString();

                int currentOverlayKb = ParentWindow != null ? Math.Max(1, (int)Math.Ceiling(ParentWindow.GetCurrentOverlaySizeBytes() / 1024.0)) : 0;
                int currentDevices = ParentWindow != null ? ParentWindow.GetCurrentDevicesCount() : 0;
                int currentBases = ParentWindow != null ? ParentWindow.GetCurrentBaseCount() : 0;

                string baseText = string.Format(T("AuthDiscordConnectedFormat", "Discord connected - Tier: {0}"), Services.Auth.SupabaseAuthManager.CurrentTier.ToUpper());
                TxtAuthStatus.Text = $"{baseText}\nLimits Usage:\n• Overlay size: {currentOverlayKb} KB / {maxOverlay}\n• Devices: {currentDevices} / {maxDevs}\n• Bases: {currentBases} / {maxBs}";
                TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else if (isEmail)
            {
                var email = Services.Auth.SupabaseAuthManager.Client?.Auth?.CurrentUser?.Email ?? "";
                TxtDiscordBtnLabel.Text = "Sign in with Discord";
                BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

                int maxBytes = Services.Auth.SupabaseAuthManager.GetMaxOverlayBytes();
                string maxOverlay = maxBytes == int.MaxValue ? "unlimited" : $"{maxBytes / 1024} KB";
                int maxDevices = Services.Auth.SupabaseAuthManager.GetMaxDevices();
                string maxDevs = maxDevices == int.MaxValue ? "unlimited" : maxDevices.ToString();
                int maxBases = Services.Auth.SupabaseAuthManager.GetMaxBases();
                string maxBs = maxBases == int.MaxValue ? "unlimited" : maxBases.ToString();

                int currentOverlayKb = ParentWindow != null ? Math.Max(1, (int)Math.Ceiling(ParentWindow.GetCurrentOverlaySizeBytes() / 1024.0)) : 0;
                int currentDevices = ParentWindow != null ? ParentWindow.GetCurrentDevicesCount() : 0;
                int currentBases = ParentWindow != null ? ParentWindow.GetCurrentBaseCount() : 0;

                string baseText = string.Format(T("AuthEmailConnectedFormat", "Email connected: {0} - Tier: {1}"), email, Services.Auth.SupabaseAuthManager.CurrentTier.ToUpper());
                TxtAuthStatus.Text = $"{baseText}\nLimits Usage:\n• Overlay size: {currentOverlayKb} KB / {maxOverlay}\n• Devices: {currentDevices} / {maxDevs}\n• Bases: {currentBases} / {maxBs}";
                TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                TxtDiscordBtnLabel.Text = "Sign in with Discord";
                BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                TxtAuthStatus.Text = T("AuthNotConnectedStatus", "Not connected - sign in to use Cloud Sync and backups");
                TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
            }

            BrdSupporterSettings.IsEnabled = connected && isPremium;
            BrdSupporterSettings.Opacity = (connected && isPremium) ? 1.0 : 0.5;
            BtnEmailConnect.Content = isEmail ? "Manage email account" : "Sign in with email";

            if (connected && isPremium)
            {
                _ = LoadDiscordBotSettingsAsync();
            }

            if (connected)
            {
                PopulateAlexaServers();
                _ = LoadAlexaSettingsAsync();
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            var code = CmbLanguage.SelectedValue as string;
            if (code != null)
            {
                TrackingService.SelectedLanguage = code;
                
                // Try to apply it immediately
                if (Application.Current is App app)
                {
                    app.SetLanguage();
                }
            }
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.AutoStartEnabled = ChkAutoStart.IsChecked == true;
            TrackingService.StartMinimizedEnabled = ChkStartMinimized.IsChecked == true;
            TrackingService.AutoConnectEnabled = ChkAutoConnect.IsChecked == true;
            TrackingService.CloseToTrayEnabled = ChkCloseToTray.IsChecked == true;
            TrackingService.IsBackgroundTrackingEnabled = ChkBackgroundTracking.IsChecked == true;
            TrackingService.HideConsole = ChkHideConsole.IsChecked == true;
            TrackingService.MapAbbreviateNames = ChkStreamerMode.IsChecked == true;
            
            if (CmbMapScalingMode != null && CmbMapScalingMode.SelectedIndex >= 0)
            {
                TrackingService.MapBitmapScalingMode = CmbMapScalingMode.SelectedIndex;
            }
            TrackingService.MapUseCacheMode = ChkMapUseCacheMode.IsChecked == true;
            if (CmbMapRenderScale != null && CmbMapRenderScale.SelectedIndex >= 0)
            {
                double val = 1.0;
                switch (CmbMapRenderScale.SelectedIndex)
                {
                    case 0: val = 0.5; break;
                    case 1: val = 0.75; break;
                    case 2: val = 1.0; break;
                    case 3: val = 1.25; break;
                    case 4: val = 1.5; break;
                    case 5: val = 2.0; break;
                }
                TrackingService.MapRenderScale = val;
            }
            TrackingService.MapUseAliasedEdgeMode = ChkMapUseAliasedEdgeMode.IsChecked == true;

            // Save Cloud Sync setting
            if (sender == ChkCloudSync)
            {
                if (ChkCloudSync.IsChecked == true)
                {
                    if (ParentWindow != null)
                    {
                        var dlg = new CloudDisclaimerWindow { Owner = ParentWindow };
                        dlg.ShowDialog();
                        if (dlg.CloudSyncAccepted)
                        {
                            TrackingService.CloudSyncEnabled = true;
                            TrackingService.UploadConsentGiven = true;
                            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
                        }
                        else
                        {
                            _isSettingsInitialized = false;
                            ChkCloudSync.IsChecked = false;
                            _isSettingsInitialized = true;
                            TrackingService.CloudSyncEnabled = false;
                            TrackingService.UploadConsentGiven = false;
                            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                        }
                    }
                    else
                    {
                        TrackingService.CloudSyncEnabled = true;
                        TrackingService.UploadConsentGiven = true;
                        _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
                    }
                }
                else
                {
                    TrackingService.CloudSyncEnabled = false;
                    TrackingService.UploadConsentGiven = false;
                    _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                }
            }
            else
            {
                TrackingService.CloudSyncEnabled = ChkCloudSync.IsChecked == true;
            }

            TrackingService.OfflineDeathAlertsEnabled = ChkOfflineDeathAlerts.IsChecked == true;
            TrackingService.OfflineDeathSoundLoopEnabled = ChkOfflineDeathSoundLoop.IsChecked == true;
            TrackingService.OfflineDeathDiscordEnabled = ChkOfflineDeathDiscord.IsChecked == true;

            // Notification Center Settings
            TrackingService.NotificationsToastEnabled = ChkNotificationsToast.IsChecked == true;
            TrackingService.NotificationsSoundsEnabled = ChkNotificationsSounds.IsChecked == true;
            TrackingService.NotificationsRetentionDays = (int)SliderNotificationsRetention.Value;
            TxtRetentionDays.Text = string.Format(T("NotificationRetentionDays", "{0} days"), (int)SliderNotificationsRetention.Value);
            PopulateMutedServers();

            ParentWindow?.ApplySettings();
            ParentWindow?.UpdateCloudSyncUI();
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
        }

        private void OnMarkerSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.MapShowSteamMarkers  = ChkShowProfileMarkers.IsChecked == true;
            TrackingService.MapShowPlayerArrows  = ChkShowPlayerArrows.IsChecked == true;
            TrackingService.MapShowDeathTags     = ChkShowDeathMarkers.IsChecked == true;
            TrackingService.MapAbbreviateNames   = ChkStreamerModeMarkers.IsChecked == true;
            TrackingService.MapPlayerIconScale   = SliderPlayerIconScaleOverlay.Value;

            ParentWindow?.SyncPlayerSettingsFromTrackingService();
        }


        private void BtnShowResetDialog_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;
            var dialog = new ResetDataWindow { Owner = ParentWindow };
            if (dialog.ShowDialog() == true)
            {
                _ = ParentWindow.PerformGranularResetAsync(
                    dialog.ResetConnection,
                    dialog.ResetProfiles,
                    dialog.ResetSteam,
                    dialog.ResetPairing,
                    dialog.ResetCrosshairs,
                    dialog.ResetCache
                );
            }
        }
      
        private void BtnDelete3DMapData_Click(object sender, RoutedEventArgs e)
        {
            var owner = ParentWindow ?? Window.GetWindow(this);
            var result = MessageBox.Show(
                "Delete all cached 3D map data for every server? This removes parsed map files and generated viewer JSON, but keeps app assets and icons.",
                "Delete 3D Map Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var deleted = Map3DLocalBuildService.DeleteAllCachedMapData();
            ParentWindow?.ResetBuildingBlockedZonesAfterCacheDelete();
            ParentWindow?.AppendLog($"[3D Map] Deleted cached 3D map data ({deleted.DeletedFiles} files, {deleted.DeletedDirectories} folders). Generated data will be rebuilt when needed.");
            MessageBox.Show(owner, "Cached 3D map data deleted. It will be rebuilt when you open a 3D map again.", "3D Map Data", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnManuallyParseMap_Click(object sender, RoutedEventArgs e)
        {
            ParentWindow?.ManuallyImportMapFile();
        }
        private void BtnBackupData_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;

            var dialog = new BackupPasswordDialog { Owner = ParentWindow };
            dialog.SetMode(false); // Encryption mode

            if (dialog.ShowDialog() == true)
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "ZIP Archives (*.zip)|*.zip",
                    FileName = "RustPlusDesk_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip",
                    Title = Properties.Resources.BackupApplicationDataTitle
                };

                if (sfd.ShowDialog() == true)
                {
                    try
                    {
                        RustPlusDesk.Services.Data.BackupDataModule.CreateBackup(sfd.FileName, dialog.Password);
                        ParentWindow.AppendLog(string.Format(Properties.Resources.BackupSuccessLog, sfd.FileName));
                        MessageBox.Show(Properties.Resources.BackupSuccessMessage, Properties.Resources.BackupSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        ParentWindow.AppendLog(string.Format(Properties.Resources.BackupErrorLog, ex.Message));
                        MessageBox.Show(string.Format(Properties.Resources.BackupErrorMessage, ex.Message), Properties.Resources.BackupFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnRestoreData_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;

            var ask = MessageBox.Show(
                Properties.Resources.RestoreConfirmMessage,
                Properties.Resources.RestoreConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ZIP Archives (*.zip)|*.zip",
                Title = Properties.Resources.RestoreApplicationDataTitle
            };

            if (ofd.ShowDialog() == true)
            {
                string password = "";
                if (RustPlusDesk.Services.Data.BackupDataModule.IsBackupEncrypted(ofd.FileName))
                {
                    var dialog = new BackupPasswordDialog { Owner = ParentWindow };
                    dialog.SetMode(true); // Decryption mode

                    if (dialog.ShowDialog() == true)
                    {
                        password = dialog.Password;
                    }
                    else
                    {
                        // User canceled decryption prompt, abort restore
                        return;
                    }
                }

                try
                {
                    RustPlusDesk.Services.Data.BackupDataModule.RestoreBackup(ofd.FileName, password);
                    ParentWindow.ReloadApplicationData();
                    MessageBox.Show(Properties.Resources.RestoreSuccessMessage, Properties.Resources.RestoreSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    ParentWindow.AppendLog(Properties.Resources.RestorePasswordErrorLog);
                    MessageBox.Show(Properties.Resources.RestorePasswordErrorMessage, Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    ParentWindow.AppendLog(string.Format(Properties.Resources.RestoreErrorLog, ex.Message));
                    MessageBox.Show(string.Format(Properties.Resources.RestoreErrorMessage, ex.Message), Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCompareCloud_Click(object sender, RoutedEventArgs e)
        {
            var cloudWindow = new RustPlusDesk.Views.Windows.CloudFeaturesWindow();
            cloudWindow.Owner = ParentWindow ?? Window.GetWindow(this);
            cloudWindow.ShowDialog();
        }

        public void BringCloudAccountIntoView() => CloudSettingsAnchor.BringIntoView();

        private async void BtnDiscordConnect_Click(object sender, RoutedEventArgs e)
        {
            BtnDiscordConnect.IsEnabled = false;
            BtnEmailConnect.IsEnabled = false;

            if (Services.Auth.SupabaseAuthManager.IsDiscordAuthenticated)
            {
                // Disconnect Discord
                TxtDiscordBtnLabel.Text = T("AuthDisconnectingStatus", "Disconnecting...");
                await Services.Auth.SupabaseAuthManager.LogoutAsync();
                ParentWindow?.AppendLog("[Cloud] Discord disconnected.");
            }
            else
            {
                // Connect Discord
                ParentWindow?.AppendLog("[Cloud] Starting Discord OAuth login...");
                TxtDiscordBtnLabel.Text = T("AuthConnectingStatus", "Connecting...");
                bool success = await Services.Auth.SupabaseAuthManager.LoginWithDiscordAsync();

                if (success)
                {
                    ParentWindow?.AppendLog("[Cloud] Discord connected. Syncing roles...");
                    var tier = Services.Auth.SupabaseAuthManager.CurrentTier;
                    ParentWindow?.AppendLog($"[Cloud] Rollen-Sync abgeschlossen. Tier: {tier.ToUpper()}");
                }
                else
                {
                    ParentWindow?.AppendLog("[Cloud] Discord login failed or canceled.");
                }
            }

            BtnDiscordConnect.IsEnabled = true;
            BtnEmailConnect.IsEnabled = true;
            LoadSettings();
            ParentWindow?.UpdateCloudSyncUI();
        }

        private void BtnEmailConnect_Click(object sender, RoutedEventArgs e)
        {
            // If email-authenticated, offer logout
            if (Services.Auth.SupabaseAuthManager.IsEmailAuthenticated)
            {
                var result = System.Windows.MessageBox.Show(
                    T("EmailLogoutConfirmMessage", "Sign out of the email account?"),
                    T("CloudAccountTitle", "Cloud Account"),
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _ = Services.Auth.SupabaseAuthManager.LogoutAsync();
                    ParentWindow?.AppendLog("[Cloud] Email account signed out.");
                    LoadSettings();
                    ParentWindow?.UpdateCloudSyncUI();
                }
                return;
            }

            // Open email login window
            var win = new Views.Windows.EmailLoginWindow { Owner = ParentWindow };
            if (win.ShowDialog() == true && win.LoginSuccessful)
            {
                ParentWindow?.AppendLog("[Cloud] Email login successful.");
                LoadSettings();
                ParentWindow?.UpdateCloudSyncUI();
            }
        }

        private void BtnModifyChatAlerts_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
            ParentWindow?.OpenChatAlertsFromSettings();
        }

        private void BtnChatCommands_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
            ParentWindow?.OpenChatCommandsFromSettings();
        }

        private void PremiumFeature_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!Services.Auth.SupabaseAuthManager.IsPremium)
            {
                e.Handled = true;
                if (ParentWindow != null)
                {
                    var win = new Views.Windows.PremiumInfoWindow("") { Owner = ParentWindow };
                    win.ShowDialog();
                }
            }
        }

        private void TxtSettingsDiscordWebhook_TextChanged(object sender, TextChangedEventArgs e)
        {
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            if (vm?.Selected != null && ParentWindow != null)
            {
                ParentWindow.SyncAlertMenuItems();
            }
        }

        private void BtnClearSettingsWebhook_Click(object sender, RoutedEventArgs e)
        {
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            if (vm?.Selected != null && ParentWindow != null)
            {
                vm.Selected.DiscordWebhookChatAlertsUrl = string.Empty;
                ParentWindow.SyncAlertMenuItems();
            }
        }

        private void BtnDiscordWebhookHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async void BtnFcmHelp_Click(object sender, RoutedEventArgs e)
        {
            var msg = "With Webhooks, we can automatically send FCM notifications (Offline Death and Raid Alerts) to Discord or other Smart Home solutions like IFTTT to e.g. trigger smart lights or be called when a raid happens.";
            var msgBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Offline Cloud Alerts",
                Content = msg,
                PrimaryButtonText = "OK"
            };
            await msgBox.ShowDialogAsync();
        }

        private async void BtnAlexaHelp_Click(object sender, RoutedEventArgs e)
        {
            var msg = "How to use Alexa Integration:\n\n" +
                      "1. Enable the 'RustPlusDesktop' Skill in your Amazon Alexa App.\n" +
                      "2. Select the server whose devices you want to control with Alexa or from which you want to receive Raid Alerts.\n" +
                      "3. Click 'Generate Login PIN'.\n" +
                      "4. Link accounts in the Alexa App by entering the PIN.\n" +
                      "5. Search for new devices in the Alexa App.\n\n" +
                      "Smart Switches and Smart Alerts will then appear in Alexa as Smart Devices. Smart Alerts are created as motion sensors in the device list of the linked server with their name. Routines can then be created for these. e.g. If triggered, announce on all Alexa devices and send a push notification and turn on my lights.\n\n" +
                      "Switches can be turned on and off via Alexa as usual, renamed and activated by their name. e.g. \"Alexa, turn on Turrets\".\n\n" +
                      "If new devices are added later, they can easily be found in Alexa via the device search. After a wipe, simply delete the old devices from the Alexa App.";
            var msgBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Alexa Smart Home",
                Content = msg,
                PrimaryButtonText = "OK"
            };
            await msgBox.ShowDialogAsync();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }

        private async void BtnSyncFcm_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as WpfUi.Button;
            if (btn != null) btn.IsEnabled = false;

            try
            {
                var consentDialog = new Windows.Dialogs.FcmConsentWindow { Owner = ParentWindow };
                if (consentDialog.ShowDialog() != true) return;

                bool success = await RustPlusDesk.Services.FcmSyncService.SyncFcmCredentialsAsync();
                if (success)
                {
                    if (btn != null)
                    {
                        btn.Content = "Synced!";
                        btn.Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.Checkmark24 };
                        btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                    }
                }
                else
                {
                    MessageBox.Show("Failed to sync FCM connection. Ensure you are logged in, have an active Premium/Supporter tier, and your connection in Rust+ Companion is active.", "Cloud Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void BtnRevokeFcm_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as WpfUi.Button;
            if (btn != null) btn.IsEnabled = false;

            try
            {
                bool success = await RustPlusDesk.Services.FcmSyncService.RevokeFcmCredentialsAsync();
                if (success)
                {
                    BtnSyncFcm.Content = "Sync Cloud Connection";
                    BtnSyncFcm.Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.CloudArrowUp24 };
                    BtnSyncFcm.ClearValue(WpfUi.Button.ForegroundProperty);
                    MessageBox.Show("Cloud access has been revoked and your credentials have been deleted from the cloud.", "Access Revoked", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to revoke FCM connection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private void BtnInviteDiscordBot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.com/oauth2/authorize?client_id=1511865399971545199&permissions=39584569350144&integration_type=0&scope=bot+applications.commands",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Failed to open invite link: {ex.Message}");
            }
        }

        private async void BtnSaveDiscordGuild_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            var steamId = vm?.SteamId64;
            if (string.IsNullOrEmpty(steamId)) return;

            var guildId = TxtDiscordGuildId.Text?.Trim();
            if (string.IsNullOrEmpty(guildId))
            {
                try
                {
                    BtnSaveDiscordGuild.IsEnabled = false;
                    
                    var queryParams = new Dictionary<string, string> { ["owner_steam_id"] = steamId };
                    var body = await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/settings", HttpMethod.Get, null, queryParams);
                    var list = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordBotSettingsModel>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    var guildSetting = list?.FirstOrDefault();
                    if (guildSetting != null && !string.IsNullOrEmpty(guildSetting.GuildId))
                    {
                        var delParams = new Dictionary<string, string> { ["guild_id"] = guildSetting.GuildId };
                        await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/settings", HttpMethod.Delete, null, delParams);
                    }
                    
                    MessageBox.Show("Discord Server unlinked successfully. The bot will no longer interact with your server.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    _ = LoadDiscordBotSettingsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to unlink Discord Server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    BtnSaveDiscordGuild.IsEnabled = true;
                }
                return;
            }

            try
            {
                BtnSaveDiscordGuild.IsEnabled = false;

                var payload = new
                {
                    guild_id = guildId,
                    owner_steam_id = steamId,
                    commands_enabled = ChkDiscordCommandsEnabled.IsChecked != false,
                    allowed_command_role_ids = NormalizeDiscordRoleIds(TxtDiscordAllowedRoleIds.Text)
                };

                var resultStr = await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/settings", HttpMethod.Post, payload);
                var resultList = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordBotRegistrationResult>>(resultStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var registration = resultList?.FirstOrDefault();
                if (registration == null || !registration.Success)
                {
                    MessageBox.Show(registration?.Message ?? "Failed to link Discord Server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show("Discord Server linked successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                _ = LoadDiscordBotSettingsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to link Discord Server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSaveDiscordGuild.IsEnabled = true;
            }
        }

        private async void BtnSaveChannels_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            var guildId = TxtDiscordGuildId.Text?.Trim();
            if (string.IsNullOrEmpty(guildId))
            {
                MessageBox.Show("Please save a Discord Server ID first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                BtnSaveChannels.IsEnabled = false;

                await SaveDiscordCommandPermissionsAsync(guildId);

                var queryParams = new Dictionary<string, string> { ["guild_id"] = guildId };
                var body = await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/settings", HttpMethod.Get, null, queryParams);
                
                List<RustPlusDesk.Models.DiscordChannelsConfigModel> existingList = new();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("discord_channels_config", out var configEl) && configEl.ValueKind == JsonValueKind.Array)
                {
                    existingList = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordChannelsConfigModel>>(configEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }

                async Task SaveChannelAsync(string type, string channelId, bool tts, string mentionText)
                {
                    if (string.IsNullOrWhiteSpace(channelId))
                    {
                        var modelToDelete = existingList.FirstOrDefault(c => c.NotificationType == type);
                        if (modelToDelete != null)
                        {
                            var delParams = new Dictionary<string, string>
                            {
                                ["guild_id"] = guildId,
                                ["notification_type"] = type
                            };
                            await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/channels", HttpMethod.Delete, null, delParams);
                        }
                        return;
                    }

                    var payload = new
                    {
                        guild_id = guildId,
                        notification_type = type,
                        channel_id = channelId.Trim(),
                        mention_text = (mentionText ?? "").Trim(),
                        tts_enabled = tts,
                        audio_alert_enabled = false
                    };

                    await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/channels", HttpMethod.Post, payload);
                }

                await SaveChannelAsync("raid", TxtChannelRaid.Text, ChkRaidTTS.IsChecked == true, GetMentionFromCheckboxes(ChkChannelRaidEveryone, ChkChannelRaidHere));
                await SaveChannelAsync("events", TxtChannelEvents.Text, ChkEventsTTS.IsChecked == true, GetMentionFromCheckboxes(ChkChannelEventsEveryone, ChkChannelEventsHere));
                await SaveChannelAsync("chat", TxtChannelChat.Text, ChkChatTTS.IsChecked == true, GetMentionFromCheckboxes(ChkChannelChatEveryone, ChkChannelChatHere));
                await SaveChannelAsync("shop", TxtChannelShop.Text, ChkShopTTS.IsChecked == true, GetMentionFromCheckboxes(ChkChannelShopEveryone, ChkChannelShopHere));

                MessageBox.Show("Channels configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save channels: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSaveChannels.IsEnabled = true;
            }
        }

        private string GetMentionFromCheckboxes(System.Windows.Controls.CheckBox everyone, System.Windows.Controls.CheckBox here)
        {
            var list = new List<string>();
            if (everyone?.IsChecked == true) list.Add("@everyone");
            if (here?.IsChecked == true) list.Add("@here");
            return string.Join(" ", list);
        }

        private async Task LoadDiscordBotSettingsAsync()
        {
            if (Services.Auth.SupabaseAuthManager.Client == null || !Services.Auth.SupabaseAuthManager.IsPremium) return;

            try
            {
                var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
                var steamId = vm?.SteamId64;
                if (string.IsNullOrEmpty(steamId)) return;

                var queryParams = new Dictionary<string, string> { ["owner_steam_id"] = steamId };
                var body = await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/settings", HttpMethod.Get, null, queryParams);
                var list = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordBotSettingsModel>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var guildSetting = list?.FirstOrDefault();

                if (guildSetting != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtDiscordGuildId.Text = guildSetting.GuildId;
                        ChkDiscordCommandsEnabled.IsChecked = guildSetting.CommandsEnabled;
                        TxtDiscordAllowedRoleIds.Text = guildSetting.AllowedCommandRoleIds ?? string.Empty;
                    });

                    List<RustPlusDesk.Models.DiscordChannelsConfigModel> channelsList = new();
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        var firstRow = root[0];
                        if (firstRow.TryGetProperty("discord_channels_config", out var configEl) && configEl.ValueKind == JsonValueKind.Array)
                        {
                            channelsList = JsonSerializer.Deserialize<List<RustPlusDesk.Models.DiscordChannelsConfigModel>>(configEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        TxtChannelRaid.Text = string.Empty;
                        ChkChannelRaidEveryone.IsChecked = false;
                        ChkChannelRaidHere.IsChecked = false;
                        ChkRaidTTS.IsChecked = false;
                        
                        TxtChannelEvents.Text = string.Empty;
                        ChkChannelEventsEveryone.IsChecked = false;
                        ChkChannelEventsHere.IsChecked = false;
                        ChkEventsTTS.IsChecked = false;
                        
                        TxtChannelChat.Text = string.Empty;
                        ChkChannelChatEveryone.IsChecked = false;
                        ChkChannelChatHere.IsChecked = false;
                        ChkChatTTS.IsChecked = false;
                        
                        TxtChannelShop.Text = string.Empty;
                        ChkChannelShopEveryone.IsChecked = false;
                        ChkChannelShopHere.IsChecked = false;
                        ChkShopTTS.IsChecked = false;

                        foreach (var ch in channelsList)
                        {
                            switch (ch.NotificationType)
                            {
                                case "raid":
                                    TxtChannelRaid.Text = ch.ChannelId;
                                    ChkChannelRaidEveryone.IsChecked = ch.MentionText?.Contains("@everyone") ?? false;
                                    ChkChannelRaidHere.IsChecked = ch.MentionText?.Contains("@here") ?? false;
                                    ChkRaidTTS.IsChecked = ch.TtsEnabled;
                                    break;
                                case "events":
                                    TxtChannelEvents.Text = ch.ChannelId;
                                    ChkChannelEventsEveryone.IsChecked = ch.MentionText?.Contains("@everyone") ?? false;
                                    ChkChannelEventsHere.IsChecked = ch.MentionText?.Contains("@here") ?? false;
                                    ChkEventsTTS.IsChecked = ch.TtsEnabled;
                                    break;
                                case "chat":
                                    TxtChannelChat.Text = ch.ChannelId;
                                    ChkChannelChatEveryone.IsChecked = ch.MentionText?.Contains("@everyone") ?? false;
                                    ChkChannelChatHere.IsChecked = ch.MentionText?.Contains("@here") ?? false;
                                    ChkChatTTS.IsChecked = ch.TtsEnabled;
                                    break;
                                case "shop":
                                    TxtChannelShop.Text = ch.ChannelId;
                                    ChkChannelShopEveryone.IsChecked = ch.MentionText?.Contains("@everyone") ?? false;
                                    ChkChannelShopHere.IsChecked = ch.MentionText?.Contains("@here") ?? false;
                                    ChkShopTTS.IsChecked = ch.TtsEnabled;
                                    break;
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Error loading settings: {ex.Message}");
            }
        }

        private async Task SaveDiscordCommandPermissionsAsync(string guildId)
        {
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            var steamId = vm?.SteamId64;
            if (string.IsNullOrWhiteSpace(steamId)) return;

            var payload = new
            {
                guild_id = guildId,
                owner_steam_id = steamId,
                commands_enabled = ChkDiscordCommandsEnabled.IsChecked != false,
                allowed_command_role_ids = NormalizeDiscordRoleIds(TxtDiscordAllowedRoleIds.Text)
            };

            await Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync("discord-bot/settings", HttpMethod.Post, payload);
        }

        private static string NormalizeDiscordRoleIds(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var ids = raw
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.All(char.IsDigit))
                .Distinct()
                .ToArray();

            return string.Join(",", ids);
        }

        private void BtnSelectOfflineDeathSound_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files (*.mp3, *.wav)|*.mp3;*.wav",
                Title = "Select Custom Death Sound"
            };
            if (ofd.ShowDialog() == true)
            {
                TrackingService.OfflineDeathSoundPath = ofd.FileName;
                TxtOfflineDeathSoundPath.Text = System.IO.Path.GetFileName(ofd.FileName);
            }
        }

        private void BtnResetOfflineDeathSound_Click(object sender, RoutedEventArgs e)
        {
            TrackingService.OfflineDeathSoundPath = string.Empty;
            TxtOfflineDeathSoundPath.Text = Properties.Resources.DefaultSoundLabel;
        }

        private void BtnOpenOfflineDeathsLog_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;
            var win = new Windows.OfflineDeathsHistoryWindow { Owner = ParentWindow };
            win.ShowDialog();
        }

        private void PopulateMutedServers()
        {
            if (PnlMutedServers == null) return;
            PnlMutedServers.Children.Clear();

            var muted = TrackingService.MutedNotificationServers;
            if (muted == null || muted.Count == 0)
            {
                PnlMutedServers.Children.Add(new TextBlock
                {
                    Text = "No servers muted",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(4)
                });
                return;
            }

            foreach (var serverKey in muted)
            {
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new TextBlock
                {
                    Text = serverKey,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11
                };
                Grid.SetColumn(txt, 0);
                grid.Children.Add(txt);

                var btn = new Button
                {
                    Content = "Unmute",
                    Height = 20,
                    Padding = new Thickness(6, 1, 6, 1),
                    FontSize = 10,
                    Tag = serverKey,
                    Style = FindResource("GhostButton") as Style
                };
                btn.Click += (s, e) =>
                {
                    if (s is Button b && b.Tag is string key)
                    {
                        var parts = key.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                        {
                            TrackingService.UnmuteServer(parts[0], port);
                            PopulateMutedServers();
                        }
                    }
                };
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);

                PnlMutedServers.Children.Add(grid);
            }
        }

      
        private void TxtDiscordWebhookUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            TrackingService.DiscordWebhookUrl = TxtDiscordWebhookUrl.Text;
        }

        private void ChkFcmMention_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            TrackingService.DiscordWebhookMention = GetMentionFromCheckboxes(ChkFcmMentionEveryone, ChkFcmMentionHere);
        }

        private void TxtSmartHomeWebhookUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            TrackingService.SmartHomeWebhookUrl = TxtSmartHomeWebhookUrl.Text;
        }

        private async void BtnGenerateTelegramUrl_Click(object sender, RoutedEventArgs e)
        {
            var user = TxtTelegramUser.Text?.Trim() ?? "";
            if (!user.StartsWith("@")) user = "@" + user;
            if (string.IsNullOrWhiteSpace(user) || user == "@")
            {
                MessageBox.Show("Please enter a valid Telegram username.", "Invalid Username", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var msg = TxtTelegramMsg.Text?.Trim() ?? "";
            var lang = (CmbTelegramLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "de-DE-Standard-A";

            if (ChkTelegramIncTitle.IsChecked == true) msg = "{{title}} " + msg;
            if (ChkTelegramIncMsg.IsChecked == true) msg += " {{message}}";
            if (ChkTelegramIncType.IsChecked == true) msg += " {{type}}";

            var encodedMsg = Uri.EscapeDataString(msg).Replace("%20", "+");
            var url = $"http://api.callmebot.com/start.php?user={user}&text={encodedMsg}&lang={lang}";

            TxtGeneratedTelegramUrl.Text = url;
            TrackingService.TelegramCallUser = user;
            TrackingService.TelegramCallMsg = TxtTelegramMsg.Text?.Trim() ?? "";
            TrackingService.TelegramCallLang = lang;
            TrackingService.TelegramCallIncTitle = ChkTelegramIncTitle.IsChecked == true;
            TrackingService.TelegramCallIncMsg = ChkTelegramIncMsg.IsChecked == true;
            TrackingService.TelegramCallIncType = ChkTelegramIncType.IsChecked == true;
            TrackingService.TelegramCallWebhookUrl = url;

            TxtGeneratedTelegramUrl.Visibility = Visibility.Visible;
            BtnTestTelegramUrl.Visibility = Visibility.Visible;
            BtnRevokeTelegramUrl.Visibility = Visibility.Visible;

            // Trigger FCM Sync directly to save
            var consentDialog = new Windows.Dialogs.FcmConsentWindow { Owner = ParentWindow };
            if (consentDialog.ShowDialog() != true) return;

            bool success = await RustPlusDesk.Services.FcmSyncService.SyncFcmCredentialsAsync();
            if (success)
            {
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Success",
                    Content = "Telegram Call URL generated and synced to the cloud worker successfully!",
                    PrimaryButtonText = "OK"
                };
                await msgBox.ShowDialogAsync();
            }
            else
            {
                MessageBox.Show("Failed to sync FCM connection. Please ensure you are logged in.", "Cloud Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnTestTelegramUrl_Click(object sender, RoutedEventArgs e)
        {
            var url = TxtGeneratedTelegramUrl.Text;
            if (!string.IsNullOrEmpty(url))
            {
                // Replace placeholders for the test call so the user actually hears something valid
                var testUrl = url.Replace("%7B%7Btitle%7D%7D", "Test+Alarm")
                                 .Replace("%7B%7Bmessage%7D%7D", "Test+Message")
                                 .Replace("%7B%7Btype%7D%7D", "alarm");
                                 
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = testUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnRevokeTelegramUrl_Click(object sender, RoutedEventArgs e)
        {
            TrackingService.TelegramCallWebhookUrl = "";
            TxtGeneratedTelegramUrl.Text = "";
            
            TxtGeneratedTelegramUrl.Visibility = Visibility.Collapsed;
            BtnTestTelegramUrl.Visibility = Visibility.Collapsed;
            BtnRevokeTelegramUrl.Visibility = Visibility.Collapsed;

            await RustPlusDesk.Services.FcmSyncService.SyncFcmCredentialsAsync();
        }

        private void PopulateAlexaServers()
        {
            CmbAlexaServer.Items.Clear();
            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            if (vm?.Servers != null)
            {
                foreach (var s in vm.Servers)
                {
                    if (!string.IsNullOrEmpty(s.Host) && s.Port > 0)
                    {
                        CmbAlexaServer.Items.Add(new ComboBoxItem
                        {
                            Content = string.IsNullOrEmpty(s.Name) ? $"{s.Host}:{s.Port}" : $"{s.Name} ({s.Host}:{s.Port})",
                            Tag = $"{s.Host}-{s.Port}"
                        });
                    }
                }
            }
        }

        private async Task LoadAlexaSettingsAsync()
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            var user = Services.Auth.SupabaseAuthManager.Client.Auth.CurrentUser;
            if (user == null) return;

            try
            {
                var response = await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.UserAlexaSettingsModel>()
                    .Where(x => x.UserId == user.Id)
                    .Single();

                if (response != null && !string.IsNullOrEmpty(response.ActiveServerKey))
                {
                    foreach (ComboBoxItem item in CmbAlexaServer.Items)
                    {
                        if (item.Tag?.ToString() == response.ActiveServerKey)
                        {
                            CmbAlexaServer.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignored, might not exist yet
            }
        }

        private async void BtnGenerateAlexaPIN_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null)
            {
                MessageBox.Show("Please connect your Cloud Account first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var vm = RustPlusDesk.App.Current.MainWindow.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            var steamId = vm?.SteamId64;
            if (string.IsNullOrEmpty(steamId))
            {
                MessageBox.Show("Steam ID not found. Please connect to a server first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnGenerateAlexaPIN.IsEnabled = false;
            try
            {
                var random = new Random();
                string pin = random.Next(100000, 999999).ToString();

                var response = await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.UserFcmCredentialsModel>().Where(x => x.SteamId == steamId).Single();
                if (response == null)
                {
                    MessageBox.Show("Please enable Cloud Sync first before generating an Alexa PIN.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    BtnGenerateAlexaPIN.IsEnabled = true;
                    return;
                }

                var fcmConfig = response.FcmConfig ?? new Newtonsoft.Json.Linq.JObject();
                fcmConfig["alexa_pin"] = pin;
                fcmConfig["alexa_pin_expires"] = DateTime.UtcNow.AddMinutes(15).ToString("O");
                response.FcmConfig = fcmConfig;
                
                await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.UserFcmCredentialsModel>().Upsert(response);

                TxtAlexaPIN.Text = pin;
                TxtAlexaPIN.Visibility = Visibility.Visible;
                BtnGenerateAlexaPIN.Content = "PIN Generated (valid for 15m)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate PIN: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnGenerateAlexaPIN.IsEnabled = true;
            }
        }

        private async void BtnLinkAlexa_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null)
            {
                MessageBox.Show("Please connect your Cloud Account first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var selected = CmbAlexaServer.SelectedItem as ComboBoxItem;
            var serverKey = selected?.Tag?.ToString();
            if (string.IsNullOrEmpty(serverKey))
            {
                MessageBox.Show("Please select a server first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
            var steamId = vm?.SteamId64;
            if (string.IsNullOrEmpty(steamId))
            {
                MessageBox.Show("Steam ID not found. Please connect to a server first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = Services.Auth.SupabaseAuthManager.Client.Auth.CurrentUser;
            if (user == null) return;

            var consentDialog = new Windows.Dialogs.FcmConsentWindow { Owner = ParentWindow };
            if (consentDialog.ShowDialog() != true) return;

            BtnLinkAlexa.IsEnabled = false;
            try
            {
                bool syncSuccess = await RustPlusDesk.Services.FcmSyncService.SyncFcmCredentialsAsync();
                if (!syncSuccess)
                {
                    var msgBox = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Cloud Sync Failed",
                        Content = "Failed to sync FCM connection. Ensure you are logged in, have an active Premium/Supporter tier, and your connection in Rust+ Companion is active.",
                        PrimaryButtonText = "OK"
                    };
                    await msgBox.ShowDialogAsync();
                    return;
                }

                var serverProfile = vm.Servers.FirstOrDefault(s => $"{s.Host}-{s.Port}" == serverKey);
                if (serverProfile != null)
                {
                    // 1. Link Alexa active server
                    var alexaModel = new RustPlusDesk.Models.UserAlexaSettingsModel
                    {
                        UserId = user.Id,
                        ActiveServerKey = serverKey,
                        SteamId = steamId,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.UserAlexaSettingsModel>().Upsert(alexaModel);

                    // 2. Upload Server Credentials for Cloud Worker
                    var serverCredsModel = new RustPlusDesk.Models.UserServerModel
                    {
                        UserId = user.Id,
                        SteamId = steamId,
                        ServerIp = serverProfile.Host,
                        ServerPort = serverProfile.Port,
                        PlayerToken = serverProfile.PlayerToken,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.UserServerModel>().Upsert(serverCredsModel);

                    // 3. Force Sync Devices for Alexa Discovery
                    if (ulong.TryParse(steamId, out var steamIdUlong))
                    {
                        // We use the same generic local overlay to append the devices
                        var currentOverlay = Services.Data.OverlayDataModule.LoadLocalOverlay(serverKey, steamIdUlong);
                        _ = Services.Data.DeviceDataModule.UploadDevicesSnapshotAsync(serverKey, steamIdUlong, serverProfile.Devices, currentOverlay, false);
                    }

                    var msgBox = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Success",
                        Content = "Alexa Server linked successfully! Alexa will now control devices from this server and receive Smart Alarms.",
                        PrimaryButtonText = "OK"
                    };
                    await msgBox.ShowDialogAsync();
                }
            }
            catch (Exception ex)
            {
                var msgBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Error",
                    Content = $"Failed to link Alexa Server: {ex.Message}",
                    PrimaryButtonText = "OK"
                };
                await msgBox.ShowDialogAsync();
            }
            finally
            {
                BtnLinkAlexa.IsEnabled = true;
            }
        }

        private async void BtnRevokeAlexa_Click(object sender, RoutedEventArgs e)
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            var user = Services.Auth.SupabaseAuthManager.Client.Auth.CurrentUser;
            if (user == null) return;

            BtnRevokeAlexa.IsEnabled = false;
            try
            {
                await Services.Auth.SupabaseAuthManager.Client.From<RustPlusDesk.Models.UserAlexaSettingsModel>()
                    .Where(x => x.UserId == user.Id)
                    .Delete();

                CmbAlexaServer.SelectedItem = null;
                MessageBox.Show("Alexa access revoked successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to revoke Alexa access: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRevokeAlexa.IsEnabled = true;
            }
        }
    }
}
