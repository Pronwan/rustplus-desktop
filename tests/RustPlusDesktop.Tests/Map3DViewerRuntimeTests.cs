using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public class Map3DViewerRuntimeTests
{
    [DataTestMethod]
    [DataRow("modules/glbWorker.js", false)]
    [DataRow(@"MODULES\GLBWORKER.JS", false)]
    [DataRow("modules/glbWorkerClient.js", true)]
    [DataRow("modules/other.js", true)]
    [DataRow("node_modules/three/index.js", true)]
    [DataRow(".git/config", true)]
    [DataRow("maps/private.json", true)]
    [DataRow("vendor/three/GLTFLoader.js", false)]
    [DataRow("vendor/three/draco/draco_decoder.wasm", false)]
    [DataRow("Rust_Assets/FP/model.glb", false)]
    public void RuntimeFilter_KeepsWorkerAndModelsWithoutCopyingDevelopmentSources(string path, bool ignored)
    {
        Assert.AreEqual(ignored, MainWindow.IsIgnoredRuntimePath(path));
    }

    [TestMethod]
    public void RuntimeCopy_PreservesWorkerDependenciesAndModels_OnRepeatedPreparation()
    {
        string folder = Path.Combine(Path.GetTempPath(), "RustPlusDesk-ViewerTest-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(folder, "source");
        string target = Path.Combine(folder, "runtime");
        string[] runtimeFiles = ["index.html", "assets/manifest.json", "modules/glbWorker.js", "vendor/three/GLTFLoader.js", "vendor/three/draco/draco_decoder.wasm", "Rust_Assets/FP/model.glb"];
        string[] developmentFiles = ["modules/glbWorkerClient.js", "node_modules/esbuild/index.js", "maps/private.json", ".git/config", "app.js"];
        try
        {
            foreach (string relative in runtimeFiles.Concat(developmentFiles))
            {
                string file = Path.Combine(source, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, relative);
            }
            MainWindow.CopyDirectoryIfExists(source, target);
            MainWindow.CopyDirectoryIfExists(source, target);
            foreach (string relative in runtimeFiles)
                Assert.AreEqual(relative, File.ReadAllText(Path.Combine(target, relative)), relative);
            foreach (string relative in developmentFiles)
                Assert.IsFalse(File.Exists(Path.Combine(target, relative)), relative);

            File.WriteAllText(Path.Combine(source, "modules/glbWorker.js"), "updated worker");
            MainWindow.CopyDirectoryIfExists(source, target);
            Assert.AreEqual("updated worker", File.ReadAllText(Path.Combine(target, "modules/glbWorker.js")));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
