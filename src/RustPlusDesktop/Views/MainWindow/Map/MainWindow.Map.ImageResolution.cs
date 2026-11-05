using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Map;

namespace RustPlusDesk.Views;

/// <summary>
/// Which image the 2D map shows, and how large: the server's image or MapParser's render, each
/// decoded at the size <see cref="MapImageResolution"/> asks for.
/// </summary>
public partial class MainWindow
{
    /// <summary>The server's (or custom) map image at its own size, before any resolution is applied.</summary>
    private (int Width, int Height) _serverMapPixelSize;

    /// <summary>The answer when the person chose not to have it remembered: asked once a session.</summary>
    private bool? _renderedImageDecisionThisSession;

    /// <summary>
    /// Whether to show MapParser's render rather than the server's image: the remembered choice,
    /// this session's answer, or the person's answer to the question.
    /// </summary>
    private async Task<bool> DecideUseRenderedImageAsync(string renderPath)
    {
        if (TrackingService.MapUseRenderedImage is bool remembered) return remembered;
        if (_renderedImageDecisionThisSession is bool once) return once;

        var (renderWidth, renderHeight) = await Task.Run(() => MapImageLoader.ReadSize(renderPath));
        double fullMb = MapImagePolicy.EstimateMb(renderWidth, renderHeight)
                        - MapImagePolicy.EstimateMb(_serverMapPixelSize.Width, _serverMapPixelSize.Height);

        return await Dispatcher.InvokeAsync(() =>
        {
            var dialog = new Windows.MapHdImageWindow(this, MapImagePolicy.RoundForDisplay(Math.Max(0, fullMb)));
            bool useRender = dialog.ShowDialog() == true && dialog.UseRenderedImage;

            if (dialog.Remember) TrackingService.MapUseRenderedImage = useRender;
            else _renderedImageDecisionThisSession = useRender;

            AppendLog($"[map-render] HD map {(useRender ? "accepted" : "declined")}{(dialog.Remember ? " (remembered)" : "")}.");
            return useRender;
        });
    }

    /// <summary>MapParser's render, decoded at the chosen resolution.</summary>
    private BitmapImage LoadRenderedImage(string renderPath)
    {
        var (width, _) = MapImageLoader.ReadSize(renderPath);
        int decodeWidth = MapImagePolicy.RenderDecodeWidth(TrackingService.MapImageResolution, width, _serverMapPixelSize.Width);
        return MapImageLoader.Load(renderPath, decodeWidth);
    }

    /// <summary>
    /// A local map image (an offline map's, or one shown before the server's arrives), at the chosen
    /// resolution. There is nothing to choose between here, so nobody is asked.
    /// </summary>
    private BitmapImage LoadLocalMapImage(string path)
    {
        var (width, _) = MapImageLoader.ReadSize(path);
        int decodeWidth = IsParserRenderedImage(path)
            ? MapImagePolicy.RenderDecodeWidth(TrackingService.MapImageResolution, width, 0)
            : MapImagePolicy.ServerDecodeWidth(TrackingService.MapImageResolution, width);
        return MapImageLoader.Load(path, decodeWidth);
    }

    /// <summary>
    /// What each resolution would cost for the map showing now, for the settings page:
    /// (economy, standard, high) in MB, or null with no map loaded.
    /// </summary>
    internal (int Economy, int Standard, int High)? EstimateMapImageMemory()
    {
        var server = _serverMapPixelSize;
        (int Width, int Height) render = (0, 0);
        if (_vm?.Selected is { } profile && TrackingService.MapUseRenderedImage != false)
        {
            string path = System.IO.Path.Combine(
                Map3DLocalBuildService.GetPreparedFolderPath(profile, profile.RustMapsMapId),
                Map3DLocalBuildService.RenderedTextureFileName);
            if (System.IO.File.Exists(path)) render = MapImageLoader.ReadSize(path);
        }

        var source = render.Width > 0 ? render : server;
        if (source.Width <= 0) return null;

        double At(MapImageResolution resolution)
        {
            int decodeWidth = render.Width > 0
                ? MapImagePolicy.RenderDecodeWidth(resolution, render.Width, server.Width)
                : MapImagePolicy.ServerDecodeWidth(resolution, server.Width);
            var (w, h) = MapImagePolicy.Scaled(source.Width, source.Height, decodeWidth);
            return MapImagePolicy.EstimateMb(w, h);
        }

        return (MapImagePolicy.RoundForDisplay(At(MapImageResolution.Economy)),
                MapImagePolicy.RoundForDisplay(At(MapImageResolution.Standard)),
                MapImagePolicy.RoundForDisplay(At(MapImageResolution.High)));
    }
}
