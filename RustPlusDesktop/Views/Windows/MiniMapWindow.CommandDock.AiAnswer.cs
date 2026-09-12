using System;
using System.Linq;
using System.Windows;
using RustPlusDesk.Models;
using RustPlusDesk.Services.AiCompanion;

namespace RustPlusDesk
{
    /// <summary>
    /// The dock's side of the answer panel: where it sits, and when it is there at all.
    ///
    /// The window itself knows nothing about tiles — it is handed a rectangle. That keeps the
    /// one thing that changes as the dock is dragged, resized and rearranged in the place that
    /// already tracks all three.
    /// </summary>
    public partial class MiniMapWindow
    {
        private Views.Windows.AiAnswerWindow? _aiAnswer;

        private CommandDockTile? AiTile =>
            _dock.Tiles.FirstOrDefault(t => t.Kind == CommandDockTileKinds.AiCompanion);

        /// <summary>Opens the panel under the AI tile, or moves it there if it is already open.</summary>
        private void ShowAiAnswer()
        {
            var tile = AiTile;
            if (tile == null) return;

            _aiAnswer ??= new Views.Windows.AiAnswerWindow { Owner = this };
            _aiAnswer.ApplyAppearance();

            var cell = CellRect(tile);

            // The canvas sits at the window's own origin — no chrome to account for, since the
            // dock has none — so a cell's position on screen is just the window's plus its own.
            var onScreen = new Rect(Left + cell.X, Top + cell.Y, cell.Width, cell.Height);

            _aiAnswer.Show();
            _aiAnswer.DockUnder(onScreen, ScreenBoundsFor(this));
        }

        private void HideAiAnswer() => _aiAnswer?.Hide();

        /// <summary>
        /// Keeps the panel under its tile while the dock is moved or rearranged.
        ///
        /// Called from the same places that reposition the settings popup: an overlay that stays
        /// behind when the window it belongs to moves reads as a second, unrelated window.
        /// </summary>
        private void FollowAiAnswer()
        {
            if (_aiAnswer is not { IsVisible: true }) return;

            if (AiTile == null)
            {
                _aiAnswer.Hide();
                return;
            }

            ShowAiAnswer();
        }

        /// <summary>Closes the panel for good, when the dock itself is going away.</summary>
        private void CloseAiAnswer()
        {
            AiCompanionService.Instance.Cancel();

            _aiAnswer?.Close();
            _aiAnswer = null;
        }
    }
}
