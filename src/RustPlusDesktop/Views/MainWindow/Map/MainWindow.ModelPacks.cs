using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RustPlusDesk.Services.ModelPacks;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private const string BuilderPack = "builder";
    private const string MonumentsPack = "monuments";
    private const string TerrainPack = "terrain";

    // Packs turned down this session, so the 3D map does not ask every time it opens.
    private readonly HashSet<string> _declinedModelPacks = new();
    private bool _modelDownloadShown;

    /// <summary>
    /// Offers the packs that are missing or out of date. Returns false only when a pack is
    /// <paramref name="required"/> and still not installed afterwards. A required pack sent to the
    /// background is waited for here, behind the feature's own loading screen.
    /// </summary>
    private async Task<bool> EnsureModelPacksAsync(string heading, string message, bool required, params string[] packs)
    {
        ModelPackService service = ModelPackService.Instance;
        IReadOnlyList<ModelPackStatus> statuses = await service.GetStatusAsync(packs);
        var offer = statuses
            .Where(pack => pack.MissingFiles > 0 && !service.IsDownloadingPack(pack.Id)
                && ((required && !pack.Installed) || !_declinedModelPacks.Contains(pack.Id)))
            .ToList();

        if (offer.Count > 0)
        {
            var window = new ModelPackWindow(heading, message, offer) { Owner = this };
            if (window.ShowDialog() != true && !window.MovedToBackground)
                _declinedModelPacks.UnionWith(offer.Select(pack => pack.Id));
        }

        if (!required) return true;

        try { await service.WaitForAsync(packs); }
        catch (Exception ex) { AppendLog($"[models] {ex.Message}"); }

        statuses = await service.GetStatusAsync(packs);
        return statuses.Count == packs.Length && statuses.All(pack => pack.Installed);
    }

    /// <summary>One offer on a new lite install, once the window has settled. Saying no here does not count against later prompts.</summary>
    internal async Task OfferModelPacksAtFirstRunAsync()
    {
        ModelPackService.Instance.DownloadChanged += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshModelDownload));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            if (!await ModelPackService.Instance.TakeFirstRunOfferAsync()) return;
            await EnsureModelPacksAsync(
                "Get the 3D models",
                "RustPlus Desktop works without them. They power the 3D map and the builders, and you can download them later when you open one.",
                false, BuilderPack, MonumentsPack, TerrainPack);
            _declinedModelPacks.Clear();
        }
        catch (Exception ex)
        {
            AppendLog($"[models] {ex.Message}");
        }
    }

    // The title bar ring stands in for the window while a download runs in the background.
    private void RefreshModelDownload()
    {
        ModelPackService service = ModelPackService.Instance;
        bool downloading = service.IsDownloading;
        RingModelDownload.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        IconModelDownload.Visibility = downloading ? Visibility.Collapsed : Visibility.Visible;
        if (!downloading) BtnModelDownload.ToolTip = "3D models";

        if (service.CurrentProgress is { } update)
        {
            string text = $"{ModelPackWindow.FormatBytes(update.DoneBytes)} of {ModelPackWindow.FormatBytes(update.TotalBytes)}";
            RingModelDownload.Progress = update.TotalBytes > 0 ? update.DoneBytes * 100.0 / update.TotalBytes : 0;
            BtnModelDownload.ToolTip = "Downloading 3D models: " + text;
            if (_baseBuilderOpening) BaseBuilderLoadingText.Text = "Downloading building models: " + text;
        }

        if (_modelDownloadShown && !downloading)
        {
            if (service.LastError is { } error) ShowInfoSnackbar("3D models", "The download failed: " + error, WpfUi.ControlAppearance.Danger);
            else ShowInfoSnackbar("3D models", "Your download is finished. Reopen the 3D map to see new models.", WpfUi.ControlAppearance.Success);
        }
        _modelDownloadShown = downloading;
    }

    // Always in the title bar, so the packs are one click away whatever the user turned down earlier.
    private async void BtnModelDownload_Click(object sender, RoutedEventArgs e)
    {
        ModelPackService service = ModelPackService.Instance;
        ModelPackWindow window = service.IsDownloading
            ? ModelPackWindow.ForRunningDownload()
            : new ModelPackWindow(
                "3D models",
                "These power the 3D map and the builders. Download what you want, whenever you want.",
                await service.GetStatusAsync(new[] { BuilderPack, MonumentsPack, TerrainPack }));
        window.Owner = this;
        window.ShowDialog();
    }

    private static async Task ClearReplacedModelsFromCacheAsync(CoreWebView2 core)
    {
        if (ModelPackService.Instance.TakeViewerCacheStale())
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
    }
}
