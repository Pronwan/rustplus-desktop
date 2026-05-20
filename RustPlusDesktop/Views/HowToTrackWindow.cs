using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ui = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// A self-contained, scrollable "How to Track" help window.
/// Open it with <see cref="Show"/>.
/// </summary>
public static class HowToTrackWindow
{
    private static Window? _instance;

    public static void Show(Window owner)
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }

        _instance = Build(owner);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static Window Build(Window owner)
    {
        var win = new Window
        {
            Title = "How to Track Players — Rust+ Desk",
            Width = 800,
            Height = 700,
            MinWidth = 600,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = new SolidColorBrush(Color.FromRgb(20, 22, 26)),
            ResizeMode = ResizeMode.CanResizeWithGrip,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer

        // ── Header ───────────────────────────────────────────────────────────
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 33, 40)),
            Padding = new Thickness(24, 16, 24, 16),
        };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new TextBlock
        {
            Text = "❓",
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        var titleBlock = new StackPanel();
        titleBlock.Children.Add(new TextBlock
        {
            Text = "How to Track Players",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = "Two tracking methods — native UDP or BattleMetrics shortcuts",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 170)),
        });
        headerStack.Children.Add(titleBlock);
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // ── Scrollable content ───────────────────────────────────────────────
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24, 20, 24, 20),
        };
        var content = new StackPanel();
        scroll.Content = content;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // ── Sections ─────────────────────────────────────────────────────────

        // 1 — Overview
        content.Children.Add(SectionHeader("Overview", "🎯"));
        content.Children.Add(InfoBox(
            "Rust+ Desk supports two complementary tracking methods. Choose based on the server you play on:",
            isNote: false));

        content.Children.Add(TwoColumnCards(
            ("🔵  Native UDP Tracking",
             "Full local tracking with session times, online/offline alerts, and Activity Reports.\n\nRequires a server that:\n• Has Facepunch's name randomizer disabled\n• Accepts A2S / Steam Query UDP requests\n\nYou own all collected data — nothing leaves your machine."),
            ("🟡  BattleMetrics Shortcuts",
             "One-click access to a player's BattleMetrics profile to check online status manually.\n\nWorks on any server indexed by BattleMetrics — including servers with anonymized player names.\n\nNo local session data is stored. Perfect for casual tracking on large official servers.")
        ));

        // 2 — Native tracking
        content.Children.Add(Divider());
        content.Children.Add(SectionHeader("Method 1 — Native UDP Tracking", "🔵"));
        content.Children.Add(BodyText(
            "Use this method on servers that show real Steam names in the player list. The app sends a direct A2S query to the game server — no third-party service involved."));

        content.Children.Add(Step("1", "Click  Refresh  in the Online Players header to fetch the live player list."));
        content.Children.Add(ScreenshotCard("Tracking5.jpg",
            "The online player list with real Steam names and a Track button next to each entry."));

        content.Children.Add(Step("2", "Find the player you want to track and click Track, or type their Steam ID in the manual input field and press Track."));
        content.Children.Add(Step("3", "The player is added to the Tracked tab. From that moment the app polls the server in the background every 2 minutes and records connect / disconnect events automatically."));

        content.Children.Add(ScreenshotCard("Tracking1.jpg",
            "The Full Analysis Report — session heatmap, total playtime, and activity forecast — built entirely from locally collected data."));

        content.Children.Add(InfoBox(
            "💬  Chat Alerts: Go to Chat Alerts settings to enable in-game notifications when tracked players or groups log in or out.", isNote: true));

        // 3 — BM tracking
        content.Children.Add(Divider());
        content.Children.Add(SectionHeader("Method 2 — BattleMetrics Shortcuts", "🟡"));
        content.Children.Add(BodyText(
            "Use this method when the server shows randomized / anonymous names, or when you simply want a quick link to a player's public BattleMetrics profile without local data collection."));

        content.Children.Add(Step("1", "Click  Search BM  in the Online Players header. A built-in browser opens over the map and searches for the connected server on BattleMetrics automatically."));
        content.Children.Add(ScreenshotCard("Tracking2.jpg",
            "The built-in BattleMetrics browser. The current server is pre-searched at the top."));

        content.Children.Add(Step("2", "Click the matching server entry, then browse to the player you want to track and click their name."));
        content.Children.Add(ScreenshotCard("Tracking3.jpg",
            "Navigating to a player profile inside the BM browser. The URL changes to battlemetrics.com/players/…"));

        content.Children.Add(Step("3",
            "Once you are on a player profile page (URL starts with battlemetrics.com/players/…), a  TRACK PLAYER  button appears in the browser toolbar.\n\n" +
            "Optionally select (highlight) the player's name on the page first — the app will use that as the display name."));
        content.Children.Add(ScreenshotCard("Tracking4.jpg",
            "The TRACK PLAYER toolbar button appears on any player profile page. The selected text is used as the display name."));

        content.Children.Add(Step("4",
            "The player is instantly added to the Tracked tab as a BattleMetrics Shortcut.\n\n" +
            "• Tap  View on BM  to open their profile directly in your system browser\n" +
            "• No local session data is collected — BM is the source of truth for these players"));

        // 4 — Limitations
        content.Children.Add(Divider());
        content.Children.Add(SectionHeader("Limitations & ToS", "⚠️"));
        content.Children.Add(InfoBox(
            "Servers that block UDP queries AND do not expose data through BattleMetrics' RCON integration cannot be tracked with either method. For those servers, online-status checking must be done manually on the BattleMetrics website.",
            isNote: false));
        content.Children.Add(BodyText(
            "Both methods are fully compliant with BattleMetrics' Terms of Service. The BM browser integration only accesses publicly visible profile pages — no scraping, no private API calls, no automation beyond what a regular browser visit would do.\n\n" +
            "Even when native tracking is not possible, BattleMetrics Shortcuts let you organize players in groups and check their status with a single click — without keeping a RAM-hungry browser open in the background."));

        // ── Footer ───────────────────────────────────────────────────────────
        var footerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 27, 33)),
            Padding = new Thickness(24, 12, 24, 12),
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        footerGrid.Children.Add(new TextBlock
        {
            Text = "Close this window at any time — you can reopen it with the  ❓ How to Track  button.",
            Foreground = new SolidColorBrush(Color.FromRgb(110, 120, 140)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var closeBtn = new ui.Button
        {
            Content = "Close",
            Appearance = ui.ControlAppearance.Secondary,
            Padding = new Thickness(20, 6, 20, 6),
        };
        closeBtn.Click += (_, _) => win.Close();
        Grid.SetColumn(closeBtn, 1);
        footerGrid.Children.Add(closeBtn);

        footerBorder.Child = footerGrid;
        Grid.SetRow(footerBorder, 2);
        root.Children.Add(footerBorder);

        win.Content = root;
        return win;
    }

    // ─── Helper builders ────────────────────────────────────────────────────

    private static UIElement SectionHeader(string title, string emoji)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 24, 0, 12),
        };
        sp.Children.Add(new TextBlock
        {
            Text = emoji + "  " + title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
        });
        return sp;
    }

    private static UIElement BodyText(string text) =>
        new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 200, 215)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 12),
        };

    private static UIElement Step(string number, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromRgb(30, 80, 160)),
            Margin = new Thickness(0, 2, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = number,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var body = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 215, 225)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        return grid;
    }

    private static UIElement InfoBox(string text, bool isNote)
    {
        var border = new Border
        {
            Background = isNote
                ? new SolidColorBrush(Color.FromArgb(40, 88, 166, 255))
                : new SolidColorBrush(Color.FromArgb(30, 255, 200, 50)),
            BorderBrush = isNote
                ? new SolidColorBrush(Color.FromRgb(50, 100, 200))
                : new SolidColorBrush(Color.FromRgb(180, 140, 30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 8, 0, 12),
        };
        border.Child = new TextBlock
        {
            Text = text,
            Foreground = isNote
                ? new SolidColorBrush(Color.FromRgb(160, 200, 255))
                : new SolidColorBrush(Color.FromRgb(230, 210, 140)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
        };
        return border;
    }

    private static UIElement Divider() =>
        new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 55)),
            Margin = new Thickness(0, 8, 0, 0),
        };

    private static UIElement ScreenshotCard(string resourceName, string caption)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 31, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 55, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 10, 0, 16),
            Padding = new Thickness(10),
        };

        var sp = new StackPanel();

        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/Screenshots/{resourceName}");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
            };
            sp.Children.Add(img);
        }
        catch
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"[Screenshot: {resourceName}]",
                Foreground = Brushes.Gray,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }

        sp.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = new SolidColorBrush(Color.FromRgb(130, 140, 160)),
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        card.Child = sp;
        return card;
    }

    private static UIElement TwoColumnCards((string title, string body) left, (string title, string body) right)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(MakeCard(left.title, left.body, col: 0));
        grid.Children.Add(MakeCard(right.title, right.body, col: 2));
        return grid;
    }

    private static UIElement MakeCard(string title, string body, int col)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 32, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 60, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        sp.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 200)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
        });
        border.Child = sp;
        Grid.SetColumn(border, col);
        return border;
    }
}
