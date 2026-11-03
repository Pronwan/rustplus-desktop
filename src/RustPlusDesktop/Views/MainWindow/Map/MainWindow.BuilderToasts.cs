using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json.Linq;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Toasts drawn into the Base Builder while it is on screen.
///
/// WPF cannot paint over the builder's WebView, and the full screen builder is a window of its
/// own, so a toast raised meanwhile - an invite, the host ending the session - would sit unseen
/// underneath it. The builder draws a copy (appToasts.js). Its buttons press the real toast's
/// buttons, so every handler stays where it is; the real toast going away takes the copy along.
/// </summary>
public partial class MainWindow
{
    private sealed record BuilderToast(string Id, Controls.ToastItem Item, IReadOnlyList<ButtonBase> Buttons);

    private readonly Dictionary<Controls.ToastItem, BuilderToast> _builderToasts = new();

    private bool BaseBuilderOnScreen =>
        _baseBuilderWebView?.CoreWebView2 != null && (_baseBuilderFullscreenWindow != null || BaseBuilderPanel.IsVisible);

    private void ForwardToastToBuilder(Controls.ToastItem item)
    {
        if (!BaseBuilderOnScreen || _builderToasts.ContainsKey(item)) return;

        var texts = new List<string>();
        var buttons = new List<ButtonBase>();
        if (!string.IsNullOrWhiteSpace(item.Message)) texts.Add(item.Message);
        if (item.Content is DependencyObject content) CollectToastParts(content, texts, buttons);

        var toast = new BuilderToast(Guid.NewGuid().ToString("N"), item, buttons);
        _builderToasts[item] = toast;

        var payload = new
        {
            id = toast.Id,
            title = item.Title,
            message = string.Join("\n", texts),
            tone = ToastTone(item.AccentBrush),
            actions = buttons.Select(b => new
            {
                label = b.Content?.ToString() ?? "",
                primary = b is WpfUi.Button { Appearance: WpfUi.ControlAppearance.Primary },
                hint = b.ToolTip as string,
            }),
        };
        RunInBuilder($"window.showAppToast && window.showAppToast({JsonSerializer.Serialize(payload)});");
    }

    /// <summary>The words and buttons of a rich toast, in the order they appear. Text inside a button is its label.</summary>
    private static void CollectToastParts(DependencyObject node, List<string> texts, List<ButtonBase> buttons)
    {
        switch (node)
        {
            case ButtonBase button:
                if (button.Visibility == Visibility.Visible) buttons.Add(button);
                return;
            case TextBlock { Text: { Length: > 0 } text }:
                texts.Add(text);
                return;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            CollectToastParts(child, texts, buttons);
    }

    private string ToastTone(System.Windows.Media.Brush accent) =>
        ReferenceEquals(accent, _toastSuccessBrush) ? "success"
        : ReferenceEquals(accent, _toastCautionBrush) ? "caution"
        : ReferenceEquals(accent, _toastDangerBrush) ? "danger"
        : "info";

    /// <summary>The real toast is gone, whichever way it went.</summary>
    private void ForgetBuilderToast(Controls.ToastItem item)
    {
        if (!_builderToasts.Remove(item, out var toast)) return;
        RunInBuilder($"window.dismissAppToast && window.dismissAppToast({JsonSerializer.Serialize(toast.Id)});");
    }

    /// <summary>A button or the close button on the builder's copy.</summary>
    private void HandleBuilderToastMessage(JObject message)
    {
        var id = message["id"]?.ToString();
        var toast = _builderToasts.Values.FirstOrDefault(t => t.Id == id);
        if (toast == null) return;

        if (message["type"]?.ToString() == "app_toast_closed")
        {
            DismissToast(toast.Item);
            return;
        }

        var index = message["index"]?.Value<int>() ?? -1;
        if (index < 0 || index >= toast.Buttons.Count) return;
        var button = toast.Buttons[index];
        // A button the toast has disabled (an answer already on its way) is not pressed twice.
        if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }
}
