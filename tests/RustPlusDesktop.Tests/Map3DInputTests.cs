using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public class Map3DInputTests
{
    [TestMethod]
    public void PointerLockNotice_OnlyMatchesTheLocalViewerReminder()
    {
        Assert.IsTrue(Map3DPointerLockNotice.IsPointerLockNotice(new[] { "rustplus3d.local", "To show your cursor, press", "Esc" }));
        Assert.IsTrue(Map3DPointerLockNotice.IsPointerLockNotice(new[] { "rustplus3d.local - To show your cursor, press Esc" }));
        Assert.IsFalse(Map3DPointerLockNotice.IsPointerLockNotice(new[] { "example.com", "To show your cursor, press Esc" }));
        Assert.IsFalse(Map3DPointerLockNotice.IsPointerLockNotice(new[] { "rustplus3d.local", "Allow notifications?" }));
        Assert.IsFalse(Map3DPointerLockNotice.IsPointerLockNotice(new[] { "rustplus3d.local", "Press Esc to exit full screen" }));
    }

    [STATestMethod]
    public void NativeViewport_ClipsOnlyDrawerPixels_WithoutResizingTheWindow()
    {
        using var source = new HwndSource(new HwndSourceParameters("3D viewport clip test") { Width = 300, Height = 200 });
        Assert.IsTrue(GetWindowRect(source.Handle, out var before));
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            foreach (int left in new[] { 40, 180, 40 })
            {
                Assert.IsTrue(MainWindow.ApplyMap3DWindowRegion(source.Handle, left, 300, 200));
                Assert.AreEqual(2, GetWindowRgn(source.Handle, region));
                Assert.AreEqual(2, GetRgnBox(region, out var bounds));
                Assert.AreEqual(left, bounds.Left);
                Assert.AreEqual(300, bounds.Right);
                Assert.AreEqual(200, bounds.Bottom);
                Assert.IsTrue(GetWindowRect(source.Handle, out var after));
                Assert.AreEqual(before, after);
            }
            Assert.IsTrue(MainWindow.ApplyMap3DWindowRegion(source.Handle, 0, 300, 200));
            Assert.AreEqual(0, GetWindowRgn(source.Handle, region));
        }
        finally { DeleteObject(region); }
        Assert.AreEqual(0, MainWindow.GetMap3DLeftClip(-1800, 1000, -1900));
        Assert.AreEqual(350, MainWindow.GetMap3DLeftClip(-1800, 1000, -1450));
        Assert.AreEqual(1000, MainWindow.GetMap3DLeftClip(-1800, 1000, 0));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern int GetRgnBox(IntPtr region, out NativeRect rect);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr region);

    [STATestMethod]
    public void Active3DView_LeavesMouseAndKeyboardInputUntouched()
    {
        // Avoid starting accounts, network services and the full window for an input routing test.
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_isMap3DActive", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_isPanning", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_spacePanHeld", flags)!.SetValue(window, true);
        using var source = new HwndSource(new HwndSourceParameters("3D input test") { Width = 1, Height = 1 });
        var inputs = new (string Handler, InputEventArgs Args)[]
        {
            ("MouseDown", new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)),
            ("MouseMove", new MouseEventArgs(Mouse.PrimaryDevice, 0)),
            ("MouseUp", new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)),
            ("MouseWheel", new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)),
            ("KeyDown", new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)),
            ("KeyUp", new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space))
        };

        foreach (var (handler, args) in inputs)
        {
            typeof(MainWindow).GetMethod("WebViewHost_" + handler, flags)!.Invoke(window, new object[] { window, args });
            Assert.IsFalse(args.Handled, handler + " must remain available to the 3D browser.");
        }
        Assert.AreEqual(true, typeof(MainWindow).GetField("_isPanning", flags)!.GetValue(window));
        Assert.AreEqual(true, typeof(MainWindow).GetField("_spacePanHeld", flags)!.GetValue(window));
    }
}
