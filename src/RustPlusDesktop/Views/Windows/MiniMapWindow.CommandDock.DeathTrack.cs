using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk
{
    /// <summary>
    /// After a death: one press to write down who did it.
    ///
    /// Rust puts the killer's name and their weapon across the top of the death screen, and then
    /// takes it away again on respawn — so whatever is going to remember it has to do so in the
    /// few seconds it is up. This reads that band off a copy of the screen, the same picture a
    /// screen recorder would take, and files the name against the death the game already
    /// reported.
    ///
    /// A button rather than an automatic read, and deliberately. Nothing here touches the game:
    /// no memory is read, no input is sent, nothing is decided for the player. A press is a
    /// person deciding to keep a note, which is a different thing from a program playing along
    /// on their behalf, and it is the difference worth keeping.
    /// </summary>
    public partial class MiniMapWindow
    {
        private const string GlyphTombstone = "";

        /// <summary>What the last read produced, per tile, so a rebuild does not lose it.</summary>
        private sealed class DeathTrackState
        {
            public string? Killer;
            public string? Weapon;
            public bool Busy;
            public string? Problem;

            /// <summary>The death this was written against, so the same one is not filed twice.</summary>
            public long ForDeath;
        }

        private readonly Dictionary<string, DeathTrackState> _deathTrackStates = new();

        private DeathTrackState DeathTrackStateFor(string tileId)
        {
            if (!_deathTrackStates.TryGetValue(tileId, out var state))
            {
                state = new DeathTrackState();
                _deathTrackStates[tileId] = state;
            }

            return state;
        }

        /// <summary>
        /// Whether the player is dead, which is the only time this tile is any use.
        ///
        /// It takes its space on the dock only while it is needed and gives it back afterwards,
        /// which is why the host is asked rather than the tile keeping its own idea.
        /// </summary>
        private bool PlayerIsDead => DockHost?.DockPlayerDead == true;

        private FrameworkElement BuildDeathTrackTile(CommandDockTile tile)
        {
            var style = StyleFor(tile);
            var shell = TileShell(style);
            shell.Tag = tile;

            var state = DeathTrackStateFor(tile.Id);

            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            shell.Child = lines;

            var heading = new TextBlock
            {
                Text = Loc.Text("CommandDockDeathTrackTitle", "Who killed you?"),
                FontSize = style.Size(11),
                FontWeight = FontWeights.SemiBold,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            lines.Children.Add(heading);

            var name = new TextBlock
            {
                FontSize = style.Size(13),
                FontWeight = FontWeights.Bold,
                Foreground = style.TextMain,
                Effect = style.TextShadow,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            lines.Children.Add(name);

            var detail = new TextBlock
            {
                FontSize = style.Size(10),
                Foreground = style.TextSub,
                Effect = style.TextShadow,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            lines.Children.Add(detail);

            var button = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(5),
                Background = Brush("SurfaceAlt", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = Loc.Text("CommandDockDeathTrackRead", "Track death"),
                    FontSize = style.Size(11),
                    Foreground = style.TextMain,
                },
            };

            button.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = ReadDeathScreenAsync(tile); };
            lines.Children.Add(button);

            KeepPresses(button);

            _tileRefreshers.Add(() =>
            {
                long death = DockHost?.DockLastOwnDeathAt ?? 0;

                // A new death clears the last one's answer. Left up, the tile would offer the
                // previous killer's name as though it were this one's.
                if (death != state.ForDeath && death != 0)
                {
                    state.ForDeath = death;
                    state.Killer = null;
                    state.Weapon = null;
                    state.Problem = null;
                }

                bool done = state.Killer is { Length: > 0 };

                name.Text = state.Busy
                    ? Loc.Text("CommandDockDeathTrackReading", "Reading…")
                    : state.Killer ?? "";

                name.Visibility = state.Busy || done ? Visibility.Visible : Visibility.Collapsed;

                detail.Text = state.Problem ?? state.Weapon ?? "";
                detail.Foreground = state.Problem != null
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A))
                    : style.TextSub;

                detail.Visibility = detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

                // The button stays after a successful read: the region may have caught the wrong
                // line, and pressing again overwrites rather than adds.
                ((TextBlock)button.Child).Text = done
                    ? Loc.Text("CommandDockDeathTrackAgain", "Read again")
                    : Loc.Text("CommandDockDeathTrackRead", "Track death");

                button.Opacity = state.Busy ? 0.4 : 1;
            });

            return shell;
        }

        /// <summary>
        /// Takes the picture, reads it, and files the name against this death.
        ///
        /// Our own windows are left out of the capture — the dock sits over the game, and this
        /// button is on it — which is the same mechanism the AI companion's screenshot uses.
        /// </summary>
        private async Task ReadDeathScreenAsync(CommandDockTile tile)
        {
            var state = DeathTrackStateFor(tile.Id);
            if (state.Busy) return;

            bool viaAi = tile.DeathReadWithAi && AiDeathScreen.Available;

            if (!viaAi && !DeathScreenReader.Available)
            {
                state.Problem = Loc.Text("CommandDockDeathTrackNoOcr",
                    "Windows has no text recognition installed.");

                RefreshTiles();
                return;
            }

            state.Busy = true;
            state.Problem = null;
            RefreshTiles();

            try
            {
                var read = await DeathScreenReader.ReadAsync(
                    tile.DeathRegionLeft, tile.DeathRegionTop,
                    tile.DeathRegionWidth, tile.DeathRegionHeight,
                    OverlayWindows()).ConfigureAwait(true);

                // The model reads the same crop, and reads scripts Windows cannot. It works
                // from the picture rather than from the gaps between words, so it is asked
                // even when Windows found something — that something may be the weapon,
                // standing in for a name the recogniser could not see.
                if (viaAi && read.ImagePath != null)
                {
                    var better = await AiDeathScreen.ReadAsync(read.ImagePath).ConfigureAwait(true);
                    if (better.Killer is { Length: > 0 })
                    {
                        var (killer, weapon) = Reconcile(better, read);
                        read = read with { Killer = killer, Weapon = weapon };
                    }
                }

                if (read.Killer is not { Length: > 0 })
                {
                    // Two different failures, and the difference matters: text was read and
                    // none of it was a name — a fall, the cold, a fire — or nothing was read at
                    // all, which is the region being wrong.
                    state.Problem = read.Lines.Count > 0
                        ? Loc.Text("CommandDockDeathTrackNoName",
                            "No name on that death screen — nothing killed you that has one.")
                        : Loc.Text("CommandDockDeathTrackNothing",
                            "Nothing readable there — check the region in this tile's settings.");

                    return;
                }

                state.Killer = read.Killer;
                state.Weapon = read.Weapon;

                long death = DockHost?.DockLastOwnDeathAt ?? 0;
                if (death == 0)
                {
                    // Read, but nothing to attach it to: the game has not reported a death this
                    // session, so there is no entry in the log to put a name on.
                    state.Problem = Loc.Text("CommandDockDeathTrackNoDeath",
                        "No death recorded yet to attach this to.");

                    return;
                }

                state.ForDeath = death;

                KillerLogStore.Record(DockHost?.DockServerKey, death, read.Killer, read.Weapon);

                // Rust shows the player's own name when they killed themselves, which is worth
                // keeping as a fact and worth not calling a killer.
                if (KillerLogStore.IsSelf(read.Killer, DockHost?.DockPlayerName))
                {
                    state.Weapon = Loc.Text("CommandDockDeathTrackSuicide", "Suicide");
                }
            }
            catch (Exception ex)
            {
                state.Problem = ex.Message;
            }
            finally
            {
                state.Busy = false;
                RefreshTiles();
            }
        }

        /// <summary>
        /// Settles the two readings of the same picture against each other.
        ///
        /// The model reads any alphabet, which is why it is asked at all, but a small one —
        /// the free tiers especially — answers with the name and the weapon run together in
        /// one piece however plainly it was asked not to. Windows had already split the row by
        /// the gaps between the boxes, and got that part right; it simply could not read the
        /// letters. So one supplies the name and the other supplies the seam.
        ///
        /// Only ever removes a suffix that the other reading independently called the weapon.
        /// A player genuinely called "Sassy Rock", killed with a rock, keeps their name unless
        /// the recogniser also thought the weapon was there — and if it did, the two readings
        /// agree and there is nothing better to go on.
        /// </summary>
        private static (string Killer, string? Weapon) Reconcile(
            (string? Killer, string? Weapon) ai, DeathScreenText ocr)
        {
            string killer = ai.Killer!.Trim();
            string? weapon = string.IsNullOrWhiteSpace(ai.Weapon) ? ocr.Weapon : ai.Weapon!.Trim();

            if (string.IsNullOrWhiteSpace(weapon)) return (killer, null);

            // The weapon hanging off the end of the name, which is the failure this is for.
            if (killer.Length > weapon!.Length &&
                killer.EndsWith(weapon, StringComparison.CurrentCultureIgnoreCase))
            {
                var trimmed = killer[..^weapon.Length].TrimEnd(' ', '|', '-', ',', ':');
                if (trimmed.Length >= 2) killer = trimmed;
            }

            return (killer, weapon);
        }

        /// <summary>Our own windows, so the capture is of the game and not of this app.</summary>
        private IReadOnlyList<IntPtr> OverlayWindows()
        {
            var handles = new List<IntPtr>();

            foreach (Window window in Application.Current?.Windows ?? new WindowCollection())
            {
                try
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero) handles.Add(handle);
                }
                catch
                {
                    // A window without a handle yet is not in the picture either.
                }
            }

            return handles;
        }
    }
}
