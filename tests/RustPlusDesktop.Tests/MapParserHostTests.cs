using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MapParserHostTests
{
    [TestMethod]
    public void TryRun_LeavesOrdinaryStartsToTheApp()
    {
        Assert.IsFalse(MapParserHost.TryRun(Array.Empty<string>(), out _));
        Assert.IsFalse(MapParserHost.TryRun(new[] { "--background" }, out _));
        Assert.IsFalse(MapParserHost.TryRun(new[] { "rustplus://pair", MapParserHost.Flag }, out _),
            "Only the first argument may select the parser; a link's arguments never can.");
    }

    [TestMethod]
    public void StartInfo_RunsThisProcessWithTheFlagFirst_InTheParserFolder()
    {
        string parser = Path.Combine(Path.GetTempPath(), "MapParser", "MapParser.dll");

        var psi = MapParserHost.CreateStartInfo(parser, new[] { "map file.map", "--output-dir", "out" });

        Assert.AreEqual(Environment.ProcessPath, psi.FileName);
        Assert.AreEqual(Path.GetDirectoryName(parser), psi.WorkingDirectory);
        CollectionAssert.AreEqual(new[] { MapParserHost.Flag, "map file.map", "--output-dir", "out" }, psi.ArgumentList.ToArray());
        Assert.IsTrue(psi.RedirectStandardOutput && psi.RedirectStandardError && psi.CreateNoWindow);
    }

    [TestMethod]
    public void Resolve_PrefersTheInstalledFolder_ThenASiblingBuild()
    {
        string root = Path.Combine(Path.GetTempPath(), "rpd-parser-" + Guid.NewGuid().ToString("N"));
        string appDir = Path.Combine(root, "src", "bin");
        Directory.CreateDirectory(appDir);
        try
        {
            Assert.IsNull(MapParserHost.ResolveParserAssembly(appDir));

            string sibling = Path.Combine(root, "MapParser", "bin", "Release", "net8.0", "win-x64", "publish", "MapParser.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
            File.WriteAllText(sibling, "");
            File.WriteAllText(Path.Combine(root, "MapParser", "MapParser.csproj"), "");
            Assert.AreEqual(sibling, MapParserHost.ResolveParserAssembly(appDir));

            string installed = Path.Combine(appDir, "MapParser", "MapParser.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.WriteAllText(installed, "");
            Assert.AreEqual(installed, MapParserHost.ResolveParserAssembly(appDir));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
