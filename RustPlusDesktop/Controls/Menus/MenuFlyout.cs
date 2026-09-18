using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
/// 3. The open animation. Windows slides a menu up into place, and doing that
///    inside the template would only slide the content within a window already
///    sized to it, clipping the bottom rows. The whole popup window has to move,
///    so the animation runs on the Popup's own offset from out here.
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
        SlideOpen(menu);
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        ApplyAcrylic(PresentationSource.FromVisual(menu) as HwndSource);
        ScheduleIconGutter(menu);
        SlideOpen(menu);
    }

    private static void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) menu.SetValue(AnimatedProperty, false);
    }

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        // SubmenuOpened bubbles, so every ancestor item sees it too. Only the
        // item that actually opened needs the work.
        if (sender is not MenuItem item || !ReferenceEquals(sender, e.OriginalSource)) return;

        ScheduleIconGutter(item);

        if (item.Template?.FindName("PART_Popup", item) is Popup popup)
        {
            Slide(popup);

            item.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (popup.Child is Visual child)
                    ApplyAcrylic(PresentationSource.FromVisual(child) as HwndSource);
            }));
        }
    }

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

    /// <summary>How far below its resting place a menu starts, in DIPs.</summary>
    private const double SlideDistance = 14;

    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(170));

    /// <summary>
    /// Guards against animating twice for one opening: Loaded and Opened both
    /// fire, and Loaded is the one early enough to catch the first frame.
    /// </summary>
    private static readonly DependencyProperty AnimatedProperty =
        DependencyProperty.RegisterAttached(
            "Animated", typeof(bool), typeof(MenuFlyout), new PropertyMetadata(false));

    private static void SlideOpen(ContextMenu menu)
    {
        if ((bool)menu.GetValue(AnimatedProperty)) return;

        // A ContextMenu does not own its popup — WPF makes one and parents the
        // menu to it, which is the only handle we get on the window to move.
        if (LogicalTreeHelper.GetParent(menu) is not Popup popup) return;

        menu.SetValue(AnimatedProperty, true);
        Slide(popup);
    }

    private static void Slide(Popup popup)
    {
        // FillBehavior.Stop hands the offset back to whatever set it — the
        // submenu template's own -4, or the placement WPF computed — instead of
        // leaving an animation pinned over it for the life of the popup.
        var resting = popup.VerticalOffset;

        var slide = new DoubleAnimation
        {
            From = resting + SlideDistance,
            To = resting,
            Duration = SlideDuration,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        popup.BeginAnimation(Popup.VerticalOffsetProperty, slide);

        // Short enough to only soften the leading edge of the slide rather than
        // read as a fade of its own. Drop it for a bare slide.
        if (popup.Child is UIElement child)
        {
            child.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(70)),
                FillBehavior = FillBehavior.Stop,
            });
        }
    }

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
