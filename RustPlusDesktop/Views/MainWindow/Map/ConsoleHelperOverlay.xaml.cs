using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class ConsoleHelperOverlay : UserControl
{
    /// <summary>Item names for the picker, formatted "Name (id)" so the id survives the round trip.</summary>
    public ObservableCollection<string> ItemNames { get; } = new();

    private readonly Dictionary<string, int> _itemIdByLabel = new(StringComparer.OrdinalIgnoreCase);

    public event RoutedEventHandler? CloseRequested;
    public event RoutedEventHandler? PopoutRequested;

    public ConsoleHelperOverlay()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, __) => Populate();
    }

    private bool _populated;

    private void Populate()
    {
        if (_populated) return;
        _populated = true;

        // Route the library's diagnostics into the app log. Without this a missing or malformed
        // console-commands.json would just show an empty panel with no clue why.
        if (Application.Current?.MainWindow is MainWindow mw)
            ConsoleCommandLibrary.SetLogger(mw.AppendLog);

        BuildItemList();
        ApplyFilter(string.Empty);

        if (ConsoleCommandLibrary.All.Count == 0)
        {
            ClientEmpty.Text = RustPlusDesk.Properties.Resources.GetString("ConsoleHelperLoadFailed")
                               ?? "Could not load the command list.";
            ClientEmpty.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Flattens the item catalogue into labels the suggestion box can match on. The id is carried
    /// in the label rather than in a parallel structure, because the control hands back the string
    /// the user picked and nothing else.
    /// </summary>
    private void BuildItemList()
    {
        ItemNames.Clear();
        _itemIdByLabel.Clear();

        foreach (var item in MainWindow.sItemsById.Values
                     .Where(i => !string.IsNullOrWhiteSpace(i.Display))
                     .OrderBy(i => i.Display, StringComparer.OrdinalIgnoreCase))
        {
            var label = $"{item.Display} ({item.Id})";
            if (_itemIdByLabel.ContainsKey(label)) continue;
            _itemIdByLabel[label] = item.Id;
            ItemNames.Add(label);
        }
    }

    private void ApplyFilter(string query)
    {
        query = (query ?? "").Trim();

        bool Match(ConsoleCommandDef c) =>
            query.Length == 0
            || c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || c.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || c.Command.Contains(query, StringComparison.OrdinalIgnoreCase)
            || c.Group.Contains(query, StringComparison.OrdinalIgnoreCase);

        foreach (var (category, featured, groups, empty) in new (string, ItemsControl, ItemsControl, UIElement)[]
                 {
                     ("client", ClientFeatured, ClientGroups, ClientEmpty),
                     ("admin",  AdminFeatured,  AdminGroups,  AdminEmpty),
                 })
        {
            var feat = ConsoleCommandLibrary.Featured(category).Where(Match).ToList();
            var grouped = ConsoleCommandLibrary.Grouped(category)
                .Select(g =>
                {
                    var kept = g.Commands.Where(Match).ToList();
                    if (kept.Count == 0) return null;
                    var ng = new ConsoleCommandGroup { Title = g.Title };
                    foreach (var c in kept) ng.Commands.Add(c);
                    return ng;
                })
                .Where(g => g != null)
                .ToList();

            featured.ItemsSource = feat;
            groups.ItemsSource = grouped;
            empty.Visibility = (feat.Count == 0 && grouped.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_populated) return;
        ApplyFilter((sender as WpfUi.TextBox)?.Text ?? "");
    }

    private static void CopyToClipboard(string text, string what)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // The clipboard is a shared, locked resource; another app can hold it for a moment.
            // A retry costs nothing and turns most of these into a success.
            try { Clipboard.SetDataObject(text, true); } catch { }
        }
    }

    private void BtnCopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ConsoleCommandDef cmd)
            CopyToClipboard(cmd.ResolvedCommand, "command");
    }

    private void BtnCopyBind_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConsoleCommandDef cmd) return;
        if (!cmd.HasBind)
        {
            // Copying "bind <key> ..." would paste a line that errors in the console.
            MessageBox.Show(
                RustPlusDesk.Properties.Resources.GetString("ConsoleHelperPickKeyFirst")
                    ?? "Pick a key first.",
                RustPlusDesk.Properties.Resources.GetString("ConsoleHelperTitle") ?? "Console Helper",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CopyToClipboard(cmd.BindLine, "bind");
    }

    private void BtnCopyAllBinds_Click(object sender, RoutedEventArgs e)
    {
        var all = ConsoleCommandLibrary.AllBindLines();
        if (string.IsNullOrWhiteSpace(all))
        {
            MessageBox.Show(
                RustPlusDesk.Properties.Resources.GetString("ConsoleHelperNoBindsYet")
                    ?? "No keys bound yet.",
                RustPlusDesk.Properties.Resources.GetString("ConsoleHelperTitle") ?? "Console Helper",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CopyToClipboard(all, "all binds");
    }

    /// <summary>
    /// Captures the next key press and stores Rust's name for it. Runs as a modal prompt rather
    /// than a background listener, so we never read keystrokes the user did not offer us.
    /// </summary>
    private void BtnCaptureKey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConsoleCommandDef cmd) return;

        var dlg = new Windows.KeyCaptureWindow
        {
            Owner = Window.GetWindow(this),
            CurrentKey = cmd.BindKey,
        };

        if (dlg.ShowDialog() == true)
            cmd.BindKey = dlg.CapturedKey ?? "";
    }

    /// <summary>
    /// A WPF Popup lives in its own top-level window, and that window is not topmost. Inside the
    /// popout - which is Topmost so it can sit over the game - the suggestion list therefore
    /// opens behind the panel and looks broken. Promoting the popup's own window fixes it, and
    /// only ever runs when the host window is actually topmost.
    /// </summary>
    private bool _loggedPickerState;

    private void ItemPicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfUi.AutoSuggestBox box) return;

        // Assigned directly rather than bound. The RelativeSource binding this replaces resolved
        // differently depending on which window hosted the panel, which is exactly the difference
        // between the tab and the popout.
        box.OriginalItemsSource = ItemNames;

        if (!_loggedPickerState)
        {
            _loggedPickerState = true;
            Log($"[console-helper] item picker ready, {ItemNames.Count} items, " +
                $"host={Window.GetWindow(this)?.GetType().Name ?? "none"}");
        }

        if (Window.GetWindow(this) is { Topmost: true })
        {
            var popup = FindDescendant<System.Windows.Controls.Primitives.Popup>(box);
            if (popup != null)
            {
                popup.Opened -= Popup_Opened;
                popup.Opened += Popup_Opened;
            }
        }
    }

    private static void Log(string line)
    {
        if (Application.Current?.MainWindow is MainWindow mw) mw.AppendLog(line);
    }

    private void Popup_Opened(object? sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.Popup popup) return;
        if (popup.Child == null) return;

        // The popup's HWND only exists once it has been rendered, so this has to wait a beat.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (PresentationSource.FromVisual(popup.Child) is System.Windows.Interop.HwndSource src)
                    NativeTopmost.Promote(src.Handle);
            }
            catch
            {
                // Worst case the list stays behind the panel, which is where it was before.
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is T nested) return nested;
        }
        return null;
    }

    private void ItemPicker_SuggestionChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfUi.AutoSuggestBox box) return;
        if (box.Tag is not ConsoleCommandParam param) return;

        var chosen = box.Text;
        if (string.IsNullOrWhiteSpace(chosen)) return;

        if (_itemIdByLabel.TryGetValue(chosen, out var id))
        {
            param.Value = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        // Someone typed a raw id instead of picking from the list - accept it, it is valid input.
        if (int.TryParse(chosen.Trim(), out var typed))
            param.Value = typed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void BtnPopout_Click(object sender, RoutedEventArgs e) => PopoutRequested?.Invoke(this, e);

    private void BtnClose_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    /// <summary>Hides the popout button when the panel is already living in its own window.</summary>
    public void SetPopoutAvailable(bool available)
    {
        if (BtnPopout != null)
            BtnPopout.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }
}
