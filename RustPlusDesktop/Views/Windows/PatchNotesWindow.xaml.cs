using System;
using System.Collections.Generic;
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
    public partial class PatchNotesWindow : Window
    {
        public PatchNotesWindow()
        {
            InitializeComponent();
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

        private async void BtnTranslate_Click(object sender, RoutedEventArgs e)
        {
            if (BtnTranslate == null || TxtTranslate == null) return;

            if (!Services.TrackingService.TranslationConsentGiven)
            {
                TranslationConsentOverlay.Visibility = Visibility.Visible;
                return;
            }

            BtnTranslate.IsEnabled = false;
            TxtTranslate.Text = "Translating...";

            try
            {
                var textElements = new List<object>();
                FindTextElements(PatchNotesContent, textElements);

                if (textElements.Count == 0)
                {
                    TxtTranslate.Text = "No text found";
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
                TxtTranslate.Text = "Translated";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Translation failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTranslate.Text = "Translate";
                BtnTranslate.IsEnabled = true;
            }
        }

        private async Task<string> TranslateTextAsync(string text, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode) return text;
                
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement[0];
                var sb = new StringBuilder();
                foreach (var item in arr.EnumerateArray())
                {
                    sb.Append(item[0].GetString());
                }
                return sb.ToString();
            }
            catch
            {
                return text;
            }
        }

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
                // If it has Inlines, we should look into them instead of just the Text property
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
                return; // Don't look at visual children of TextBlock
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
