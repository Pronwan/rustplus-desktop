using System;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RustPlusDesk.Features.Tutorials.Controls;

public partial class TutorialOverlay : UserControl, ITutorialPresenter
{
    private TutorialPresentation? _presentation;
    private IInputElement? _previousFocus;

    public TutorialOverlay()
    {
        InitializeComponent();

        // Straight out of this control and into a window of its own. See EnsureHost.
        CanvasHost.Children.Remove(OverlayCanvas);

        SizeChanged += (_, _) =>
        {
            ApplyCoverSize();
            RenderPresentation();
            PositionHost();
        };

        // Everything that hides the overlay does it by setting Visibility, here and from
        // the window that drives the tutorial, so that is what the host follows.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) ShowHost();
            else HideHost();
        };

        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window == null) return;

            window.LocationChanged += (_, _) => PositionHost();
            window.SizeChanged += (_, _) => PositionHost();

            // Maximising and restoring move where the client area starts without always
            // raising LocationChanged, and the size is only settled a pass later.
            window.StateChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(PositionHost), DispatcherPriority.Loaded);

            // The keys belong to the tutorial wherever the focus happens to be. The host
            // window is deliberately never activated, so Enter and Escape arrive here.
            window.PreviewKeyDown += OnPreviewKeyDown;
        };

        PreviewKeyDown += OnPreviewKeyDown;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) ApplyContrast();
        };
    }

    /// <summary>The window the overlay is drawn in, owned by the app's. See EnsureHost.</summary>
    private Window? _host;

    /// <summary>The area the overlay covers, which everything inside the host is drawn to.</summary>
    private Size _cover = Size.Empty;

    private double CoverWidth => _cover.Width;
    private double CoverHeight => _cover.Height;

    /// <summary>
    /// How big the overlay is meant to be, asking its parent when it cannot say itself.
    ///
    /// A collapsed control reports nothing — WPF does not arrange it — and the overlay is
    /// collapsed right up to the moment the tutorial starts. Its own reading is used when
    /// there is one; the slot it sits in answers the same question the rest of the time.
    /// </summary>
    private Size CoverSize()
    {
        if (ActualWidth > 0 && ActualHeight > 0) return new Size(ActualWidth, ActualHeight);

        return VisualTreeHelper.GetParent(this) is FrameworkElement parent &&
               parent.ActualWidth > 0 && parent.ActualHeight > 0
            ? new Size(parent.ActualWidth, parent.ActualHeight)
            : _cover;
    }

    /// <summary>Gives the canvas the size the overlay covers.</summary>
    private void ApplyCoverSize()
    {
        var size = CoverSize();
        if (size.Width <= 0 || size.Height <= 0) return;

        _cover = size;

        OverlayCanvas.Width = size.Width;
        OverlayCanvas.Height = size.Height;
    }

    /// <summary>
    /// The window that holds the dimming, the spotlight and the popover.
    ///
    /// This was a Popup, for one good reason: the 3D map is a WebView2, which draws in a
    /// window of its own and covers anything WPF puts over it. A popup is a window too, so
    /// it sat above it.
    ///
    /// But a transparent popup is given at most three quarters of the monitor's area —
    /// measured here on three screens of different sizes, 75% of each, to the pixel — and a
    /// window maximised on a 2560x1440 screen needs 2560x1343, a quarter past the
    /// allowance. WPF handed back a shorter window without saying so, and the dimming
    /// stopped part-way down the screen with the app bright below it. Asking again did not
    /// help: the limit is the same every time, on every monitor.
    ///
    /// A window of its own has no such limit, and keeps what the popup was there for:
    /// owned by the app's window, so it stays above it and above the WebView2, minimises
    /// with it and closes with it. Never activated, so the app keeps the keyboard.
    /// </summary>
    private void EnsureHost()
    {
        if (_host != null) return;

        var owner = Window.GetWindow(this);
        if (owner == null) return;

        _host = new Window
        {
            Owner = owner,
            Title = "Tutorial",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            SnapsToDevicePixels = true,
            Content = OverlayCanvas,
        };

        _host.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Brings the host up over the app, at the size and place it belongs.</summary>
    private void ShowHost()
    {
        EnsureHost();
        if (_host == null) return;

        PositionHost();

        try
        {
            if (!_host.IsVisible) _host.Show();
        }
        catch
        {
            // The owner is on its way out; there is nothing left to put a tutorial over.
            return;
        }

        // Again once layout has run: until it has, a control that was collapsed a moment
        // ago can still be reporting the position and size it had before.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PositionHost();
            RenderPresentation();
        }), DispatcherPriority.Loaded);
    }

    private void HideHost()
    {
        try { _host?.Hide(); }
        catch { /* already gone with its owner */ }
    }

    /// <summary>Lays the host exactly over the area this control occupies on the desktop.</summary>
    private void PositionHost()
    {
        if (_host == null) return;

        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is null) return;

            ApplyCoverSize();
            if (_cover.Width <= 0 || _cover.Height <= 0) return;

            Point device = PointToScreen(new Point(0, 0));
            Point dip = source.CompositionTarget.TransformFromDevice.Transform(device);

            _host.Left = dip.X;
            _host.Top = dip.Y;
            _host.Width = _cover.Width;
            _host.Height = _cover.Height;
        }
        catch
        {
            // Between a window closing and its source going away this can throw; the
            // overlay is about to disappear with it either way.
        }
    }
    public event EventHandler? NextRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? SkipRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? QuickTourRequested;
    public event EventHandler? ChooseTutorialsRequested;
    public event EventHandler? WelcomeDismissed;

    bool ITutorialPresenter.IsVisible => Visibility == Visibility.Visible;

    public void Show(TutorialPresentation presentation)
    {
        _presentation = presentation;
        if (Visibility != Visibility.Visible) _previousFocus = Keyboard.FocusedElement;
        WelcomePanel.Visibility = Visibility.Collapsed;
        CancelPanel.Visibility = Visibility.Collapsed;
        StepPanel.Visibility = Visibility.Visible;
        TutorialTitleText.Text = presentation.TutorialTitle;
        StepTitleText.Text = presentation.StepTitle;
        DescriptionText.Text = presentation.Description;
        // A missing or unreadable image must not take the step down with it: the words carry
        // the instruction, the picture only illustrates it.
        StepImageBorder.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(presentation.ImagePath))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(presentation.ImagePath, UriKind.RelativeOrAbsolute);
                bitmap.EndInit();
                StepImage.Source = bitmap;
                StepImageBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                StepImage.Source = null;
            }
        }

        TipText.Text = presentation.Tip;
        TipBorder.Visibility = string.IsNullOrWhiteSpace(presentation.Tip) ? Visibility.Collapsed : Visibility.Visible;
        ProgressText.Text = string.Format(Properties.Resources.GetString("Tutorials.Common.Progress"), presentation.StepNumber, presentation.StepCount);
        BackButton.IsEnabled = presentation.CanGoBack;
        NextButton.Content = Properties.Resources.GetString(presentation.IsLastStep ? "Tutorials.Common.Finish" : "Tutorials.Common.Next");
        AutomationProperties.SetName(NextButton, NextButton.Content?.ToString() ?? string.Empty);
        TargetBlocker.Visibility = presentation.Target.Element is not null && !presentation.AllowTargetInteraction &&
            !Tutorial.GetAllowInteraction(presentation.Target.Element)
            ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;

        ShowHost();
        FlowDirection = FlowDirection.LeftToRight;
        Popover.FlowDirection = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ApplyContrast();
        RenderPresentation();
        Dispatcher.BeginInvoke(() =>
        {
            RenderPresentation();
            NextButton.Focus();
            (UIElementAutomationPeer.CreatePeerForElement(StepTitleText) ?? new TextBlockAutomationPeer(StepTitleText))
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, DispatcherPriority.Loaded);
        if (SystemParameters.ClientAreaAnimation && _host != null)
        {
            _host.Opacity = 0;
            _host.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        }
    }

    public void ShowWelcome()
    {
        _presentation = null;
        _previousFocus = Keyboard.FocusedElement;
        StepPanel.Visibility = Visibility.Collapsed;
        CancelPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Visible;
        SpotlightBorder.Visibility = Visibility.Collapsed;
        TargetBlocker.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;

        ShowHost();
        FlowDirection = FlowDirection.LeftToRight;
        Popover.FlowDirection = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        RenderCentered();
        Dispatcher.BeginInvoke(() => Keyboard.Focus(WelcomePanel), DispatcherPriority.Loaded);
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        HideHost();
        _presentation = null;
        if (_previousFocus is not null) Keyboard.Focus(_previousFocus);
        _previousFocus = null;
    }

    private void RenderPresentation()
    {
        DimPath.Data = CreateDimGeometry(_presentation?.Target.Bounds ?? Rect.Empty);
        if (_presentation is null || _presentation.Placement == TutorialPlacement.Center || _presentation.Target.Bounds.IsEmpty)
        {
            SpotlightBorder.Visibility = Visibility.Collapsed;
            TargetBlocker.Visibility = Visibility.Collapsed;
            RenderCentered();
            return;
        }

        Rect target = _presentation.Target.Bounds;
        SpotlightBorder.Visibility = Visibility.Visible;
        SetBounds(SpotlightBorder, target);
        SetBounds(TargetBlocker, target);

        const double gap = 16;
        double width = Popover.Width;
        double height = Popover.ActualHeight > 0 ? Popover.ActualHeight : 300;
        TutorialPlacement placement = PickPlacement(_presentation.Placement, target, width, height);
        double x = placement switch
        {
            TutorialPlacement.Left => target.Left - width - gap,
            TutorialPlacement.Right => target.Right + gap,
            _ => target.Left + (target.Width - width) / 2
        };
        double y = placement switch
        {
            TutorialPlacement.Top => target.Top - height - gap,
            TutorialPlacement.Bottom => target.Bottom + gap,
            _ => target.Top + (target.Height - height) / 2
        };
        Canvas.SetLeft(Popover, Math.Clamp(x, 12, Math.Max(12, CoverWidth - width - 12)));
        Canvas.SetTop(Popover, Math.Clamp(y, 12, Math.Max(12, CoverHeight - height - 12)));
    }

    private TutorialPlacement PickPlacement(TutorialPlacement requested, Rect target, double width, double height)
    {
        if (requested != TutorialPlacement.Auto) return requested;
        if (target.Right + width + 16 <= CoverWidth) return TutorialPlacement.Right;
        if (target.Left - width - 16 >= 0) return TutorialPlacement.Left;
        if (target.Bottom + height + 16 <= CoverHeight) return TutorialPlacement.Bottom;
        return TutorialPlacement.Top;
    }

    private void RenderCentered()
    {
        double height = Popover.ActualHeight > 0 ? Popover.ActualHeight : 300;
        Canvas.SetLeft(Popover, Math.Max(12, (CoverWidth - Popover.Width) / 2));
        Canvas.SetTop(Popover, Math.Max(12, (CoverHeight - height) / 2));
    }

    private Geometry CreateDimGeometry(Rect cutout)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, Math.Max(0, CoverWidth), Math.Max(0, CoverHeight))));
        if (!cutout.IsEmpty) geometry.Children.Add(new RectangleGeometry(cutout, 10, 10));
        return geometry;
    }

    private static void SetBounds(FrameworkElement element, Rect bounds)
    {
        Canvas.SetLeft(element, bounds.Left);
        Canvas.SetTop(element, bounds.Top);
        element.Width = bounds.Width;
        element.Height = bounds.Height;
    }

    private void ApplyContrast()
    {
        DimPath.Fill = new SolidColorBrush(SystemParameters.HighContrast ? Color.FromArgb(230, 0, 0, 0) : Color.FromArgb(184, 0, 0, 0));
        SpotlightBorder.BorderBrush = SystemParameters.HighContrast ? Brushes.Yellow : new SolidColorBrush(Color.FromRgb(96, 205, 255));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Also hung on the app window, because the host is never activated and the keys
        // would otherwise never reach the tutorial. That makes every key in the app come
        // through here, so a tutorial that is not on screen must not take any of them.
        if (Visibility != Visibility.Visible) return;

        if (e.Key == Key.Escape && WelcomePanel.Visibility != Visibility.Visible)
        {
            StepPanel.Visibility = Visibility.Collapsed;
            CancelPanel.Visibility = Visibility.Visible;
            RenderCentered();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && StepPanel.Visibility == Visibility.Visible)
        {
            NextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void SkipButton_Click(object sender, RoutedEventArgs e) => SkipRequested?.Invoke(this, EventArgs.Empty);
    private void KeepLearning_Click(object sender, RoutedEventArgs e) { CancelPanel.Visibility = Visibility.Collapsed; StepPanel.Visibility = Visibility.Visible; RenderPresentation(); NextButton.Focus(); }
    private void ConfirmCancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
    private void WelcomeQuickTour_Click(object sender, RoutedEventArgs e) => QuickTourRequested?.Invoke(this, EventArgs.Empty);
    private void WelcomeChoose_Click(object sender, RoutedEventArgs e) => ChooseTutorialsRequested?.Invoke(this, EventArgs.Empty);
    private void WelcomeNotNow_Click(object sender, RoutedEventArgs e) => WelcomeDismissed?.Invoke(this, EventArgs.Empty);
}
