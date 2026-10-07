using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

/// <summary>
/// Labelled rail: an optional wider rail that shows each tab's name beside its icon, for
/// people who would rather read than learn the icons. Off by default; toggled from the rail
/// and remembered. The width change is applied in one place (<see cref="ApplyRailLayout"/>)
/// so the panel beside the rail and the overlays that start at its edge move with it.
/// </summary>
public partial class MainWindow
{
    private double RailEntryWidth => RailWidth - 16;

    // Original icon content of the static bottom-rail buttons, so labels can be added and removed.
    private readonly Dictionary<WpfUi.Button, object?> _railStaticIcons = new();

    /// <summary>Sizes everything that sits against the rail to the current rail width.</summary>
    private void ApplyRailLayout()
    {
        double rail = RailWidth;
        CompactSidebarRail.Width = rail;
        LeftPanelBorder.MinWidth = rail;
        // The unfolded panel keeps its content width: the cap grows with the rail.
        LeftPanelBorder.MaxWidth = MaxExpandedSidebarWidth;
        ColSidebar.MaxWidth = MaxExpandedSidebarWidth;
        LeftPanelContent.Margin = new Thickness(rail + 8, 16, 12, 16);
        // Full-screen tools and left-panel overlays start at the rail's edge (DynamicResource).
        Resources["RailInset"] = new Thickness(rail, 0, 0, 0);
    }

    private void BtnToggleRailLabels_Click(object sender, RoutedEventArgs e)
    {
        double before = RailWidth;
        TrackingService.RailShowLabels = !TrackingService.RailShowLabels;

        // Keep the panel content as wide as it was: the rail took (or gave back) the difference.
        _expandedSidebarWidth = Math.Clamp(_expandedSidebarWidth + RailWidth - before,
            MinExpandedSidebarWidth, MaxExpandedSidebarWidth);

        ApplyRailLayout();
        RebuildRail();
        LabelStaticRailButtons();
        SetSidebarExpanded(_isSidebarExpanded);
    }

    /// <summary>
    /// The hover card for a rail button, or, when the rail shows names, a plain tooltip with the
    /// help text: the card would only repeat the name while covering the panel beside the rail.
    /// </summary>
    private void AttachRailHint(Panel host, WpfUi.Button button, Func<FrameworkElement, System.Windows.Controls.Primitives.Popup> buildPopover)
    {
        if (TrackingService.RailShowLabels)
        {
            button.SetBinding(ToolTipProperty, new Binding
            {
                Path = new PropertyPath(System.Windows.Automation.AutomationProperties.HelpTextProperty),
                Source = button,
            });
            return;
        }
        host.Children.Add(buildPopover(button));
    }

    /// <summary>Icon alone, or icon and name when the rail shows labels.</summary>
    private FrameworkElement WithRailLabel(Control owner, FrameworkElement icon, string? nameKey, string fallback)
    {
        if (!TrackingService.RailShowLabels) return icon;

        var label = new TextBlock
        {
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (nameKey is not null && TryFindResource(nameKey) is not null)
            label.SetResourceReference(TextBlock.TextProperty, nameKey);
        else
            label.Text = fallback;
        label.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = owner });

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(label);
        return panel;
    }

    /// <summary>Widens a rail button for its label; icon-only buttons keep the style's 44px square.</summary>
    private void SizeRailButton(WpfUi.Button button)
    {
        if (TrackingService.RailShowLabels)
        {
            button.Width = RailEntryWidth - 4;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(10, 0, 8, 0);
        }
        else
        {
            button.ClearValue(WidthProperty);
            button.ClearValue(HorizontalContentAlignmentProperty);
            button.ClearValue(PaddingProperty);
        }
    }

    /// <summary>Labels (or unlabels) the hand-placed buttons at the bottom of the rail.</summary>
    private void LabelStaticRailButtons()
    {
        bool labelled = TrackingService.RailShowLabels;
        string toggleKey = labelled ? "RailHideLabels" : "RailShowLabels";

        RailLabelsIcon.Symbol = labelled ? WpfUi.SymbolRegular.PanelLeftContract20 : WpfUi.SymbolRegular.PanelLeftExpand20;
        BtnToggleRailLabels.SetResourceReference(ToolTipProperty, toggleKey);
        BtnToggleRailLabels.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, toggleKey);

        LabelStaticRailButton(RailSocialButton, "SocialRailName", "Community");
        LabelStaticRailButton(RailTicketsButton, null, "Tickets");
        LabelStaticRailButton(BtnToggleRailLabels, toggleKey, labelled ? "Hide labels" : "Show labels");
        LabelStaticRailButton(BtnCompactPinSidebar, "UiPinSidebar", "Pin sidebar");
        LabelStaticRailButton(BtnRailSettings, "Settings", "Settings");
    }

    private void LabelStaticRailButton(WpfUi.Button button, string? nameKey, string fallback)
    {
        if (!_railStaticIcons.TryGetValue(button, out var icon))
        {
            icon = button.Content;
            _railStaticIcons[button] = icon;
        }

        // Detach the icon from the button or from a previous label row before reusing it.
        button.Content = null;
        if (icon is FrameworkElement { Parent: Panel previousRow } iconElement)
            previousRow.Children.Remove(iconElement);

        button.Content = icon is FrameworkElement element
            ? WithRailLabel(button, element, nameKey, fallback)
            : icon;
        SizeRailButton(button);
    }
}
