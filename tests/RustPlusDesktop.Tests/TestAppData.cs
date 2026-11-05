using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Data;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Gives the whole test run its own app data folder.
///
/// Some tests open the real mini-map and widget windows, which save their positions as they
/// close. Run from a developer's machine they wrote into that developer's own settings: every
/// run moved the widget overlay and parked the mini-map at -2000,-500, and the windows flashed
/// across each monitor while it happened.
/// </summary>
[TestClass]
public static class TestAppData
{
    private static string? _folder;

    [AssemblyInitialize]
    public static void Redirect(TestContext _)
    {
        _folder = Path.Combine(Path.GetTempPath(), "rpd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        DataManager.AppDirOverride = _folder;
    }

    [AssemblyCleanup]
    public static void Remove()
    {
        DataManager.AppDirOverride = null;
        try { if (_folder != null) Directory.Delete(_folder, recursive: true); }
        catch { /* a window may still hold a file; the temp folder is the OS's to clear */ }
    }
}
