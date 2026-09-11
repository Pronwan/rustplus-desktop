using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using RustPlusDesk.Models;

namespace RustPlusDesk
{
    /// <summary>
    /// Turns a tile's appearance settings into the brushes, sizes and effects its builder needs.
    ///
    /// Every tile asks here rather than reaching for a theme resource directly, so "transparent
    /// with readable text" is one decision made once instead of a rule each builder has to
    /// remember. The three values resolve tile-first, dock-default second — see
    /// <see cref="CommandDockTile.Opacity"/> for why the global ones are defaults and not a
    /// second set of switches.
    /// </summary>
    public partial class MiniMapWindow
    {
        /// <summary>Everything a tile builder needs to draw itself in the user's chosen style.</summary>
        internal sealed class TileStyle
        {
            public double Opacity { get; init; } = 1.0;
            public double FontScale { get; init; } = 1.0;
            public Brush TextMain { get; init; } = Brushes.White;
            public Brush TextSub { get; init; } = Brushes.Gray;
            public Effect? TextShadow { get; init; }

            /// <summary>A font size with the tile's scale applied, rounded to a whole pixel.</summary>
            public double Size(double baseSize) => Math.Round(Math.Max(7, baseSize * FontScale));

            /// <summary>Fades a colour by the tile's opacity — for backgrounds and borders only.</summary>
            public Brush Chrome(Color color)
            {
                var faded = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Round(color.A * Opacity), color.R, color.G, color.B));
                faded.Freeze();
                return faded;
            }

            /// <summary>The same, starting from a brush the theme supplied.</summary>
            public Brush Chrome(Brush brush) =>
                brush is SolidColorBrush solid ? Chrome(solid.Color) : brush;
        }

        private TileStyle StyleFor(CommandDockTile tile)
        {
            double opacity = Math.Clamp(tile.Opacity ?? _dock.DefaultOpacity, 0, 1);
            double scale = Math.Clamp(tile.FontScale ?? _dock.DefaultFontScale, 0.7, 2.0);
            string key = tile.TextColorKey ?? _dock.DefaultTextColorKey ?? CommandDockTextColors.Auto;

            var (main, sub) = TextBrushes(key);

            // Below roughly half opacity the tile stops carrying its own text and the game shows
            // through behind it. A soft shadow is what keeps white legible over snow and black
            // legible over water; above that the background does the job and the shadow is only
            // cost.
            Effect? shadow = opacity < 0.55 ? SharedTextShadow : null;

            return new TileStyle
            {
                Opacity = opacity,
                FontScale = scale,
                TextMain = main,
                TextSub = sub,
                TextShadow = shadow,
            };
        }

        private (Brush Main, Brush Sub) TextBrushes(string key)
        {
            switch (key)
            {
                case CommandDockTextColors.White:
                    return (Frozen(Colors.White), Frozen(Color.FromRgb(0xDC, 0xDC, 0xDC)));
                case CommandDockTextColors.Black:
                    return (Frozen(Color.FromRgb(0x10, 0x12, 0x14)), Frozen(Color.FromRgb(0x36, 0x3B, 0x3F)));
                case CommandDockTextColors.Cyan:
                    return (Frozen(Color.FromRgb(0x3F, 0xD7, 0xFF)), Frozen(Color.FromRgb(0x86, 0xE4, 0xFF)));
                case CommandDockTextColors.Amber:
                    return (Frozen(Color.FromRgb(0xFF, 0xC2, 0x46)), Frozen(Color.FromRgb(0xFF, 0xD9, 0x8C)));
                case CommandDockTextColors.Red:
                    return (Frozen(Color.FromRgb(0xFF, 0x6B, 0x5E)), Frozen(Color.FromRgb(0xFF, 0x9D, 0x94)));
                case CommandDockTextColors.Green:
                    return (Frozen(Color.FromRgb(0x5B, 0xD9, 0x8A)), Frozen(Color.FromRgb(0x95, 0xE8, 0xB4)));
                default:
                    return (Brush("TextPrimary", Colors.White), Brush("TextSubtle", Colors.Gray));
            }
        }

        /// <summary>A readable swatch for the colour picker, on whatever the panel's ground is.</summary>
        internal Brush TextColorSwatch(string key) => key == CommandDockTextColors.Auto
            ? Brush("TextPrimary", Colors.White)
            : TextBrushes(key).Main;

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // One instance for the whole dock. Effects are expensive per element, and a frozen one
        // can be shared across every text block that needs it.
        private static readonly Effect SharedTextShadow = CreateTextShadow();

        private static Effect CreateTextShadow()
        {
            var shadow = new DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Direction = 270,
                Opacity = 0.85,
                Color = Colors.Black,
                RenderingBias = RenderingBias.Performance,
            };
            shadow.Freeze();
            return shadow;
        }
    }
}
