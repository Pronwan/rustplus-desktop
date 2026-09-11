using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk
{
    /// <summary>
    /// The session tile: what this sitting has amounted to on this server.
    ///
    /// Five figures in a row, because they only mean anything next to one another — an hour
    /// online reads differently with forty minutes of it idle.
    /// </summary>
    public partial class MiniMapWindow
    {
        private FrameworkElement BuildSessionTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 5; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shell.Child = grid;

            var online = AddSessionCell(grid, 0, style, "\uE916", Loc.Text("CommandDockSessionOnline", "Online"));
            var distance = AddSessionCell(grid, 1, style, "\uE707", Loc.Text("CommandDockSessionDistance", "Walked"));
            var afk = AddSessionCell(grid, 2, style, "\uE708", Loc.Text("CommandDockSessionAfk", "Idle"));
            var deaths = AddSessionCell(grid, 3, style, "\uE7BA", Loc.Text("CommandDockSessionDeaths", "Deaths"));
            var teamDeaths = AddSessionCell(grid, 4, style, "\uE716", Loc.Text("CommandDockSessionTeamDeaths", "Team"));

            _tileRefreshers.Add(() =>
            {
                if (!SupabaseAuthManager.IsPremium)
                {
                    shell.Opacity = 0.4;
                    ToolTipService.SetToolTip(shell, Loc.Text("CommandDockSupporterOnly",
                        "A supporter feature. Sign in with a supporter account to use this tile."));

                    online.Text = distance.Text = afk.Text = deaths.Text = teamDeaths.Text = "—";
                    return;
                }

                shell.Opacity = 1.0;

                var session = SessionTracker.Instance.For(DockHost?.DockServerKey);
                if (session == null)
                {
                    ToolTipService.SetToolTip(shell, Loc.Text("CommandDockSessionWaiting",
                        "Counting starts once you are connected to a server."));
                    online.Text = distance.Text = afk.Text = deaths.Text = teamDeaths.Text = "—";
                    return;
                }

                online.Text = FormatSpan(TimeSpan.FromSeconds(session.OnlineSeconds));
                distance.Text = FormatDistance(session.DistanceMetres);
                afk.Text = FormatSpan(TimeSpan.FromSeconds(session.AfkSeconds));
                deaths.Text = session.Deaths.ToString();
                teamDeaths.Text = session.TeamDeaths.ToString();

                ToolTipService.SetToolTip(shell, string.Format(
                    Loc.Text("CommandDockSessionSince", "Session since {0}"),
                    session.StartedUtc.ToLocalTime().ToString("HH:mm")));
            });

            return shell;
        }

        /// <summary>One figure with its glyph and caption. Returns the block holding the value.</summary>
        private static TextBlock AddSessionCell(Grid grid, int column, TileStyle style, string glyph, string caption)
        {
            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
            };

            stack.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = style.Size(11),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = style.TextSub,
                Effect = style.TextShadow,
            });

            var value = new TextBlock
            {
                Text = "—",
                FontSize = style.Size(13),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                TextAlignment = TextAlignment.Center,
            };
            stack.Children.Add(value);

            stack.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = style.Size(9),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = style.TextSub,
                Effect = style.TextShadow,
            });

            Grid.SetColumn(stack, column);
            grid.Children.Add(stack);
            return value;
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
            if (span.TotalMinutes >= 1) return $"{span.Minutes}m";
            return $"{span.Seconds}s";
        }

        /// <summary>
        /// Kilometres past a thousand metres, and never a decimal below it. The distance is a
        /// sum of straight lines between position samples, so it is a floor — a figure with a
        /// decimal point would claim a precision the sampling does not have.
        /// </summary>
        private static string FormatDistance(double metres)
            => metres >= 1000 ? $"{metres / 1000.0:0.0} km" : $"{(int)metres} m";
    }
}
