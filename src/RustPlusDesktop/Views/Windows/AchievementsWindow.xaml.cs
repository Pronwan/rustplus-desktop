using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services.Achievements;

namespace RustPlusDesk.Views.Windows;

public partial class AchievementsWindow
{
    /// <summary>One row in the list, already shaped for binding.</summary>
    public sealed class Row
    {
        public string Name { get; init; } = "";
        public ImageSource? Icon { get; init; }
        public double IconOpacity { get; init; }
        public string EarnedText { get; init; } = "";
        public Visibility EarnedTextVisibility { get; init; }
        public Visibility UnknownGlyphVisibility { get; init; }
        public Brush NameBrush { get; init; } = Brushes.White;
        public FontWeight NameWeight { get; init; }
        public Brush RowBackground { get; init; } = Brushes.Transparent;

        /// <summary>Earned since the list was last opened - lit up so it stands out.</summary>
        public Brush RowBorder { get; init; } = Brushes.Transparent;
        public Thickness RowBorderThickness { get; init; }
        public Visibility NewBadgeVisibility { get; init; }
        public Effect? RowGlow { get; init; }
    }

    /// <summary>
    /// Icons come out of one sprite sheet, cropped per cell. Cropping shares the
    /// decoded sheet between all 31 rows instead of decoding 31 files.
    /// </summary>
    private static BitmapSource? _sheet;

    public AchievementsWindow(Window? owner = null)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;

        Build();

        // Opening the list is what "seeing" them means, so the badge clears here.
        AchievementService.MarkAllSeen();
    }

    private void Build()
    {
        int earned = AchievementService.EarnedCount;
        int total = AchievementService.TotalCount;

        string fmt = Helpers.Loc.Text("AchievementsProgress", "{0} of {1} earned");
        TxtProgress.Text = string.Format(CultureInfo.CurrentCulture, fmt, earned, total);
        BarProgress.Maximum = total;
        BarProgress.Value = earned;

        var rows = new List<Row>(total);
        foreach (var def in AchievementCatalog.All)
        {
            bool isEarned = AchievementService.IsEarned(def.Id);

            // Read before MarkAllSeen runs in the constructor, so "new" is still known.
            bool isNew = isEarned && AchievementService.IsUnseen(def.Id);

            // A secret stays hidden until earned: no name, no icon, just a question mark.
            bool hideEntirely = def.IsSecret && !isEarned;

            var when = AchievementService.EarnedAt(def.Id);
            string earnedText = when.HasValue
                ? when.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "";

            rows.Add(new Row
            {
                Name = hideEntirely ? "???" : def.Name,
                Icon = hideEntirely ? null : GetIcon(def.SheetIndex),
                IconOpacity = isEarned ? 1.0 : 0.22,
                EarnedText = earnedText,
                EarnedTextVisibility = isEarned ? Visibility.Visible : Visibility.Collapsed,
                UnknownGlyphVisibility = hideEntirely ? Visibility.Visible : Visibility.Collapsed,
                NameBrush = isEarned ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x7A, 0xFF, 0xFF, 0xFF)),
                NameWeight = isEarned ? FontWeights.SemiBold : FontWeights.Normal,
                RowBackground = isNew
                    ? new SolidColorBrush(Color.FromArgb(0x2E, 0xD2, 0x7A, 0x1E))
                    : isEarned
                        ? new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))
                        : Brushes.Transparent,
                RowBorder = isNew
                    ? new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xC1, 0x4D))
                    : Brushes.Transparent,
                RowBorderThickness = new Thickness(isNew ? 1 : 0),
                NewBadgeVisibility = isNew ? Visibility.Visible : Visibility.Collapsed,
                RowGlow = isNew
                    ? new DropShadowEffect
                    {
                        Color = Color.FromRgb(0xFF, 0xC1, 0x4D),
                        BlurRadius = 14,
                        ShadowDepth = 0,
                        Opacity = 0.55
                    }
                    : null
            });
        }

        ListAchievements.ItemsSource = rows;
    }

    /// <summary>Crops one cell out of the sheet, by index in reading order.</summary>
    private static ImageSource? GetIcon(int index)
    {
        try
        {
            if (_sheet == null)
            {
                var sheet = new BitmapImage();
                sheet.BeginInit();
                sheet.UriSource = new Uri(AchievementCatalog.SheetUri, UriKind.Absolute);
                sheet.CacheOption = BitmapCacheOption.OnLoad;
                sheet.EndInit();
                sheet.Freeze();
                _sheet = sheet;
            }

            int col = index % AchievementCatalog.Columns;
            int row = index / AchievementCatalog.Columns;

            int step = AchievementCatalog.CellSize + AchievementCatalog.CellGap;
            int x = AchievementCatalog.CellGap + col * step;
            int y = AchievementCatalog.CellGap + row * step;

            if (x + AchievementCatalog.CellSize > _sheet.PixelWidth) return null;
            if (y + AchievementCatalog.CellSize > _sheet.PixelHeight) return null;

            var cropped = new CroppedBitmap(
                _sheet,
                new Int32Rect(x, y, AchievementCatalog.CellSize, AchievementCatalog.CellSize));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            // A missing sheet costs the icons, not the window.
            return null;
        }
    }
}
