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
            var candidates = new List<string>();

            // 1) AppContext.BaseDirectory (Standard in .NET)
            candidates.Add(AppContext.BaseDirectory);

            // 2) Environment.ProcessPath (Location of the actual EXE)
            try
            {
                var procPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(procPath))
                {
                    var dir = Path.GetDirectoryName(procPath);
                    if (!string.IsNullOrEmpty(dir)) candidates.Add(dir);
                }
            }
            catch { }

            // 3) AppDomain.CurrentDomain.BaseDirectory
            candidates.Add(AppDomain.CurrentDomain.BaseDirectory);

            // 4) Current Working Directory
            candidates.Add(Directory.GetCurrentDirectory());

            // Deduplicate and normalize
            var pathsToTry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
            {
                if (!string.IsNullOrEmpty(c))
                {
                    try { pathsToTry.Add(Path.GetFullPath(c)); } catch { }
                }
            }

            foreach (var baseDir in pathsToTry)
            {
                // Try subfolder "runtime/node-win-x64/node.exe"
                var p = Path.Combine(baseDir, "runtime", "node-win-x64", "node.exe");
                if (File.Exists(p)) return p;

                // Try "node-win-x64/node.exe" (falls runtime-Ordner weggelassen wurde)
                var p2 = Path.Combine(baseDir, "node-win-x64", "node.exe");
                if (File.Exists(p2)) return p2;

                // Try "node.exe" directly (falls alles flach liegt)
                var p3 = Path.Combine(baseDir, "node.exe");
                if (File.Exists(p3)) return p3;
            }

            // 5) Debug Fallback: Deep search up for project root
            try
            {
                var cur = AppContext.BaseDirectory;
                for (int i = 0; i < 5; i++)
                {
                    var pDev = Path.Combine(cur, "runtime", "node-win-x64", "node.exe");
                    if (File.Exists(pDev)) return Path.GetFullPath(pDev);
                    
                    var next = Path.GetDirectoryName(cur);
                    if (string.IsNullOrEmpty(next) || next == cur) break;
                    cur = next;
                }
            }
            catch { }

            return null;
        }

        public static string GetNodeNotFoundMessage()
        {
            var msg = "Node.js Runtime not found.\n\nSearched locations:";
            
            var candidates = new List<string>();
            candidates.Add(AppContext.BaseDirectory);
            try { var p = Environment.ProcessPath; if (!string.IsNullOrEmpty(p)) candidates.Add(Path.GetDirectoryName(p) ?? ""); } catch { }
            candidates.Add(Directory.GetCurrentDirectory());

            var pathsToTry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in candidates)
            {
                if (!string.IsNullOrEmpty(c))
                {
                    try { pathsToTry.Add(Path.GetFullPath(c)); } catch { }
                }
            }

            foreach (var b in pathsToTry)
            {
                msg += $"\n- {Path.Combine(b, "runtime\\node-win-x64\\node.exe")}";
            }

            msg += "\n\nPlease ensure that Google Chrome or Microsoft Edge is installed and the 'runtime' folder exists in the application directory.";
            
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
