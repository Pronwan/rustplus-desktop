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
        SizeChanged += (_, _) =>
        {
            RenderPresentation();
            UpdatePopupPosition();
            ResizePopupToOverlay();
        };
        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged += (s, e) => UpdatePopupPosition();
                window.SizeChanged += (s, e) => UpdatePopupPosition();

                // Maximising and restoring change where the client area starts without
                // always raising LocationChanged.
                window.StateChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdatePopupPosition();
                    ResizePopupToOverlay();
                }), DispatcherPriority.Loaded);
            }
        };

        OverlayPopup.Opened += (_, _) =>
        {
            UpdatePopupPosition();
            _popupSize = new Size(ActualWidth, ActualHeight);
        };
        PreviewKeyDown += OnPreviewKeyDown;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) ApplyContrast();
        };
    }

    /// <summary>The size the popup's own window was built for, so a stale one is noticed.</summary>
    private Size _popupSize = Size.Empty;

    /// <summary>
    /// Rebuilds the popup when the overlay has changed size under it.
    ///
    /// A Popup sizes its own window once, when it opens, and does not grow with its child.
    /// So maximising the app left the dimming at the size the window had when the tutorial
    /// started — covering the top of the screen and stopping part-way down, taking the
    /// popover's buttons with it at that same edge. Closing and reopening is the only thing
    /// that makes a popup take a new size; there is no property for it.
    ///
    /// Guarded by the size it was last built at, because this runs on every SizeChanged and a
    /// popup that closed and reopened for a one-pixel change would visibly blink.
    /// </summary>
    private void ResizePopupToOverlay()
    {
        if (!OverlayPopup.IsOpen) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        if (Math.Abs(_popupSize.Width - ActualWidth) < 1 &&
            Math.Abs(_popupSize.Height - ActualHeight) < 1) return;

        _popupSize = new Size(ActualWidth, ActualHeight);

        OverlayPopup.IsOpen = false;
        UpdatePopupPosition();
        OverlayPopup.IsOpen = true;
    }

    /// <summary>
    /// Pins the popup over this control, wherever on the desktop that is.
    ///
    /// It used to be placed relative to this control and nudged by a pixel to force a refresh.
    /// WPF reserves the right to move a relatively-placed popup it thinks will not fit, and a
    /// popup the size of a maximised window never fits — so it was moved, and on a
    /// multi-monitor desktop it landed on the neighbouring screen.
    ///
    /// AbsolutePoint is the one mode WPF positions and then leaves alone, so the offsets here
    /// are the whole answer: this control's top-left corner, in the device-independent units
    /// the popup measures its offsets in. Deliberately usable while the popup is closed, since
    /// the resize above sets the position between closing and reopening.
    /// </summary>
    private void UpdatePopupPosition()
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is null) return;

            Point device = PointToScreen(new Point(0, 0));
            Point dip = source.CompositionTarget.TransformFromDevice.Transform(device);

            OverlayPopup.HorizontalOffset = dip.X;
            OverlayPopup.VerticalOffset = dip.Y;
        }
        catch
        {
            // Between a window closing and its source going away this can throw; the overlay
            // is about to disappear with it either way.
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
        OverlayPopup.IsOpen = true;
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
        if (SystemParameters.ClientAreaAnimation)
        {
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
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
        OverlayPopup.IsOpen = true;
        FlowDirection = FlowDirection.LeftToRight;
        Popover.FlowDirection = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        RenderCentered();
        Dispatcher.BeginInvoke(() => Keyboard.Focus(WelcomePanel), DispatcherPriority.Loaded);
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        OverlayPopup.IsOpen = false;
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
        Canvas.SetLeft(Popover, Math.Clamp(x, 12, Math.Max(12, ActualWidth - width - 12)));
        Canvas.SetTop(Popover, Math.Clamp(y, 12, Math.Max(12, ActualHeight - height - 12)));
    }

    private TutorialPlacement PickPlacement(TutorialPlacement requested, Rect target, double width, double height)
    {
        if (requested != TutorialPlacement.Auto) return requested;
        if (target.Right + width + 16 <= ActualWidth) return TutorialPlacement.Right;
        if (target.Left - width - 16 >= 0) return TutorialPlacement.Left;
        if (target.Bottom + height + 16 <= ActualHeight) return TutorialPlacement.Bottom;
        return TutorialPlacement.Top;
    }

    private void RenderCentered()
    {
        double height = Popover.ActualHeight > 0 ? Popover.ActualHeight : 300;
        Canvas.SetLeft(Popover, Math.Max(12, (ActualWidth - Popover.Width) / 2));
        Canvas.SetTop(Popover, Math.Max(12, (ActualHeight - height) / 2));
    }

    private Geometry CreateDimGeometry(Rect cutout)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight))));
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
