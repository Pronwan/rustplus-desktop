using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfUi = Wpf.Ui.Controls;

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
            Text = Properties.Resources.HowToTrackPlayersTitle ?? "How to Track Players",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = Properties.Resources.NativeUDPTrackingSubtitle ?? "Native UDP tracking",
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

        content.Children.Add(InfoBox(Properties.Resources.TrackingGuideEnglishOnlyNotice ?? "The tracking guide is currently only available in English, as it will undergo changes in the near future. We ask for your patience.", isNote: false));

        // 1 — Native tracking
        content.Children.Add(SectionHeader("Native UDP Tracking", "🔵"));
        content.Children.Add(BodyText(
            "Rust+ Desk uses a direct A2S (Steam Query) UDP request to the game server to track players locally. No third-party service involved. You own all collected data — nothing leaves your machine."));

        content.Children.Add(Step("1", "Click  Refresh  in the Online Players header to fetch the live player list."));
        content.Children.Add(ScreenshotCard("Tracking5.jpg",
            "The online player list with real Steam names and a Track button next to each entry."));

        content.Children.Add(Step("2", "Find the player you want to track and click Track, or type their Steam ID in the manual input field and press Track."));
        content.Children.Add(Step("3", "The player is added to the Tracked tab. From that moment the app polls the server in the background every 2 minutes and records connect / disconnect events automatically."));

        content.Children.Add(ScreenshotCard("Tracking1.jpg",
            "The Full Analysis Report — session heatmap, total playtime, and activity forecast — built entirely from locally collected data."));

        content.Children.Add(InfoBox(
            "💬  Chat Alerts: Go to Chat Alerts settings to enable in-game notifications when tracked players or groups log in or out.", isNote: true));

        // 2 — Limitations
        content.Children.Add(Divider());
        content.Children.Add(SectionHeader("Limitations & ToS", "⚠️"));
        content.Children.Add(BodyText(
            "Native UDP tracking is the ONLY method of tracking players that is officially intended and supported by Facepunch."));
        content.Children.Add(InfoBox(
            "If a server has the Facepunch Name Randomizer enabled, the player list will only show those randomized names instead of the real Steam names. You will only be able to track the randomized names in this case. BattleMetrics tracking is no longer supported in Rust+ Desk.",
            isNote: false));

        content.Children.Add(BodyText(
            "Stay Tuned for more about this topic. There's something coming..."));

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
            Text = Properties.Resources.HowToTrackCloseHint ?? "Close this window at any time — you can reopen it with the  ❓ How to Track  button.",
            Foreground = new SolidColorBrush(Color.FromRgb(110, 120, 140)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var closeBtn = new WpfUi.Button
        {
            Content = Properties.Resources.Close ?? "Close",
            Appearance = WpfUi.ControlAppearance.Secondary,
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
