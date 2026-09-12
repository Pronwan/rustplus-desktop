using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class ChatCommandsOverlay : UserControl
{
    public ChatCommandsOverlay()
    {
        InitializeComponent();

        // Ask the central capability service rather than being told from outside. The overlay
        // is created and shown independently of the connect flow, so pushing state into it
        // would mean remembering to do so from every call site.
        Loaded += (_, __) => { ApplyEventCapabilities(); LoadAiPerHour(); };
        EventCapabilities.Changed += OnCapabilitiesChanged;
        Unloaded += (_, __) => EventCapabilities.Changed -= OnCapabilitiesChanged;
    }

    private void OnCapabilitiesChanged() => Dispatcher.Invoke(ApplyEventCapabilities);

    /// <summary>
    /// Patrol Heli and Travelling Vendor have no server-wide audio cue, so on a server without
    /// event markers those commands can never answer. Hiding the rows is honest; leaving them
    /// configurable would invite players to set up a command that always replies "unknown".
    ///
    /// Hidden rather than deleted on purpose: a server that still sends event markers can still
    /// answer both, and a player whose profile already has the command configured keeps it.
    /// </summary>
    private void ApplyEventCapabilities()
    {
        var vis = EventCapabilities.IsCloudSourced ? Visibility.Collapsed : Visibility.Visible;

        foreach (var element in new UIElement?[]
                 {
                     CmdRowHeliLabel, CmdRowHeliPrefix, CmdRowHeliBox,
                     CmdRowVendorLabel, CmdRowVendorPrefix, CmdRowVendorBox,
                 })
        {
            if (element != null) element.Visibility = vis;
        }
    }

    public event RoutedEventHandler? CommandsEnabledChanged;

    public void SetMasterBlocked(bool blocked, string message)
    {
        if (EnableChatCommandsCheckBox != null)
            EnableChatCommandsCheckBox.IsEnabled = !blocked;

        if (ChatCommandsMasterWarning != null)
            ChatCommandsMasterWarning.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;

        if (ChatCommandsMasterWarningText != null)
            ChatCommandsMasterWarningText.Text = message;
    }

    private void EnableChatCommandsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        CommandsEnabledChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Writes the profile straight away. Everything else on this page is saved when the settings
    /// close, but a door code is the kind of thing people type in and then alt-tab away from, so
    /// it gets an explicit button that confirms the write happened.
    /// </summary>
    private void BtnSaveBaseCode_Click(object sender, RoutedEventArgs e)
    {
        var vm = System.Windows.Application.Current?.MainWindow?.DataContext
                 as RustPlusDesk.ViewModels.MainViewModel;
        if (vm == null) return;

        // Trailing blank row bookkeeping runs here too: saving a freshly filled row is exactly
        // when the next empty one should appear.
        vm.Selected?.EnsureBaseCodeRows();
        vm.Save();

        if (sender is FrameworkElement fe)
        {
            fe.ToolTip = RustPlusDesk.Properties.Resources.GetString("Saved") ?? "Saved";
        }
    }

    /// <summary>
    /// How many AI questions each teammate gets an hour.
    ///
    /// Built in code rather than bound, because "unlimited" is zero in the profile and a
    /// number in the list — a converter for four fixed choices is more machinery than the
    /// choices are worth.
    /// </summary>
    private static readonly int[] AiPerHourChoices = { 3, 5, 10, 0 };

    private bool _loadingAiPerHour;

    private void LoadAiPerHour()
    {
        if (ChatAiPerHourBox == null) return;

        _loadingAiPerHour = true;

        if (ChatAiPerHourBox.Items.Count == 0)
        {
            foreach (var limit in AiPerHourChoices)
            {
                ChatAiPerHourBox.Items.Add(new ComboBoxItem
                {
                    Content = limit == 0
                        ? Helpers.Loc.Text("ChatAiPerHourUnlimited", "Unlimited")
                        : string.Format(Helpers.Loc.Text("ChatAiPerHourCount", "{0} per hour"), limit),
                    Tag = limit,
                });
            }
        }

        var profile = DataContext as Models.ServerProfile;
        int current = profile?.ChatAiPerHour ?? 5;

        int index = System.Array.IndexOf(AiPerHourChoices, current);
        ChatAiPerHourBox.SelectedIndex = index >= 0 ? index : 1;

        _loadingAiPerHour = false;
    }

    private void ChatAiPerHour_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAiPerHour) return;
        if (DataContext is not Models.ServerProfile profile) return;

        profile.ChatAiPerHour = (ChatAiPerHourBox.SelectedItem as ComboBoxItem)?.Tag as int? ?? 5;
    }
}