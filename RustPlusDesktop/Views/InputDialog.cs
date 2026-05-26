using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public class InputDialog : Window
{
    public string Result { get; private set; } = "";
    private readonly TextBox _textBox;

    public InputDialog(string title, string label, string defaultValue = "")
    {
        Title = title;
        Width = 350;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Surface");
        Foreground = (Brush)Application.Current.FindResource("TextPrimary");

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8), Foreground = (Brush)Application.Current.FindResource("TextPrimary") };
        Grid.SetRow(lbl, 0);
        grid.Children.Add(lbl);

        _textBox = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 16) };
        Grid.SetRow(_textBox, 1);
        grid.Children.Add(_textBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new WpfUi.Button { Content = "OK", Appearance = WpfUi.ControlAppearance.Primary, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        okBtn.Click += (s, e) => { Result = _textBox.Text; DialogResult = true; Close(); };
        var cancelBtn = new WpfUi.Button { Content = "Cancel", Appearance = WpfUi.ControlAppearance.Secondary, Width = 80 };
        cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);

        var btnBorder = new Border { Child = btnPanel };
        Grid.SetRow(btnBorder, 2);
        grid.Children.Add(btnBorder);

        Content = grid;
        _textBox.Focus();
        _textBox.SelectAll();
        _textBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Result = _textBox.Text; DialogResult = true; Close(); } };
    }
}
