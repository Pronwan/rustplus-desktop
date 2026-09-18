using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk
{
    /// <summary>
    /// How busy the server is, now and over the session.
    ///
    /// The number answers "can I get in"; the line answers "is it filling up or emptying out",
    /// which is the question behind it and the one a single figure cannot settle.
    /// </summary>
    public partial class MiniMapWindow
    {
        private FrameworkElement BuildServerInfoTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            shell.Child = row;

            var figures = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(figures, 0);
            row.Children.Add(figures);

            // The count and the cap belong on one line: at a single cell, "/ 50 · 0 waiting"
            // underneath was wider than the tile and left the queue nowhere to go. The cap stays
            // small, so the reading is still what the eye lands on.
            var headline = new StackPanel { Orientation = Orientation.Horizontal };

            var players = new TextBlock
            {
                FontSize = style.Size(18),
                FontWeight = FontWeights.Bold,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
            };
            var capacity = new TextBlock
            {
                FontSize = style.Size(9),
                Margin = new Thickness(2, 0, 0, 2),
                VerticalAlignment = VerticalAlignment.Bottom,
                Foreground = style.TextSub,
                Effect = style.TextShadow,
            };
            headline.Children.Add(players);
            headline.Children.Add(capacity);

            var caption = new TextBlock
            {
                FontSize = style.Size(9),
                Foreground = style.TextSub,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Effect = style.TextShadow,
            };

            figures.Children.Add(headline);
            figures.Children.Add(caption);

            // Drawn by hand rather than through a charting library: it is one polyline, an area
            // under it and a dot on the end, and none of that is worth a dependency.
            var plot = new Canvas { ClipToBounds = true, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetColumn(plot, 1);
            row.Children.Add(plot);

            var area = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x3F, 0xD7, 0xFF)),
            };
            var line = new Polyline
            {
                Stroke = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
            };
            var head = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Brush("Accent", Color.FromRgb(0x3F, 0xD7, 0xFF)),
                Visibility = Visibility.Collapsed,
            };
            plot.Children.Add(area);
            plot.Children.Add(line);
            plot.Children.Add(head);

            int lastCount = -1;
            double lastWidth = -1;

            _tileRefreshers.Add(() =>
            {
                var (current, max, queue) = DockHost?.DockPopulation ?? (0, 0, "");

                players.Text = current > 0 || max > 0 ? current.ToString() : "—";
                capacity.Text = max > 0 ? $"/ {max}" : "";

                // The second line is the queue alone, and only when there is a queue to report.
                // A server with nobody waiting says "players" instead of an empty row.
                caption.Text = string.IsNullOrEmpty(queue)
                    ? Loc.Text("CommandDockServerPlayers", "players")
                    : string.Format(Loc.Text("CommandDockServerQueue", "{0} waiting"), queue);

                plot.Visibility = tile.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
                if (!tile.ShowGraph) return;

                var samples = SessionTracker.Instance.For(DockHost?.DockServerKey)?.Population
                              ?? new List<PopulationSample>();

                // Redrawn when there is something new to draw or the tile changed size, not on
                // every tick — the shape only moves once every five minutes.
                if (samples.Count == lastCount && Math.Abs(plot.ActualWidth - lastWidth) < 0.5) return;
                lastCount = samples.Count;
                lastWidth = plot.ActualWidth;

                DrawPopulationLine(plot, area, line, head, samples);
            });

            return shell;
        }

        /// <summary>
        /// Plots the readings across the plot area.
        ///
        /// The vertical scale is the range actually seen plus a little headroom, not zero to the
        /// server cap: a server sitting between 180 and 200 would otherwise be a flat line at the
        /// top, and the whole point is to show it moving.
        /// </summary>
        private static void DrawPopulationLine(
            Canvas plot, Polygon area, Polyline line, Ellipse head, IReadOnlyList<PopulationSample> samples)
        {
            line.Points.Clear();
            area.Points.Clear();
            head.Visibility = Visibility.Collapsed;

            double w = plot.ActualWidth, h = plot.ActualHeight;
            if (w <= 1 || h <= 1 || samples.Count < 2) return;

            int low = samples.Min(s => s.Players);
            int high = samples.Max(s => s.Players);
            double span = Math.Max(1, high - low);

            var points = new PointCollection();
            for (int i = 0; i < samples.Count; i++)
            {
                double x = samples.Count == 1 ? w : w * i / (samples.Count - 1.0);
                double y = h - 2 - (samples[i].Players - low) / span * (h - 4);
                points.Add(new Point(x, y));
            }

            line.Points = points;

            var filled = new PointCollection(points) { new Point(w, h), new Point(0, h) };
            area.Points = filled;

            var last = points[^1];
            Canvas.SetLeft(head, last.X - head.Width / 2);
            Canvas.SetTop(head, last.Y - head.Height / 2);
            head.Visibility = Visibility.Visible;
        }
    }
}
