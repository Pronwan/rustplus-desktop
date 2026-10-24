using System.Windows;
using RustPlusDesk.Views.Windows;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        private ConsoleHelperPopoutWindow? _consoleHelperPopout;

        private void ConsoleHelper_CloseRequested(object sender, RoutedEventArgs e) =>
            ReturnToLastWorkspace();

        /// <summary>
        /// Detaches the helper into its own always-on-top window so it can sit next to Rust while
        /// the console is open, and returns the main window to whatever the user was doing.
        /// A second click focuses the existing window instead of opening another one.
        /// </summary>
        private void ConsoleHelper_PopoutRequested(object sender, RoutedEventArgs e)
        {
            if (_consoleHelperPopout != null)
            {
                try
                {
                    _consoleHelperPopout.Activate();
                    return;
                }
                catch
                {
                    // Window was closed behind our back; fall through and make a new one.
                    _consoleHelperPopout = null;
                }
            }

            _consoleHelperPopout = new ConsoleHelperPopoutWindow { Owner = null };
            _consoleHelperPopout.Closed += (_, __) => _consoleHelperPopout = null;
            _consoleHelperPopout.Show();

            ReturnToLastWorkspace();
        }
    }
}
