using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class OverlaySpaceMapperTests
{
    // MapParser's render: the world edge to edge. The server's image: smaller, with an ocean margin.
    private static readonly Rect Render = new(0, 0, 4000, 4000);
    private static readonly Rect ServerImage = new(500, 500, 1000, 1000);

    [TestMethod]
    public void DrawingOnTheRender_LandsOnTheSameSpotOfTheServerImage()
    {
        var data = new OverlaySaveData
        {
            Space = OverlaySpace.From(Render),
            Strokes = { new SavedStroke { Points = { new Point(2000, 2000), new Point(4000, 0) }, Thickness = 8 } },
            Texts = { new SavedText { X = 400, Y = 800, FontSize = 40 } },
            Routes = { new SavedRoute { Points = { new Point(0, 4000) }, Thickness = 12 } },
        };

        OverlaySpaceMapper.MapTo(data, ServerImage);

        CollectionAssert.AreEqual(new[] { new Point(1000, 1000), new Point(1500, 500) }, data.Strokes[0].Points);
        Assert.AreEqual(2, data.Strokes[0].Thickness, 1e-9);
        Assert.AreEqual(new Point(600, 700), new Point(data.Texts[0].X, data.Texts[0].Y));
        Assert.AreEqual(10, data.Texts[0].FontSize, 1e-9);
        Assert.AreEqual(new Point(500, 1500), data.Routes[0].Points[0]);
        Assert.AreEqual(3, data.Routes[0].Thickness, 1e-9);
        Assert.AreEqual(ServerImage, data.Space!.ToRect());
    }

    [TestMethod]
    public void Icons_KeepTheSpotTheyMark()
    {
        var data = new OverlaySaveData
        {
            Space = OverlaySpace.From(Render),
            Icons = { new SavedIcon { X = 1980, Y = 1980, Width = 40, Height = 40 } },
        };

        OverlaySpaceMapper.MapTo(data, ServerImage);

        var icon = data.Icons[0];
        Assert.AreEqual(10, icon.Width, 1e-9);
        Assert.AreEqual(1000, icon.X + icon.Width / 2, 1e-9);
        Assert.AreEqual(1000, icon.Y + icon.Height / 2, 1e-9);
    }

    [TestMethod]
    public void BaseMarkers_UseTheirOwnSpace()
    {
        // The overlay was drawn on the server image; the base marker arrived separately from the render.
        var data = new OverlaySaveData
        {
            Space = OverlaySpace.From(ServerImage),
            Icons = { new SavedIcon { X = 1990, Y = 1990, Width = 20, Height = 20, Space = OverlaySpace.From(Render) } },
        };

        OverlaySpaceMapper.MapTo(data, ServerImage);

        Assert.AreEqual(1000, data.Icons[0].X + data.Icons[0].Width / 2, 1e-9);
        Assert.IsNull(data.Icons[0].Space, "Once mapped, the icon shares the overlay's space.");
    }

    [TestMethod]
    public void OverlaysWithoutASpace_AreTakenToFitTheCurrentImage()
    {
        var data = new OverlaySaveData
        {
            Strokes = { new SavedStroke { Points = { new Point(123, 456) }, Thickness = 3 } },
        };

        OverlaySpaceMapper.MapTo(data, ServerImage);

        Assert.AreEqual(new Point(123, 456), data.Strokes[0].Points[0]);
        Assert.AreEqual(3, data.Strokes[0].Thickness);
        Assert.AreEqual(ServerImage, data.Space!.ToRect());
    }

    [TestMethod]
    public void NoUsableTarget_LeavesEverythingAlone()
    {
        var data = new OverlaySaveData
        {
            Space = OverlaySpace.From(Render),
            Strokes = { new SavedStroke { Points = { new Point(10, 10) } } },
        };

        OverlaySpaceMapper.MapTo(data, Rect.Empty);

        Assert.AreEqual(new Point(10, 10), data.Strokes[0].Points[0]);
        Assert.AreEqual(Render, data.Space!.ToRect());
    }

    [TestMethod]
    public void Space_SurvivesJson_AndOlderPayloadsStillRead()
    {
        var data = new OverlaySaveData
        {
            Space = OverlaySpace.From(ServerImage),
            Icons = new List<SavedIcon> { new() { Space = OverlaySpace.From(Render) } },
        };

        var back = JsonSerializer.Deserialize<OverlaySaveData>(JsonSerializer.Serialize(data))!;
        Assert.AreEqual(ServerImage, back.Space!.ToRect());
        Assert.AreEqual(Render, back.Icons[0].Space!.ToRect());

        var old = JsonSerializer.Deserialize<OverlaySaveData>("{\"Strokes\":[],\"Icons\":[{\"X\":1,\"Y\":2}]}")!;
        Assert.IsNull(old.Space);
        Assert.IsNull(old.Icons[0].Space);
    }
}
