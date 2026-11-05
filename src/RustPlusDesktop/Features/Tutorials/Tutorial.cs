using System.Windows;

namespace RustPlusDesk.Features.Tutorials;

public static class Tutorial
{
    public static readonly DependencyProperty TargetIdProperty = DependencyProperty.RegisterAttached(
        "TargetId", typeof(string), typeof(Tutorial), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.RegisterAttached(
        "Padding", typeof(Thickness), typeof(Tutorial), new FrameworkPropertyMetadata(new Thickness(8)));
    public static readonly DependencyProperty PreferredPlacementProperty = DependencyProperty.RegisterAttached(
        "PreferredPlacement", typeof(TutorialPlacement), typeof(Tutorial), new FrameworkPropertyMetadata(TutorialPlacement.Auto));
    public static readonly DependencyProperty AllowInteractionProperty = DependencyProperty.RegisterAttached(
        "AllowInteraction", typeof(bool), typeof(Tutorial), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IgnoreProperty = DependencyProperty.RegisterAttached(
        "Ignore", typeof(bool), typeof(Tutorial), new FrameworkPropertyMetadata(false));

    public static void SetTargetId(DependencyObject element, string? value) => element.SetValue(TargetIdProperty, value);
    public static string? GetTargetId(DependencyObject element) => (string?)element.GetValue(TargetIdProperty);
    public static void SetPadding(DependencyObject element, Thickness value) => element.SetValue(PaddingProperty, value);
    public static Thickness GetPadding(DependencyObject element) => (Thickness)element.GetValue(PaddingProperty);
    public static void SetPreferredPlacement(DependencyObject element, TutorialPlacement value) => element.SetValue(PreferredPlacementProperty, value);
    public static TutorialPlacement GetPreferredPlacement(DependencyObject element) => (TutorialPlacement)element.GetValue(PreferredPlacementProperty);
    public static void SetAllowInteraction(DependencyObject element, bool value) => element.SetValue(AllowInteractionProperty, value);
    public static bool GetAllowInteraction(DependencyObject element) => (bool)element.GetValue(AllowInteractionProperty);
    public static void SetIgnore(DependencyObject element, bool value) => element.SetValue(IgnoreProperty, value);
    public static bool GetIgnore(DependencyObject element) => (bool)element.GetValue(IgnoreProperty);
}
