using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class ChatCommandsOverlay : UserControl
{
    public ChatCommandsOverlay()
    {
        InitializeComponent();

        Loaded += (_, __) => { ApplyCloudCodesAvailability(); LoadAiPerHour(); };
        IsVisibleChanged += (_, __) => { if (IsVisible) ApplyCloudCodesAvailability(); };
    }

    /// <summary>
    /// Door codes in the cloud are a Cloud 24/7 feature: greyed out without Premium, and a
    /// choice already made stays visible rather than silently flipping.
    /// </summary>
    private void ApplyCloudCodesAvailability()
    {
        bool premium = RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium;
        ChkBaseCodesInCloud.IsEnabled = premium;
        ChkBaseCodesInCloud.Opacity = premium ? 1 : 0.5;
        TxtBaseCodesInCloudHint.Opacity = premium ? 1 : 0.5;
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
