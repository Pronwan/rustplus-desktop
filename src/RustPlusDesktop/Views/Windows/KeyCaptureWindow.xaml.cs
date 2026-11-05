using System.Windows;
using System.Windows.Input;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// Asks for one key press and reports it back in Rust's own notation. Deliberately modal: the
/// alternative would be a background listener reading every keystroke, which is neither necessary
/// nor something this app should be doing.
/// </summary>
public partial class KeyCaptureWindow : WpfUi.FluentWindow
{
    public string? CapturedKey { get; private set; }

    /// <summary>Key already assigned, shown when the dialog opens so nothing looks lost.</summary>
    public string CurrentKey
    {
        get => CapturedKey ?? "";
        set
        {
            CapturedKey = string.IsNullOrWhiteSpace(value) ? null : value;
            if (TxtCaptured != null) TxtCaptured.Text = CapturedKey ?? "—";
        }
    }

    public KeyCaptureWindow()
    {
        InitializeComponent();

        foreach (var name in RustKeyNames.MouseAndWheel) CmbMouse.Items.Add(name);

        // PreviewKeyDown, so keys WPF would otherwise eat for navigation - Tab, arrows, Space -
        // still reach us. Those are all perfectly good binds in Rust.
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, __) => TxtCaptured.Text = CapturedKey ?? "—";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        e.Handled = true;

        var name = RustKeyNames.ToRustBind(key, Keyboard.Modifiers);
        if (name == null)
        {
            ShowWarning(RustPlusDesk.Properties.Resources.GetString("ConsoleHelperKeyUnsupported")
                        ?? "That key has no Rust equivalent. Pick another one.");
            return;
        }

        HideWarning();
        CapturedKey = name;
        TxtCaptured.Text = name;
        CmbMouse.SelectedIndex = -1;
    }

    private void CmbMouse_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbMouse.SelectedItem is not string name) return;
        HideWarning();
        CapturedKey = name;
        TxtCaptured.Text = name;
    }

    private void ShowWarning(string text)
    {
        TxtWarning.Text = text;
        TxtWarning.Visibility = Visibility.Visible;
    }

    private void HideWarning() => TxtWarning.Visibility = Visibility.Collapsed;

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        CapturedKey = null;
        TxtCaptured.Text = "—";
        CmbMouse.SelectedIndex = -1;
        HideWarning();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
