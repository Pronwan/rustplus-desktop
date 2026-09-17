using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RustPlusDesk.Views
{
    public class PatchVersionNavModel
    {
        public string Version { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Update"; // "Major", "Hotfix", "Update"
        public bool IsLatest { get; set; }
        public string TargetCardName { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }

    public partial class PatchNotesWindow : Wpf.Ui.Controls.FluentWindow
    {
        public string CurrentVersionFormatted => $"v{RustPlusDesk.Helpers.VersionHelper.GetClientVersion()}";

        private readonly List<PatchVersionNavModel> _allVersions = new();
        private readonly ObservableCollection<PatchVersionNavModel> _filteredVersions = new();
        private string _activeCategoryFilter = "All";

        public PatchNotesWindow()
        {
            Ach.Unlock(Ach.PatchNotes);
            InitializeComponent();
            InitVersionNavigation();
        }

        private void InitVersionNavigation()
        {
            _allVersions.Clear();
            _allVersions.AddRange(new[]
            {
                new PatchVersionNavModel { Version = "v10.0.0", Title = "Command Dock, AI Companion, Map Layers & Global Chat", Category = "Major", IsLatest = true, TargetCardName = "Card_v10_0_0" },
                new PatchVersionNavModel { Version = "v9.2.2",  Title = "Honest Alarm Times & Alexa Link Warning", Category = "Update", TargetCardName = "Card_v9_2_2" },
                new PatchVersionNavModel { Version = "v9.2.1",  Title = "Sidebar Folders, Drag & Drop & Bundled Icons", Category = "Update", TargetCardName = "Card_v9_2_1" },
                new PatchVersionNavModel { Version = "v9.2.0",  Title = "Tickets, Notifications & a Friendlier Chat", Category = "Major", TargetCardName = "Card_v9_2_0" },
                new PatchVersionNavModel { Version = "v9.1.1",  Title = "Responsiveness & Smarter Updates", Category = "Update", TargetCardName = "Card_v9_1_1" },
                new PatchVersionNavModel { Version = "v9.1.0",  Title = "Community Hub, Farm Routes & Clan Chat Commands", Category = "Major", TargetCardName = "Card_v9_1_0" },
                new PatchVersionNavModel { Version = "v9.0.3",  Title = "Pairing Reliability, Wipe Tracker & Splash Refresh", Category = "Hotfix", TargetCardName = "Card_v9_0_3" },
                new PatchVersionNavModel { Version = "v9.0.2",  Title = "Alexa, Discord Map & Push Notification Hotfix", Category = "Hotfix", TargetCardName = "Card_v9_0_2" },
                new PatchVersionNavModel { Version = "v9.0.1",  Title = "Discord Channels Configuration Hotfix", Category = "Hotfix", TargetCardName = "Card_v9_0_1" },
                new PatchVersionNavModel { Version = "v9.0.0",  Title = "Cloud Sync, Clan 2.0, Wipe Tracker & Genetics 2.0", Category = "Major", TargetCardName = "Card_v9_0_0" },
                new PatchVersionNavModel { Version = "v8.0.4",  Title = "Oil Rig False Alarm Hotfix", Category = "Hotfix", TargetCardName = "Card_v8_0_4" },
                new PatchVersionNavModel { Version = "v8.0.3",  Title = "Smart Alarm & Oil Rig Hotfix", Category = "Hotfix", TargetCardName = "Card_v8_0_3" },
                new PatchVersionNavModel { Version = "v8.0.2",  Title = "Clan Chat & Server Switching Hotfix", Category = "Hotfix", TargetCardName = "Card_v8_0_2" },
                new PatchVersionNavModel { Version = "v8.0.1",  Title = "Update Download Hotfix", Category = "Hotfix", TargetCardName = "Card_v8_0_1" },
                new PatchVersionNavModel { Version = "v8.0.0",  Title = "Smart Home, Automation & QoL", Category = "Major", TargetCardName = "Card_v8_0_0" },
                new PatchVersionNavModel { Version = "v7.3.3",  Title = "Discord Soft Connect Hotfix", Category = "Hotfix", TargetCardName = "Card_v7_3_3" },
                new PatchVersionNavModel { Version = "v7.3.1",  Title = "Connection Recovery & 3D View Hotfix", Category = "Hotfix", TargetCardName = "Card_v7_3_1" },
                new PatchVersionNavModel { Version = "v7.3.0",  Title = "Connection, Map Cache & 3D View", Category = "Major", TargetCardName = "Card_v7_3_0" },
                new PatchVersionNavModel { Version = "v7.2.0",  Title = "Devices UI & Recycler Calculator", Category = "Update", TargetCardName = "Card_v7_2_0" },
                new PatchVersionNavModel { Version = "v7.1.8",  Title = "Apartment Complex 3D Model", Category = "Update", TargetCardName = "Card_v7_1_8" },
                new PatchVersionNavModel { Version = "v7.1.7",  Title = "3D Map Monuments & Build Surfaces", Category = "Update", TargetCardName = "Card_v7_1_7" },
                new PatchVersionNavModel { Version = "v7.1.6",  Title = "3D Map Hotfix", Category = "Hotfix", TargetCardName = "Card_v7_1_6" },
                new PatchVersionNavModel { Version = "v7.1.5",  Title = "Velopack Update Flow & Item Lists Update", Category = "Update", TargetCardName = "Card_v7_1_5" },
                new PatchVersionNavModel { Version = "v7.1.0",  Title = "Resource Heatmaps Update", Category = "Update", TargetCardName = "Card_v7_1_0" },
                new PatchVersionNavModel { Version = "v7.0.2",  Title = "Hotfixes & Improvements", Category = "Hotfix", TargetCardName = "Card_v7_0_2" },
                new PatchVersionNavModel { Version = "v7.0.0",  Title = "3D Maps, Base Footprint Builder & Enhanced 2D Maps", Category = "Major", TargetCardName = "Card_v7_0_0" },
                new PatchVersionNavModel { Version = "v6.3.1",  Title = "Overlay Subscriptions, Bandwidth & Base Screenshot Fix", Category = "Update", TargetCardName = "Card_v6_3_1" },
                new PatchVersionNavModel { Version = "v6.3.0",  Title = "Remote Camera Controls, WPF Device List & Discord Resilience", Category = "Major", TargetCardName = "Card_v6_3_0" },
                new PatchVersionNavModel { Version = "v6.2.0",  Title = "Crosshair Memory, Hardened Setup & Polling Optimizations", Category = "Update", TargetCardName = "Card_v6_2_0" },
                new PatchVersionNavModel { Version = "v6.0.2",  Title = "Hotfix Update", Category = "Hotfix", TargetCardName = "Card_v6_0_2" },
                new PatchVersionNavModel { Version = "v6.0.1",  Title = "Discord, Timer & Raid Alert Hotfix", Category = "Hotfix", TargetCardName = "Card_v6_0_1" },
                new PatchVersionNavModel { Version = "v6.0.0",  Title = "The Cloud & Intelligence Update", Category = "Major", TargetCardName = "Card_v6_0_0" },
                new PatchVersionNavModel { Version = "v5.4.0",  Title = "Localization, UI Modernization & Storage Upkeep Update", Category = "Update", TargetCardName = "Card_v5_4_0" },
                new PatchVersionNavModel { Version = "v5.1.0",  Title = "Upkeep, Minimap & Tracking Update", Category = "Update", TargetCardName = "Card_v5_1_0" },
                new PatchVersionNavModel { Version = "v5.0.0",  Title = "The Game Changer Update", Category = "Major", TargetCardName = "Card_v5_0_0" },
                new PatchVersionNavModel { Version = "v4.5.3",  Title = "Audio Smart Alert Hotfix", Category = "Hotfix", TargetCardName = "Card_v4_5_3" },
                new PatchVersionNavModel { Version = "v4.3.1",  Title = "Bugfixes & Improvements", Category = "Hotfix", TargetCardName = "Card_v4_3_1" },
                new PatchVersionNavModel { Version = "v4.2.0",  Title = "Cargo Ship Overhaul", Category = "Update", TargetCardName = "Card_v4_2_0" },
                new PatchVersionNavModel { Version = "v4.1.4",  Title = "Granular Chat Notifications", Category = "Update", TargetCardName = "Card_v4_1_4" },
                new PatchVersionNavModel { Version = "v4.1.2",  Title = "Custom Crosshair Editor", Category = "Update", TargetCardName = "Card_v4_1_2" },
                new PatchVersionNavModel { Version = "v4.0.0",  Title = "The Map & Stability Overhaul", Category = "Major", TargetCardName = "Card_v4_0_0" },
                new PatchVersionNavModel { Version = "v3.5.4",  Title = "Player Management & Optimization", Category = "Update", TargetCardName = "Card_v3_5_4" },
                new PatchVersionNavModel { Version = "v3.5.3",  Title = "The Intelligence Update", Category = "Update", TargetCardName = "Card_v3_5_3" },
                new PatchVersionNavModel { Version = "v3.4.0",  Title = "Hierarchical Grouping & Advanced Alarms", Category = "Update", TargetCardName = "Card_v3_4_0" },
                new PatchVersionNavModel { Version = "v3.3.1",  Title = "Stability & Event Awareness", Category = "Hotfix", TargetCardName = "Card_v3_3_1" },
                new PatchVersionNavModel { Version = "v3.3.0",  Title = "Oilrig Timer & !leader Command", Category = "Update", TargetCardName = "Card_v3_3_0" },
                new PatchVersionNavModel { Version = "Legacy",  Title = "Core Features & Older Updates", Category = "Update", TargetCardName = "Card_Legacy" }
            });

            if (SidebarVersionList != null)
            {
                SidebarVersionList.ItemsSource = _filteredVersions;
            }
            ApplyFilters();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CmbCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategoryFilter?.SelectedItem is ComboBoxItem item && item.Tag is string cat)
            {
                _activeCategoryFilter = cat;
                ApplyFilters();
            }
        }

        private void ApplyFilters()
        {
            string query = TxtSearch?.Text?.Trim() ?? string.Empty;
            string[] queryTerms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            _filteredVersions.Clear();
            int visibleCardCount = 0;

            foreach (var v in _allVersions)
            {
                bool categoryMatch = _activeCategoryFilter == "All" ||
                                     (_activeCategoryFilter == "Major" && v.Category == "Major") ||
                                     (_activeCategoryFilter == "Hotfix" && v.Category == "Hotfix");

                bool textMatch = true;
                FrameworkElement? targetCard = null;
                if (!string.IsNullOrEmpty(v.TargetCardName))
                {
                    targetCard = PatchNotesContent?.FindName(v.TargetCardName) as FrameworkElement;
                }

                if (queryTerms.Length > 0)
                {
                    string cardContent = GetCardSearchableText(targetCard, v);
                    textMatch = queryTerms.All(term => cardContent.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                bool isVisible = categoryMatch && textMatch;
                if (targetCard != null)
                {
                    targetCard.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }

                if (isVisible)
                {
                    visibleCardCount++;
                    _filteredVersions.Add(v);
                }
            }

            if (PanelNoResults != null)
            {
                PanelNoResults.Visibility = visibleCardCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TxtVersionCountBadge != null)
            {
                TxtVersionCountBadge.Text = $"{visibleCardCount} {(_activeCategoryFilter == "All" && string.IsNullOrEmpty(query) ? "Releases" : "Found")}";
            }
        }

        private string GetCardSearchableText(FrameworkElement? card, PatchVersionNavModel nav)
        {
            var sb = new StringBuilder();
            sb.Append(nav.Version).Append(' ').Append(nav.Title).Append(' ').Append(nav.Category).Append(' ');

            if (card != null)
            {
                var textElements = new List<object>();
                FindTextElements(card, textElements);
                foreach (var elem in textElements)
                {
                    if (elem is TextBlock tb) sb.Append(tb.Text).Append(' ');
                    else if (elem is Run run) sb.Append(run.Text).Append(' ');
                    else if (elem is GalleryItem gi) sb.Append(gi.Description).Append(' ');
                }
            }

            return sb.ToString();
        }

        private void VersionItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string targetName)
            {
                ScrollToVersionCard(targetName);
            }
        }

        private void ScrollToVersionCard(string targetCardName)
        {
            if (PatchNotesContent == null || NotesScrollViewer == null) return;

            var targetElement = PatchNotesContent.FindName(targetCardName) as FrameworkElement;
            if (targetElement != null)
            {
                targetElement.BringIntoView();
            }
        }

        private void BtnScrollTop_Click(object sender, RoutedEventArgs e)
        {
            NotesScrollViewer?.ScrollToTop();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSearch != null)
            {
                TxtSearch.Text = string.Empty;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { /* Fehler beim Öffnen des Browsers abfangen */ }
        }

        private void TrackingLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HowToTrackWindow.Show(this.Owner ?? this);
                this.Close();
            }
            catch { }
        }

        private void BtnCompareCloud_Click(object sender, RoutedEventArgs e)
        {
            var cloudWindow = new RustPlusDesk.Views.Windows.CloudFeaturesWindow();
            cloudWindow.Owner = this;
            cloudWindow.ShowDialog();
        }

        private async void BtnTranslate_Click(object sender, RoutedEventArgs e)
        {
            if (BtnTranslate == null || TxtTranslate == null) return;

            if (!Services.TrackingService.TranslationConsentGiven)
            {
                TranslationConsentOverlay.Visibility = Visibility.Visible;
                return;
            }

            BtnTranslate.IsEnabled = false;
            TxtTranslate.Text = RustPlusDesk.Properties.Resources.GetString("CodeUiTranslating");

            try
            {
                var textElements = new List<object>();
                FindTextElements(PatchNotesContent, textElements);

                if (textElements.Count == 0)
                {
                    TxtTranslate.Text = RustPlusDesk.Properties.Resources.GetString("CodeUiNoTextFound");
                    BtnTranslate.IsEnabled = true;
                    return;
                }

                var targetLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (targetLang == "iv") targetLang = "en"; // Invariant culture fallback

                var tasks = new List<Task>();
                foreach (var element in textElements)
                {
                    string text = "";
                    if (element is TextBlock tb) text = tb.Text;
                    else if (element is Run run) text = run.Text;
                    else if (element is GalleryItem item) text = item.Description;

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    tasks.Add(Task.Run(async () =>
                    {
                        string translated = await TranslateTextAsync(text, targetLang);
                        Dispatcher.Invoke(() =>
                        {
                            if (element is TextBlock tbElem) tbElem.Text = translated;
                            else if (element is Run runElem) runElem.Text = translated;
                            else if (element is GalleryItem itemElem)
                            {
                                itemElem.Description = translated;
                                itemElem.ParentGallery?.UpdateGallery();
                            }
                        });
                    }));
                }

                await Task.WhenAll(tasks);
                TxtTranslate.Text = RustPlusDesk.Properties.Resources.GetString("CodeUiTranslated");
            }
            catch (Exception ex)
            {
                MessageBox.Show(RustPlusDesk.Properties.Resources.GetString("CodeUiTranslationFailed") + ex.Message, RustPlusDesk.Properties.Resources.GetString("ErrorPrefix"), MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTranslate.Text = RustPlusDesk.Properties.Resources.GetString("CodeUiTranslate");
                BtnTranslate.IsEnabled = true;
            }
        }

        private static async Task<string> TranslateTextAsync(string text, string targetLang)
            => (await Services.TranslationService.TranslateAsync(text, targetLang).ConfigureAwait(false)).Text;

        private void FindTextElements(DependencyObject obj, List<object> elements)
        {
            if (obj == null) return;

            if (obj is ImageGallery gallery)
            {
                foreach (var item in gallery.Items)
                {
                    if (item != null)
                    {
                        elements.Add(item);
                    }
                }
                return; // Do not search inside the gallery's visual tree
            }

            if (obj is TextBlock tb)
            {
                if (tb.Inlines.Count > 0)
                {
                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Run run) elements.Add(run);
                        else if (inline is Span span) FindTextElementsInSpan(span, elements);
                    }
                }
                else
                {
                    elements.Add(tb);
                }
                return;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                FindTextElements(VisualTreeHelper.GetChild(obj, i), elements);
            }
        }

        private void FindTextElementsInSpan(Span span, List<object> elements)
        {
            foreach (var inline in span.Inlines)
            {
                if (inline is Run run) elements.Add(run);
                else if (inline is Span innerSpan) FindTextElementsInSpan(innerSpan, elements);
            }
        }

        private void BtnAcceptConsent_Click(object sender, RoutedEventArgs e)
        {
            Services.TrackingService.TranslationConsentGiven = true;
            TranslationConsentOverlay.Visibility = Visibility.Collapsed;
            BtnTranslate_Click(sender, e);
        }

        private void BtnDeclineConsent_Click(object sender, RoutedEventArgs e)
        {
            TranslationConsentOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
