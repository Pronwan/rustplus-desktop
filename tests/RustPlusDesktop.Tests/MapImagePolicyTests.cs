using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Map;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MapImagePolicyTests
{
    [TestMethod]
    public void Render_IsDecodedAtTheServerImageSizeByDefault()
    {
        Assert.AreEqual(3375, MapImagePolicy.RenderDecodeWidth(MapImageResolution.Standard, 4750, 3375));
        Assert.AreEqual(MapImagePolicy.StandardFallbackWidth, MapImagePolicy.RenderDecodeWidth(MapImageResolution.Standard, 4750, 0),
            "An offline map has no server image to match.");
        Assert.AreEqual(0, MapImagePolicy.RenderDecodeWidth(MapImageResolution.Standard, 3000, 3375), "Never decoded larger than the file.");
    }

    [TestMethod]
    public void Economy_CapsBothImages_AndHighLeavesThemWhole()
    {
        Assert.AreEqual(2048, MapImagePolicy.RenderDecodeWidth(MapImageResolution.Economy, 4750, 3375));
        Assert.AreEqual(2048, MapImagePolicy.ServerDecodeWidth(MapImageResolution.Economy, 3375));
        Assert.AreEqual(0, MapImagePolicy.ServerDecodeWidth(MapImageResolution.Economy, 2000));
        Assert.AreEqual(0, MapImagePolicy.ServerDecodeWidth(MapImageResolution.Standard, 3375));
        Assert.AreEqual(0, MapImagePolicy.RenderDecodeWidth(MapImageResolution.High, 4750, 3375));
    }

    [TestMethod]
    public void Estimate_MatchesWhatWasMeasured()
    {
        // A 4750 px render measured ~120 MB above a 3375 px server image, ~30 MB of it parsed overlays.
        double extra = MapImagePolicy.EstimateMb(4750, 4750) - MapImagePolicy.EstimateMb(3375, 3375);
        Assert.AreEqual(90, MapImagePolicy.RoundForDisplay(extra));
        Assert.AreEqual(10, MapImagePolicy.RoundForDisplay(0), "Never shown as free.");
        Assert.AreEqual((2048, 1024), MapImagePolicy.Scaled(4096, 2048, 2048));
        Assert.AreEqual((3000, 3000), MapImagePolicy.Scaled(3000, 3000, 0));
    }
}
