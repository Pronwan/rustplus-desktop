using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RustPlusDesk.Controls.Menus;

/// <summary>
/// The two things Views/Themes/MenuFlyout.xaml cannot express on its own.
///
/// 1. The icon rail. WinUI reserves the left column for the whole menu level
///    when any item on that level carries an icon or a checkmark, so the labels
///    line up; when nothing does, the column disappears and the menu stays
///    narrow. XAML cannot see an item's siblings, so the rail is decided here
///    and pushed onto each item as <see cref="ShowIconGutterProperty"/>.
///
/// 2. Acrylic. A popup is its own top-level window, so the blur has to be asked
///    for on that window's handle. Windows 11 only — Windows 10 keeps the
///    opaque surface the dictionary ships with.
///
/// 3. The open animation. Windows slides a menu a few pixels out from where it
///    was invoked. The acrylic belongs to the popup window, not to anything
///    the template draws, so sliding content inside it would leave the blur
///    sitting still. The whole window has to move; <see cref="PopupEntrance"/>
///    does that in step with the compositor rather than through the Popup's
///    offset, which re-ran WPF placement every frame and stuttered.
/// </summary>
public static class MenuFlyout
{
    /// <summary>Set per item by <see cref="ApplyIconGutter"/>; read by the menu templates.</summary>
    public static readonly DependencyProperty ShowIconGutterProperty =
        DependencyProperty.RegisterAttached(
            "ShowIconGutter",
            typeof(bool),
            typeof(MenuFlyout),
            new PropertyMetadata(false));

    public static void SetShowIconGutter(DependencyObject element, bool value)
        => element.SetValue(ShowIconGutterProperty, value);

    public static bool GetShowIconGutter(DependencyObject element)
        => (bool)element.GetValue(ShowIconGutterProperty);

    private static bool _initialized;

    /// <summary>
    /// Hooks every context menu in the app. Call once, from App startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // A ContextMenu's popup picks up the system menu animation through this
        // key: a 150 ms linear fade of the content that the acrylic behind it
        // does not share, so the blur pops in and the text drifts in after it.
        // The entrance below replaces it. Application resources are searched
        // before system ones, so this wins for every context menu.
        if (Application.Current is { } app)
            app.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;

        // Loaded runs before the popup's first frame, so the acrylic is in place
        // by the time anything is painted. Opened is the backstop for menus whose
        // handle is not up yet at Loaded.
        EventManager.RegisterClassHandler(
            typeof(ContextMenu), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnContextMenuLoaded));
        EventManager.RegisterClassHandler(
            typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnContextMenuOpened));
        EventManager.RegisterClassHandler(
            typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnContextMenuClosed));
        EventManager.RegisterClassHandler(
            typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
    }

    // ── Icon rail ───────────────────────────────────────────────────────────

    private static void OnContextMenuLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        ApplyAcrylic(PresentationSource.FromVisual(menu) as HwndSource);
        ScheduleIconGutter(menu);
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        // Opened is raised right after the popup window is shown and before
        // WPF renders into it, the only point where the first visible frame
        // can still be placed at the start of the slide.
        if (menu.Parent is Popup popup) BeginEntrance(popup, menu, horizontal: false);

        ApplyAcrylic(PresentationSource.FromVisual(menu) as HwndSource);
        ScheduleIconGutter(menu);
    }

    private static void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { Parent: Popup popup }) CancelEntrance(popup);
    }

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        // SubmenuOpened bubbles, so every ancestor item sees it too. Only the
        // item that actually opened needs the work.
        if (sender is not MenuItem item || !ReferenceEquals(sender, e.OriginalSource)) return;

        ScheduleIconGutter(item);

        if (item.Template?.FindName("PART_Popup", item) is not Popup popup) return;

        // SubmenuOpened is raised from the IsSubmenuOpen change callback, before
        // the template's IsOpen binding has caught up, so the window does not
        // exist yet. Its own Opened event is where it does.
        if ((bool)popup.GetValue(SubmenuHookedProperty)) return;

        popup.SetValue(SubmenuHookedProperty, true);
        popup.Opened += OnSubmenuPopupOpened;
        popup.Closed += OnSubmenuPopupClosed;

        // Backstop for a template whose popup somehow opened first. Only on the
        // first hook-up: after that the Opened handler covers it, and starting a
        // second entrance would read the parked position as the resting one.
        if (popup.IsOpen) OnSubmenuPopupOpened(popup, EventArgs.Empty);
    }

    private static void OnSubmenuPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup { Child: FrameworkElement child } popup) return;
        BeginEntrance(popup, child, horizontal: true);
        ApplyAcrylic(PresentationSource.FromVisual(child) as HwndSource);
    }

    private static void OnSubmenuPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup popup) CancelEntrance(popup);
    }

    private static readonly DependencyProperty SubmenuHookedProperty =
        DependencyProperty.RegisterAttached(
            "SubmenuHooked", typeof(bool), typeof(MenuFlyout), new PropertyMetadata(false));

    /// <summary>
    /// Containers are generated lazily, and data-bound items get their Icon set
    /// after generation, so the scan waits for the layout pass to finish.
    /// </summary>
    private static void ScheduleIconGutter(ItemsControl level)
        => level.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyIconGutter(level)));

    private static void ApplyIconGutter(ItemsControl level)
    {
        var items = new List<MenuItem>(level.Items.Count);

        for (int i = 0; i < level.Items.Count; i++)
        {
            var container = level.ItemContainerGenerator.ContainerFromIndex(i)
                            ?? level.Items[i] as DependencyObject;

            if (container is MenuItem item) items.Add(item);
        }

        bool anyIcons = false;
        foreach (var item in items)
        {
            if (item.Icon != null || item.IsCheckable || item.IsChecked)
            {
                anyIcons = true;
                break;
            }
        }

        foreach (var item in items) SetShowIconGutter(item, anyIcons);
    }

    // ── Open animation ──────────────────────────────────────────────────────

    // Windows 11 menus come out of the point they were invoked from: a menu that
    // opens below the cursor drops down into place, one flipped above it rises,
    // and a submenu slides out sideways from its parent. Short distance, Fluent
    // decelerate curve, so nearly all the travel is over in the first few
    // frames and the rest is a soft landing.

    /// <summary>Start offset of a context menu, in DIPs.</summary>
    private const double MenuSlideDistance = 16;

    /// <summary>Start offset of a submenu, in DIPs. Shorter, since it starts right next to its parent.</summary>
    private const double SubmenuSlideDistance = 10;

    /// <summary>Fluent's "normal" duration. The decelerate curve front-loads it.</summary>
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(250);

    private static readonly DependencyProperty EntranceProperty =
        DependencyProperty.RegisterAttached(
            "Entrance", typeof(PopupEntrance), typeof(MenuFlyout), new PropertyMetadata(null));

    /// <summary>
    /// Off when Windows has animation effects turned off, or the user asked the
    /// app for fewer effects. Both mean motion is unwelcome, so the menu just
    /// appears.
    /// </summary>
    private static bool AnimationsEnabled
    {
        get
        {
            if (!SystemParameters.ClientAreaAnimation || !SystemParameters.MenuAnimation) return false;

            try
            {
                return !Services.TrackingService.ReduceUiEffects;
            }
            catch
            {
                return true;
            }
        }
    }

    private static void BeginEntrance(Popup popup, FrameworkElement content, bool horizontal)
    {
        CancelEntrance(popup);

        if (!AnimationsEnabled) return;
        if (PresentationSource.FromVisual(content) is not HwndSource source) return;
        if (!GetWindowRect(source.Handle, out var rect)) return;

        var dpi = VisualTreeHelper.GetDpi(content);
        int fromX = 0, fromY = 0;

        if (horizontal)
        {
            // Sideways out of the parent item, toward the side it opened on.
            var distance = (int)Math.Round(SubmenuSlideDistance * dpi.DpiScaleX);
            // The submenu template leaves PlacementTarget unset, so the popup
            // places itself against its visual parent, the parent item's grid.
            var anchor = popup.PlacementTarget ?? VisualTreeHelper.GetParent(popup) as UIElement;
            var openedLeft = anchor is UIElement parent
                             && PresentationSource.FromVisual(parent) != null
                             && rect.Left < parent.PointToScreen(new Point(0, 0)).X;
            fromX = openedLeft ? distance : -distance;
        }
        else
        {
            // Down out of the anchor, or up out of it when there was no room below.
            var distance = (int)Math.Round(MenuSlideDistance * dpi.DpiScaleY);
            fromY = OpenedAboveAnchor(popup, rect) ? distance : -distance;
        }

        var entrance = PopupEntrance.Begin(source, fromX, fromY, SlideDuration);
        if (entrance != null) popup.SetValue(EntranceProperty, entrance);
    }

    private static void CancelEntrance(Popup popup)
    {
        if (popup.GetValue(EntranceProperty) is PopupEntrance entrance)
        {
            entrance.Cancel();
            popup.ClearValue(EntranceProperty);
        }
    }

    /// <summary>
    /// Whether WPF flipped the menu above its anchor, which is the cursor for a
    /// right-click and the target element for a menu placed against one.
    /// </summary>
    private static bool OpenedAboveAnchor(Popup popup, RECT rect)
    {
        double anchorY;

        if (popup.Placement is PlacementMode.Mouse or PlacementMode.MousePoint && GetCursorPos(out var cursor))
        {
            anchorY = cursor.Y;
        }
        else if (popup.PlacementTarget is FrameworkElement target && PresentationSource.FromVisual(target) != null)
        {
            anchorY = target.PointToScreen(new Point(0, target.ActualHeight / 2)).Y;
        }
        else
        {
            return false;
        }

        return (rect.Top + rect.Bottom) / 2.0 < anchorY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    // ── Acrylic ─────────────────────────────────────────────────────────────

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// <summary>Tint painted into the blur, as the AABBGGRR the accent API wants — #1E2026 at 75%.</summary>
    private const uint AcrylicTint = 0xBF26201E;

    /// <summary>Windows 11 21H2. Earlier builds have no rounded window corners to match the blur to.</summary>
    private static readonly bool IsSupported = Environment.OSVersion.Version.Build >= 22000;

    private static bool _resourcesSwapped;

    private static void ApplyAcrylic(HwndSource? source)
    {
        if (!IsSupported || source == null || source.Handle == IntPtr.Zero) return;

        var accent = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2,
            GradientColor = AcrylicTint,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(accent, buffer, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size,
            };

            if (SetWindowCompositionAttribute(source.Handle, ref data) == 0) return;

            // DWM rounds the window itself, which clips the blur to the same
            // radius the menu paints its own background at.
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(source.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            SwapInAcrylicResources();
        }
        catch
        {
            // Any failure leaves the opaque surface from the dictionary in place.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Only once the blur is known to have taken: the translucent surface is
    /// unreadable without it, so the opaque fallback stays until a real window
    /// has accepted the accent policy.
    /// </summary>
    private static void SwapInAcrylicResources()
    {
        if (_resourcesSwapped) return;
        _resourcesSwapped = true;

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        // The blur carries the colour now; this is only the depth on top of it.
        resources["MenuFlyoutBackground"] = new SolidColorBrush(Color.FromArgb(0x2B, 0x00, 0x00, 0x00));

        // A blurred, DWM-rounded window reads as lifted on its own, and the
        // shadow has no margin inside the popup to render into anyway.
        resources["MenuFlyoutShadow"] = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
