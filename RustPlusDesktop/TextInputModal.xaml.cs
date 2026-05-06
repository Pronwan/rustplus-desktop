using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views;

/// <summary>
/// Generic shadcn-themed text-input modal. Used for "Add camera" / "Open camera" /
/// "New group" / "Rename group" — anywhere the app needs a single short string.
/// </summary>
public partial class TextInputModal : Window
{
    public string Value { get; private set; } = "";

    public TextInputModal(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        TxtTitle.Text = title;
        TxtPrompt.Text = prompt;
        TxtInput.Text = initial ?? "";
        Loaded += (_, _) => { TxtInput.Focus(); TxtInput.SelectAll(); };
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Value = TxtInput.Text?.Trim() ?? "";
        DialogResult = !string.IsNullOrWhiteSpace(Value);
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TxtInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            BtnCancel_Click(sender, e);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
