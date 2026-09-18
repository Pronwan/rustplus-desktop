using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// The last few exchanges, so a failure that happened while the answer panel was switched
    /// off is still readable afterwards.
    ///
    /// It exists for the failures. "Failed" on a tile the size of a stamp is not a reason, and
    /// the reason — a retired model, a rejected key, a quota — is the one thing that tells the
    /// player what to change.
    /// </summary>
    public partial class AiHistoryWindow : Wpf.Ui.Controls.FluentWindow
    {
        public AiHistoryWindow()
        {
            InitializeComponent();

            AiHistory.Changed += Render;
            Closed += (_, __) => AiHistory.Changed -= Render;

            Render();
        }

        private void Render()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(Render);
                return;
            }

            Entries.Children.Clear();

            // Above the exchanges, because this is the question someone actually arrives with:
            // the answer came back but was read by Windows, and nothing said why. A refused
            // speech request is not a failed answer, so it has no entry of its own to live in.
            if (!string.IsNullOrEmpty(AiSpeech.LastError)) Entries.Children.Add(VoiceBanner());

            var history = AiHistory.Entries;

            TxtFooter.Text = string.Format(
                Loc.Text("AiHistoryCount", "{0} of the last {1} kept on this PC"),
                history.Count, AiHistory.MaxEntries);

            BtnClear.IsEnabled = history.Count > 0;

            if (history.Count == 0)
            {
                Entries.Children.Add(new TextBlock
                {
                    Text = Loc.Text("AiHistoryEmpty", "Nothing asked yet."),
                    Foreground = Resource("TextSubtle", Colors.Gray),
                    Margin = new Thickness(0, 8, 0, 0),
                });
                return;
            }

            foreach (var entry in history) Entries.Children.Add(Card(entry));
        }

        /// <summary>
        /// Says that the provider's voice refused, and why.
        ///
        /// The fallback to Windows works, which is exactly the problem: the answer arrives and
        /// is read aloud, so nothing looks broken and the setting appears to do nothing. This
        /// is the first place anyone looks after that happens.
        /// </summary>
        private UIElement VoiceBanner()
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = Loc.Text("AiHistoryVoiceFailedTitle",
                    "Spoken answers are coming from Windows, not the provider"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource("WarnOrangeBrush", Color.FromRgb(0xE0, 0xB3, 0x41)),
                Margin = new Thickness(0, 0, 0, 4),
            });

            stack.Children.Add(new TextBlock
            {
                Text = string.Format(
                    Loc.Text("AiHistoryVoiceFailedBody",
                        "The provider refused the last request for speech: {0}" + Environment.NewLine +
                        "The answer itself was unaffected. If the model name is the problem, " +
                        "change it under Connected Services."),
                    AiSpeech.LastError),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = Resource("TextSubtle", Colors.Gray),
            });

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = Resource("Card", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Resource("WarnOrangeBrush", Color.FromRgb(0xE0, 0xB3, 0x41)),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = stack,
            };
        }

        private UIElement Card(AiExchange entry)
        {
            var stack = new StackPanel();

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6),
            };

            header.Children.Add(new TextBlock
            {
                // Local time: the entry is being compared against what the player remembers
                // doing, and they do not remember it in UTC.
                Text = entry.WhenUtc.ToLocalTime().ToString("g"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Resource("TextPrimary", Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });

            header.Children.Add(new TextBlock
            {
                Text = $"  ·  {AiProviders.DisplayName(entry.Provider)}  ·  {entry.Model}",
                FontSize = 11,
                Foreground = Resource("TextSubtle", Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var badges = new List<string>();
            if (entry.HadScreenshot) badges.Add(Loc.Text("AiHistoryWithScreenshot", "screenshot"));
            if (entry.HadGameAudio) badges.Add(Loc.Text("AiHistoryWithGameAudio", "game audio"));

            if (badges.Count > 0)
            {
                header.Children.Add(new TextBlock
                {
                    Text = "  ·  " + string.Join(", ", badges),
                    FontSize = 11,
                    Foreground = Resource("Accent", Colors.SkyBlue),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            stack.Children.Add(header);

            // Only where the recording was turned into words on this machine. Where the provider
            // heard it directly there is nothing to show, and an empty "you asked:" would read
            // as though the question had been lost.
            if (!string.IsNullOrWhiteSpace(entry.Transcript))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = entry.Transcript.Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = Resource("TextSubtle", Colors.Gray),
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = entry.Failed ? entry.Error ?? "" : entry.Answer,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = entry.Failed
                    ? Resource("DangerBrush", Color.FromRgb(0xE5, 0x39, 0x35))
                    : Resource("TextPrimary", Colors.White),
            });

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = Resource("Card", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderBrush = entry.Failed
                    ? Resource("DangerBrush", Color.FromRgb(0xE5, 0x39, 0x35))
                    : Resource("CardBorder", Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Child = stack,
            };

            ToolTipService.SetToolTip(card, Loc.Text("AiHistoryCopyHint", "Click to copy"));

            card.MouseLeftButtonUp += (_, __) =>
            {
                try { Clipboard.SetText(entry.Failed ? entry.Error ?? "" : entry.Answer); }
                catch { /* another process holds the clipboard */ }
            };

            return card;
        }

        private static Brush Resource(string key, Color fallback)
        {
            if (Application.Current?.TryFindResource(key) is Brush found) return found;
            return new SolidColorBrush(fallback);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e) => AiHistory.Clear();
    }
}
