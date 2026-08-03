using System.Collections;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// Code-only window showing the local death log stats for a server: per-player
    /// counts + average survival, a breakdown by location, and recent deaths.
    /// Built without XAML so it stays self-contained.
    /// </summary>
    public sealed class DeathStatsWindow : Window
    {
        public DeathStatsWindow(string? serverKey)
        {
            Title = "Death Stats";
            Width = 660;
            Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var summary = DeathLogStore.LoadForServer(serverKey);

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = summary.Total == 0
                    ? "No deaths logged yet for this server."
                    : $"{summary.Total} death(s) across {summary.Victims} player(s).",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12),
            });

            if (summary.Total > 0)
            {
                root.Children.Add(SectionLabel("By player"));
                root.Children.Add(MakeGrid(summary.ByVictim));

                root.Children.Add(SectionLabel("By location"));
                root.Children.Add(MakeGrid(summary.ByLocation));

                root.Children.Add(SectionLabel("Recent"));
                root.Children.Add(MakeGrid(summary.Recent));
            }

            Content = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
        }

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4),
        };

        private static DataGrid MakeGrid(IEnumerable items) => new()
        {
            ItemsSource = items,
            AutoGenerateColumns = true,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            MaxHeight = 200,
        };
    }
}
