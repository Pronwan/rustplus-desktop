using System;
using System.Text.Json;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // The map toolbar's Grid, Players, Monuments and No Build switches stay visible above
    // the 3D view, so they drive the same layers there; toggling one inside the viewer
    // (its own toolbar, or fullscreen where this toolbar is hidden) moves the switch here.

    private void SyncMapLayersTo3D()
    {
        if (!_isMap3DActive || _map3DWebView?.CoreWebView2 == null) return;
        string json = JsonSerializer.Serialize(new
        {
            grid = ChkGrid?.IsChecked == true,
            players = ChkPlayers?.IsChecked != false,
            icons = Monuments?.IsChecked != false,
            noBuild = ChkNoBuildZones?.IsEnabled == true && ChkNoBuildZones.IsChecked == true
        });
        try
        {
            _ = _map3DWebView.CoreWebView2.ExecuteScriptAsync($"if (window.setThreeHostLayers) window.setThreeHostLayers({json});");
        }
        catch (Exception ex)
        {
            AppendLog($"[map3d] Could not sync map layers: {ex.Message}");
        }
    }

    // A layer_toggled message from the viewer: { layer: "grid" | "players" | "icons" | "noBuild", visible }.
    private void ApplyMap3DLayerToggle(JsonElement message)
    {
        if (!message.TryGetProperty("layer", out var layerProp) || !message.TryGetProperty("visible", out var visibleProp)) return;
        if (visibleProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        CheckBox? toggle = layerProp.GetString() switch
        {
            "grid" => ChkGrid,
            "players" => ChkPlayers,
            "icons" => Monuments,
            "noBuild" => ChkNoBuildZones,
            _ => null
        };
        if (toggle == null || !toggle.IsEnabled) return;
        // The Checked handlers forward the state back to the viewer, which ignores a layer
        // that already matches.
        toggle.IsChecked = visibleProp.GetBoolean();
    }
}
