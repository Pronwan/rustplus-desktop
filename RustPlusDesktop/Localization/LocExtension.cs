using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace RustPlusDesk.Localization;

/// <summary>
/// XAML markup extension for inline localization. Usage:
///
///   xmlns:l="clr-namespace:RustPlusDesk.Localization"
///   &lt;TextBlock Text="{l:Loc settings.title}"/&gt;
///   &lt;Button Content="{l:Loc Key=common.close}"/&gt;
///
/// Resolves to a one-way Binding against LocalizationManager.Instance[Key],
/// so changing the language live refreshes every bound element automatically.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = $"[{Key}]"
        };
        return binding.ProvideValue(serviceProvider);
    }
}
