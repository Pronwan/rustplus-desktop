using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk
{
    /// <summary>
    /// A line to Discord without leaving the game: type, send, or push the current map view.
    ///
    /// It posts through the same path as an alert of that type, so the channel's own mention
    /// text and text-to-speech setting apply exactly as they do everywhere else — see the tile's
    /// settings, which say so rather than offering switches that would not reach the sender.
    /// </summary>
    public partial class MiniMapWindow
    {
        private FrameworkElement BuildDiscordTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            shell.Child = row;

            var logo = new Image
            {
                Width = style.Size(20),
                Height = style.Size(20),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/discord.png")); }
            catch { /* the tile still works without its logo */ }
            Grid.SetColumn(logo, 0);
            row.Children.Add(logo);

            var input = new TextBox
            {
                FontSize = style.Size(11),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                MaxLength = 1800,
            };
            Grid.SetColumn(input, 1);
            row.Children.Add(input);

            var send = IconButton("\uE724", style, Loc.Text("CommandDockDiscordSend", "Send"));
            Grid.SetColumn(send, 2);
            row.Children.Add(send);

            var map = IconButton("\uE707", style, Loc.Text("CommandDockDiscordSendMap", "Send the current map view"));
            Grid.SetColumn(map, 3);
            row.Children.Add(map);

            // The tile swallows presses so the window does not drag itself; these three have to
            // be exempt or the box cannot take focus and the buttons cannot be pressed. The box
            // is a TextBox and recognised by type; the buttons are Borders and are registered.
            KeepPresses(send, map);

            input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                _ = SendDiscordLine(tile, input, send);
            };

            send.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = SendDiscordLine(tile, input, send); };
            map.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = SendDiscordMap(tile, map); };

            // Asked once per rebuild and answered from a cache; the result decides whether the
            // tile is usable at all.
            _ = RefreshDiscordChannels(tile);

            _tileRefreshers.Add(() =>
            {
                bool premium = SupabaseAuthManager.IsPremium;
                bool configured = _discordChannels.Count > 0;
                bool usable = premium && configured;

                shell.Opacity = usable ? 1.0 : 0.45;
                input.IsEnabled = usable;
                send.Cursor = usable ? Cursors.Hand : Cursors.No;
                map.Cursor = send.Cursor;

                ToolTipService.SetToolTip(shell,
                    !premium ? Loc.Text("CommandDockSupporterOnly",
                        "A supporter feature. Sign in with a supporter account to use this tile.")
                    : !configured ? Loc.Text("CommandDockDiscordNoChannels",
                        "No Discord channel is configured. Set one up under Connected Services first.")
                    : string.Format(Loc.Text("CommandDockDiscordSendsTo", "Sends to your {0} channel"),
                        ChannelLabel(EffectiveChannel(tile))));
            });

            return shell;
        }

        private readonly List<string> _discordChannels = new();

        private async System.Threading.Tasks.Task RefreshDiscordChannels(CommandDockTile tile)
        {
            if (DockHost == null) return;

            try
            {
                var channels = await DockHost.GetDockDiscordChannelsAsync();
                _discordChannels.Clear();
                _discordChannels.AddRange(channels);

                // A tile pointed at a channel that has since been removed falls back rather than
                // failing silently on the next send.
                if (tile.DiscordChannel != null && !_discordChannels.Contains(tile.DiscordChannel))
                    tile.DiscordChannel = null;
            }
            catch { /* the tile greys itself out on an empty list */ }
        }

        /// <summary>The tile's channel, or the first configured one when it has not picked.</summary>
        private string? EffectiveChannel(CommandDockTile tile)
            => tile.DiscordChannel ?? _discordChannels.FirstOrDefault();

        private static string ChannelLabel(string? type) => type switch
        {
            "chat" => Loc.Text("CommandDockDiscordChat", "chat"),
            "events" => Loc.Text("CommandDockDiscordEvents", "events"),
            "shop" => Loc.Text("CommandDockDiscordShop", "shop"),
            null => "—",
            _ => type,
        };

        private async System.Threading.Tasks.Task SendDiscordLine(CommandDockTile tile, TextBox input, Border button)
        {
            var channel = EffectiveChannel(tile);
            if (channel == null || DockHost == null) return;
            if (!SupabaseAuthManager.IsPremium) return;

            var text = input.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // The mention goes in front of the text because the sender takes the channel's own
            // mention from its configuration and this is a one-off, not that channel's default.
            var mention = tile.DiscordMention switch
            {
                1 => "@here ",
                2 => "@everyone ",
                _ => "",
            };

            input.Text = "";
            await FlashButton(button, await DockHost.SendDockDiscordMessageAsync(channel, mention + text));
        }

        private async System.Threading.Tasks.Task SendDiscordMap(CommandDockTile tile, Border button)
        {
            var channel = EffectiveChannel(tile);
            if (channel == null || DockHost == null) return;
            if (!SupabaseAuthManager.IsPremium) return;

            await FlashButton(button, await DockHost.SendDockMapToDiscordAsync(channel));
        }

        /// <summary>
        /// Green for a moment on success, red on failure. The message lands in a window the user
        /// is not looking at, so the tile has to say something.
        /// </summary>
        private static async System.Threading.Tasks.Task FlashButton(Border button, bool ok)
        {
            var original = button.Background;
            button.Background = new SolidColorBrush(ok
                ? Color.FromArgb(0x99, 0x4C, 0xC9, 0x6A)
                : Color.FromArgb(0x99, 0xE5, 0x39, 0x35));

            await System.Threading.Tasks.Task.Delay(900);
            button.Background = original;
        }

        private static Border IconButton(string glyph, TileStyle style, string tooltip)
        {
            var button = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Background = Brush("SurfaceAlt", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = style.Size(11),
                    Foreground = style.TextMain,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            ToolTipService.SetToolTip(button, tooltip);
            return button;
        }
    }
}
