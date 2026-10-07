using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace RustPlusDesk.Controls;

/// <summary>
/// The shared header for panels, drawers and flyouts (template: Views/Themes/PanelHeader.xaml).
/// Hosts set the text, an icon (a Fluent symbol or an image), optional extra buttons in
/// <see cref="Actions"/>, and handle <see cref="CloseClick"/> with the close logic they already had.
/// </summary>
public class PanelHeader : Control
{
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), "");
    public static readonly DependencyProperty SubtitleProperty = Register(nameof(Subtitle), typeof(string), "");
    public static readonly DependencyProperty EyebrowProperty = Register(nameof(Eyebrow), typeof(string), "");
    public static readonly DependencyProperty SymbolProperty = Register(nameof(Symbol), typeof(SymbolRegular), SymbolRegular.Empty);
    public static readonly DependencyProperty ImageSourceProperty = Register(nameof(ImageSource), typeof(ImageSource), null);
    public static readonly DependencyProperty AccentBrushProperty = Register(nameof(AccentBrush), typeof(Brush), null);
    public static readonly DependencyProperty ActionsProperty = Register(nameof(Actions), typeof(object), null);
    public static readonly DependencyProperty IsCompactProperty = Register(nameof(IsCompact), typeof(bool), false);
    public static readonly DependencyProperty ShowCloseButtonProperty = Register(nameof(ShowCloseButton), typeof(bool), true);

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    public SymbolRegular Symbol { get => (SymbolRegular)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public ImageSource? ImageSource { get => (ImageSource?)GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>Drawer size (smaller tile and title). Off by default: full-screen page size.</summary>
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    public bool ShowCloseButton { get => (bool)GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }

    /// <summary>Raised by the close button. Same signature as a Click handler, so hosts keep theirs.</summary>
    public event RoutedEventHandler? CloseClick;

    private FrameworkElement? _iconTile;
    private SymbolIcon? _symbol;
    private Image? _image;
    private TextBlock? _eyebrow;
    private TextBlock? _title;
    private TextBlock? _subtitle;
    private System.Windows.Controls.Button? _close;

    public PanelHeader()
    {
        // Theme accent unless the host picks its own; a host value set in XAML replaces this.
        SetResourceReference(AccentBrushProperty, "Accent");
    }

    private static DependencyProperty Register(string name, System.Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(PanelHeader),
            new PropertyMetadata(defaultValue, (d, _) => ((PanelHeader)d).UpdateLayoutState()));

    public override void OnApplyTemplate()
    {
        if (_close is not null) _close.Click -= Close_Click;

        base.OnApplyTemplate();
        _iconTile = GetTemplateChild("PART_IconTile") as FrameworkElement;
        _symbol = GetTemplateChild("PART_Symbol") as SymbolIcon;
        _image = GetTemplateChild("PART_Image") as Image;
        _eyebrow = GetTemplateChild("PART_Eyebrow") as TextBlock;
        _title = GetTemplateChild("PART_Title") as TextBlock;
        _subtitle = GetTemplateChild("PART_Subtitle") as TextBlock;
        _close = GetTemplateChild("PART_CloseButton") as System.Windows.Controls.Button;

        if (_close is not null) _close.Click += Close_Click;
        UpdateLayoutState();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseClick?.Invoke(this, e);

    private void UpdateLayoutState()
    {
        if (_title is null) return; // template not applied yet

        bool compact = IsCompact;
        bool hasImage = ImageSource is not null;
        bool hasSymbol = Symbol != SymbolRegular.Empty;

        if (_iconTile is not null)
        {
            double tile = compact ? 32 : 40;
            _iconTile.Width = tile;
            _iconTile.Height = tile;
            _iconTile.Visibility = hasImage || hasSymbol ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_image is not null) _image.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        if (_symbol is not null)
        {
            _symbol.Visibility = !hasImage && hasSymbol ? Visibility.Visible : Visibility.Collapsed;
            _symbol.FontSize = compact ? 16 : 20;
        }

        // An eyebrow is a page-level label; drawers are named by their title alone.
        if (_eyebrow is not null)
            _eyebrow.Visibility = !compact && !string.IsNullOrWhiteSpace(Eyebrow) ? Visibility.Visible : Visibility.Collapsed;
        _title.FontSize = compact ? 15.5 : 20;
        if (_subtitle is not null)
        {
            _subtitle.FontSize = compact ? 11.5 : 12;
            _subtitle.Visibility = string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_close is not null) _close.Visibility = ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;
    }
}
