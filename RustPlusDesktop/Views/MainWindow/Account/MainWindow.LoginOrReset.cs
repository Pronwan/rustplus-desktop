using System.ComponentModel;
using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _loginOrResetWired;

    /// <summary>Idempotent. Called once during MainWindow construction.</summary>
    private void InitLoginOrResetButton()
    {
        if (_loginOrResetWired) return;
        _loginOrResetWired = true;

        if (_vm is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "SteamId64")
                    Dispatcher.Invoke(UpdateLoginOrResetButton);
            };
        }
        UpdateLoginOrResetButton();
    }

    private void UpdateLoginOrResetButton()
    {
        if (BtnLoginOrReset == null) return;
        bool loggedIn = !string.IsNullOrWhiteSpace(_vm?.SteamId64);
        BtnLoginOrReset.Content = loggedIn ? "Reset Connection" : "Login with Steam";
        BtnLoginOrReset.ToolTip = loggedIn
            ? "Disconnect, log out, and re-pair from scratch"
            : "Sign in with Steam to start using Rust+";
    }

    private void BtnLoginOrReset_Click(object sender, RoutedEventArgs e)
    {
        bool loggedIn = !string.IsNullOrWhiteSpace(_vm?.SteamId64);
        if (loggedIn) BtnHardReset_Click(sender, e);
        else BtnSteamLogin_Click(sender, e);
    }
}
