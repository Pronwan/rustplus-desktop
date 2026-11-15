using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RustPlusDesk.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// A modded server's upkeep cost and oil rig crate time, set by the user.
///
/// Rust+ reports a cupboard's protection as vanilla upkeep costs would buy it, so on a server
/// with doubled upkeep the app showed twice the time the base really has; and a modded crate
/// can open after 10 minutes instead of 15. Neither can be read from the game.
/// </summary>
public partial class ServerModifiersDialog : FluentWindow
{
    private static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

    public int UpkeepPercent { get; private set; }
    public int OilRigCrateMinutes { get; private set; }

    public ServerModifiersDialog(ServerProfile profile)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(profile);

        TxtServerName.Text = profile.Name;
        TxtUpkeep.Text = profile.UpkeepPercent.ToString(CultureInfo.InvariantCulture);
        TxtCrate.Text = profile.OilRigCrateMinutes.ToString(CultureInfo.InvariantCulture);
        UpdateHints();

        try { ApplicationThemeManager.Apply(this); } catch { }
    }

    private static bool TryRead(string? text, int min, int max, out int value)
        => int.TryParse((text ?? "").Trim().TrimEnd('%').Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
           && value >= min && value <= max;

    private bool TryReadUpkeep(out int value)
        => TryRead(TxtUpkeep.Text, ServerProfile.MinUpkeepPercent, ServerProfile.MaxUpkeepPercent, out value);

    private bool TryReadCrate(out int value)
        => TryRead(TxtCrate.Text, ServerProfile.MinOilRigCrateMinutes, ServerProfile.MaxOilRigCrateMinutes, out value);

    /// <summary>The line under each field: what the number means compared with vanilla.</summary>
    private void UpdateHints()
    {
        if (TxtUpkeepHint == null || TxtCrateHint == null) return;

        bool upkeepOk = TryReadUpkeep(out var upkeep);
        TxtUpkeepHint.Text = upkeepOk
            ? string.Format(CultureInfo.CurrentCulture, Properties.Resources.GetString("ServerModifiersUpkeepFactor"),
                (upkeep / 100.0).ToString("0.##", CultureInfo.CurrentCulture))
            : Properties.Resources.GetString("ServerModifiersUpkeepRange");
        TxtUpkeepHint.Foreground = upkeepOk ? HintBrush : ErrorBrush;

        bool crateOk = TryReadCrate(out var crate);
        TxtCrateHint.Text = !crateOk
            ? Properties.Resources.GetString("ServerModifiersCrateRange")
            : crate == ServerProfile.VanillaOilRigCrateMinutes
                ? Properties.Resources.GetString("ServerModifiersCrateVanilla")
                : string.Format(CultureInfo.CurrentCulture, Properties.Resources.GetString("ServerModifiersCrateCustom"), crate);
        TxtCrateHint.Foreground = crateOk ? HintBrush : ErrorBrush;

        if (BtnSave != null) BtnSave.IsEnabled = upkeepOk && crateOk;
    }

    private void Inputs_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateHints();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        TxtUpkeep.Text = ServerProfile.VanillaUpkeepPercent.ToString(CultureInfo.InvariantCulture);
        TxtCrate.Text = ServerProfile.VanillaOilRigCrateMinutes.ToString(CultureInfo.InvariantCulture);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadUpkeep(out var upkeep) || !TryReadCrate(out var crate)) return;
        UpkeepPercent = upkeep;
        OilRigCrateMinutes = crate;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
