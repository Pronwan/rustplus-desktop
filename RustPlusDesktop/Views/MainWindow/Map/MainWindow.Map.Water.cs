using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services.Deaths;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private MapWaterMask? _waterMask;
    private BitmapSource? _waterMaskSource;
    private double _waterMaskWorldSize;
    private double _waterMaskPad;

    /// <summary>
    /// Whether a world position lies on water. False while no map is loaded, so a position
    /// simply stays "open" instead of being guessed.
    /// </summary>
    private bool IsWaterAt(double x, double y)
    {
        try
        {
            return GetWaterMask()?.IsWater(x, y) == true;
        }
        catch
        {
            // Classification must never break the team refresh.
            return false;
        }
    }

    /// <summary>
    /// Built on first use for each map image, then reused; a new map (wipe, server switch,
    /// custom map) produces a new bitmap and so a new mask.
    /// </summary>
    private MapWaterMask? GetWaterMask()
    {
        var bmp = _mapBaseBmp;
        if (bmp == null || _worldSizeS <= 0)
            return null;

        double pad = GetCurrentMapPaddingWorld();
        if (_waterMask != null
            && ReferenceEquals(bmp, _waterMaskSource)
            && _waterMaskWorldSize == _worldSizeS
            && _waterMaskPad == pad)
            return _waterMask;

        BitmapSource source = bmp.Format == PixelFormats.Bgra32
            ? bmp
            : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        // The pixel copy goes out of scope here; only the per-cell bits are kept.
        _waterMask = MapWaterMask.Build(pixels, width, height, stride, _worldSizeS, pad);
        _waterMaskSource = bmp;
        _waterMaskWorldSize = _worldSizeS;
        _waterMaskPad = pad;
        return _waterMask;
    }
}
