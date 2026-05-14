using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace RustPlusDesk.Helpers
{
    public static class RuntimeHelper
    {
        public static string? FindBundledNode()
        {
            // 1) Release/Publish: neben der EXE
            var baseDir = AppContext.BaseDirectory;
            var p1 = Path.Combine(baseDir, "runtime", "node-win-x64", "node.exe");
            if (File.Exists(p1)) return p1;

            // 1b) Fallback: Falls AppContext.BaseDirectory in Single-File nicht das ist, was wir wollen
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    var exeDir = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(exeDir) && exeDir != baseDir)
                    {
                        var p1b = Path.Combine(exeDir, "runtime", "node-win-x64", "node.exe");
                        if (File.Exists(p1b)) return p1b;
                    }
                }
            }
            catch { /* ignored */ }

            // 2) Debug: direkt aus dem Projekt
            var p2 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "runtime", "node-win-x64", "node.exe"));
            if (File.Exists(p2)) return p2;

            return null;
        }

        public static string GetNodeNotFoundMessage()
        {
            var baseDir = AppContext.BaseDirectory;
            var p1 = Path.Combine(baseDir, "runtime", "node-win-x64", "node.exe");
            var msg = $"Node.js Runtime not found.\nSearched at: {p1}";
            
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    var exeDir = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(exeDir) && exeDir != baseDir)
                    {
                        var p1b = Path.Combine(exeDir, "runtime", "node-win-x64", "node.exe");
                        msg += $"\nAlso searched at: {p1b}";
                    }
                }
            }
            catch { }
            
            return msg;
        }

        public static string EnsureCliUnpackedRoot()
        {
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "RustPlusDesk", "runtime", "rustplus-cli");
            Directory.CreateDirectory(target);

            // 1) Suche nach ZIP
            var zip = Path.Combine(AppContext.BaseDirectory, "runtime", "rustplus-cli.zip");
            
            // Fallback für Single-File
            if (!File.Exists(zip))
            {
                try
                {
                    var processPath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(processPath))
                    {
                        var exeDir = Path.GetDirectoryName(processPath);
                        if (!string.IsNullOrEmpty(exeDir))
                            zip = Path.Combine(exeDir, "runtime", "rustplus-cli.zip");
                    }
                }
                catch { }
            }

            if (File.Exists(zip))
            {
                var stamp = Path.Combine(target, ".stamp");
                var sig = $"{new FileInfo(zip).Length}-{File.GetLastWriteTimeUtc(zip).Ticks}";
                var need = !File.Exists(stamp) || File.ReadAllText(stamp) != sig
                           || !Directory.Exists(Path.Combine(target, "node_modules"));

                if (need)
                {
                    try { Directory.Delete(target, true); } catch { }
                    Directory.CreateDirectory(target);
                    ZipFile.ExtractToDirectory(zip, target);
                    File.WriteAllText(stamp, sig);
                }
                return target;
            }

            // 2) Debug-Fallback: ungezippter Ordner im Projekt
            var dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..",
                                                    "runtime", "rustplus-cli"));
            if (Directory.Exists(dev)) return dev;

            throw new FileNotFoundException("rustplus-cli not found (neither ZIP in output nor Dev Folder).\nSearched ZIP at: " + zip);
        }

        public static string? ResolveCliEntry(out string workingDir)
        {
            var root = EnsureCliUnpackedRoot();
            workingDir = root;

            foreach (var c in new[] {
                Path.Combine(root, "cli.js"),
                Path.Combine(root, "rustplus.js"),
                Path.Combine(root, "index.js"),
                Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js", "cli", "index.js")
            })
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        public static string? FindRustplusJsPackageRoot()
        {
            // wir brauchen den Ordner, der die *node_modules* enthält
            var root = EnsureCliUnpackedRoot();
            
            if (Directory.Exists(Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js")))
                return root;
            
            return null;
        }
    }
}
