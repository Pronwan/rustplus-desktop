using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows.Media.Imaging;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services;

public sealed class Map3DLocalBuildResult
{
    public string FolderPath { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string? MapTexturePath { get; init; }
    public string? MapFilePath { get; init; }
    public bool ParserReady { get; init; }
    public bool ReusedCachedData { get; init; }
    public int CandidateCount { get; init; }
    public int AttemptCount { get; init; }
    public bool NeedsManualMapSelection { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public readonly record struct Map3DReferenceMonument(double X, double Y, string Name);

public static class Map3DLocalBuildService
{
    private const int RecentCandidateCount = 7;
    private const int NameCandidateCount = 5;
    private const double MatchDistanceWorldUnits = 300.0;

    public static async Task<Map3DLocalBuildResult> PrepareAsync(
        ServerProfile profile,
        BitmapSource? currentMapTexture,
        string? rustMapsMapId,
        IReadOnlyList<Map3DReferenceMonument>? referenceMonuments = null,
        int worldSize = 0,
        string? explicitMapPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string serverKey = BuildServerKey(profile, rustMapsMapId);
        string folder = Path.Combine(DataManager.AppDir, "3DMaps", serverKey);
        Directory.CreateDirectory(folder);

        string? texturePath = null;
        string? pendingTexturePath = null;
        string? textureSha256 = null;
        if (currentMapTexture != null)
        {
            string pendingDir = Path.Combine(folder, "parser_attempts");
            Directory.CreateDirectory(pendingDir);
            pendingTexturePath = Path.Combine(pendingDir, "current_map_texture.png");
            await SavePngAsync(currentMapTexture, pendingTexturePath, ct).ConfigureAwait(false);
            textureSha256 = await ComputeFileSha256Async(pendingTexturePath, ct).ConfigureAwait(false);
        }

        string manifestPath = Path.Combine(folder, "map3d_manifest.json");
        if (explicitMapPath == null && TryLoadReusableResult(folder, manifestPath, rustMapsMapId, textureSha256, out var cached))
        {
            if (pendingTexturePath != null)
            {
                texturePath = Path.Combine(folder, "map_texture.png");
                File.Copy(pendingTexturePath, texturePath, overwrite: true);
            }

            return new Map3DLocalBuildResult
            {
                FolderPath = folder,
                ManifestPath = manifestPath,
                MapTexturePath = texturePath ?? Path.Combine(folder, "map_texture.png"),
                MapFilePath = cached.SelectedMapFile,
                ParserReady = true,
                ReusedCachedData = true,
                CandidateCount = 0,
                AttemptCount = 0,
                NeedsManualMapSelection = false,
                StatusMessage = "Existing 3D map data reused; no wipe detected."
            };
        }

        var parserPath = ResolveParserExecutable();
        var candidates = explicitMapPath == null
            ? FindMapCandidates(profile.Name, profile.Host).ToList()
            : new List<string> { explicitMapPath };

        var attempts = new List<object>();
        string? selectedMap = null;
        MapMatchScore bestScore = default;
        string status;

        if (parserPath == null)
        {
            status = "3D map workspace prepared, but MapParser.exe was not found.";
        }
        else
        {
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                string candidateOutput = Path.Combine(folder, "parser_attempts", SanitizeFileName(Path.GetFileNameWithoutExtension(candidate)));
                Directory.CreateDirectory(candidateOutput);

                var parse = await RunParserAsync(parserPath, candidate, candidateOutput, ct).ConfigureAwait(false);
                var score = parse.Success
                    ? ScoreParsedMap(Path.Combine(candidateOutput, "map_resolved.json"), referenceMonuments, worldSize)
                    : default;

                attempts.Add(new
                {
                    MapFile = candidate,
                    parse.Success,
                    parse.ExitCode,
                    parse.Error,
                    score.MatchedCount,
                    score.TotalDistance,
                    Output = candidateOutput
                });

                if (score.IsBetterThan(bestScore)) bestScore = score;

                if (parse.Success && IsGoodMatch(score, referenceMonuments))
                {
                    selectedMap = candidate;
                    CopyParserOutput(candidateOutput, folder);
                    texturePath = PromotePendingTexture(pendingTexturePath, folder);
                    break;
                }
            }

            if (selectedMap == null && explicitMapPath != null && candidates.Count == 1)
            {
                string candidateOutput = Path.Combine(folder, "parser_attempts", SanitizeFileName(Path.GetFileNameWithoutExtension(explicitMapPath)));
                if (File.Exists(Path.Combine(candidateOutput, "map_resolved.json")))
                {
                    selectedMap = explicitMapPath;
                    CopyParserOutput(candidateOutput, folder);
                    texturePath = PromotePendingTexture(pendingTexturePath, folder);
                }
            }

            status = selectedMap != null
                ? "3D map data extracted and staged."
                : candidates.Count == 0
                    ? "No local .map candidates were found automatically."
                    : explicitMapPath != null
                        ? $"Failed to parse the selected map. Check 'parser_log.txt' in {Path.Combine(folder, "parser_attempts")} for errors."
                        : $"No matching local .map file was found automatically. Check 'parser_log.txt' in {Path.Combine(folder, "parser_attempts")} for errors.";
        }

        if (selectedMap != null && texturePath == null) texturePath = PromotePendingTexture(pendingTexturePath, folder);

        if (selectedMap != null)
        {
            try
            {
                string attemptsDir = Path.Combine(folder, "parser_attempts");
                if (Directory.Exists(attemptsDir)) Directory.Delete(attemptsDir, true);
            }
            catch { }
        }

        var manifest = new
        {
            profile.Name,
            profile.Host,
            profile.Port,
            RustMapsMapId = rustMapsMapId,
            PreparedAtUtc = DateTime.UtcNow,
            MapTexture = texturePath == null ? null : Path.GetFileName(texturePath),
            MapTextureSha256 = textureSha256,
            SelectedMapFile = selectedMap,
            ParserExecutable = parserPath,
            BestMatch = new { bestScore.MatchedCount, bestScore.TotalDistance },
            Attempts = attempts,
            ParserOutput = new
            {
                MapData = "map_data.json",
                MapResolved = "map_resolved.json",
                MapRaw = "map_raw.json"
            },
            PipelineStatus = status
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json, ct).ConfigureAwait(false);

        return new Map3DLocalBuildResult
        {
            FolderPath = folder,
            ManifestPath = manifestPath,
            MapTexturePath = texturePath,
            MapFilePath = selectedMap,
            ParserReady = selectedMap != null,
            ReusedCachedData = false,
            CandidateCount = candidates.Count,
            AttemptCount = attempts.Count,
            NeedsManualMapSelection = selectedMap == null,
            StatusMessage = status
        };
    }

    private static bool TryLoadReusableResult(string folder, string manifestPath, string? rustMapsMapId, string? textureSha256, out CachedMap3DManifest cached)
    {
        cached = default;
        string mapDataPath = Path.Combine(folder, "map_data.json");
        string mapResolvedPath = Path.Combine(folder, "map_resolved.json");
        string texturePath = Path.Combine(folder, "map_texture.png");
        if (!File.Exists(manifestPath) || !File.Exists(mapDataPath) || !File.Exists(mapResolvedPath) || !File.Exists(texturePath)) return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            string? manifestMapId = root.TryGetProperty("RustMapsMapId", out var mapIdEl) ? mapIdEl.GetString() : null;
            string? manifestTextureHash = root.TryGetProperty("MapTextureSha256", out var hashEl) ? hashEl.GetString() : null;
            string? selectedMap = root.TryGetProperty("SelectedMapFile", out var selectedEl) ? selectedEl.GetString() : null;

            if (!string.Equals(manifestMapId ?? string.Empty, rustMapsMapId ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(textureSha256) && !string.Equals(manifestTextureHash, textureSha256, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(selectedMap) || !File.Exists(selectedMap)) return false;

            cached = new CachedMap3DManifest(selectedMap);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? PromotePendingTexture(string? pendingTexturePath, string folder)
    {
        string? sourcePath = pendingTexturePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            string fallbackPath = Path.Combine(folder, "parser_attempts", "current_map_texture.png");
            sourcePath = File.Exists(fallbackPath) ? fallbackPath : null;
        }

        if (sourcePath == null) return null;
        string texturePath = Path.Combine(folder, "map_texture.png");
        File.Copy(sourcePath, texturePath, overwrite: true);
        return texturePath;
    }
    public static string? GetPreferredMapPickerDirectory()
    {
        return FindRustInstallDirectories().FirstOrDefault() ?? FindSteamRoots().FirstOrDefault() ?? DataManager.AppDir;
    }

    private static IEnumerable<string> FindMapCandidates(string serverName, string host)
    {
        var all = EnumerateLikelyMapFiles()
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FileInfo(g.Key))
            .Where(f => f.Exists && f.Length > 1024 * 1024)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var selected = new List<FileInfo>();
        selected.AddRange(all.Take(RecentCandidateCount));

        string token = GetServerSearchToken(serverName, host);
        if (!string.IsNullOrWhiteSpace(token))
        {
            selected.AddRange(all
                .Where(f => !selected.Any(s => string.Equals(s.FullName, f.FullName, StringComparison.OrdinalIgnoreCase)))
                .Where(f => f.Name.Contains(token, StringComparison.OrdinalIgnoreCase) || f.DirectoryName?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)
                .Take(NameCandidateCount));
        }

        return selected.Select(f => f.FullName);
    }

    private static IEnumerable<string> EnumerateLikelyMapFiles()
    {
        foreach (string rustDir in FindRustInstallDirectories())
        {
            foreach (string file in SafeEnumerateFiles(rustDir, "*.map"))
                yield return file;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? appData = Directory.GetParent(localAppData)?.FullName;
        if (!string.IsNullOrWhiteSpace(appData))
        {
            string localLow = Path.Combine(appData, "LocalLow", "Facepunch Studios LTD", "Rust");
            foreach (string file in SafeEnumerateFiles(localLow, "*.map"))
                yield return file;
        }
    }

    private static IEnumerable<string> FindRustInstallDirectories()
    {
        foreach (var steamRoot in FindSteamRoots())
        {
            foreach (var library in EnumerateSteamLibraries(steamRoot))
            {
                string rust = Path.Combine(library, "steamapps", "common", "Rust");
                if (Directory.Exists(rust)) yield return rust;
            }
        }
    }

    private static IEnumerable<string> FindSteamRoots()
    {
        var roots = new List<string>();
        string? pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86)) roots.Add(Path.Combine(pf86, "Steam"));

        string? pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(pf)) roots.Add(Path.Combine(pf, "Steam"));

        AddRegistrySteamPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistrySteamPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "InstallPath");
        AddRegistrySteamPath(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        AddRegistrySteamPath(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");

        return roots
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeSteamPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamRoot)
    {
        yield return steamRoot;

        string libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFolders)) yield break;

        string text;
        try { text = File.ReadAllText(libraryFolders); }
        catch { yield break; }

        foreach (Match m in Regex.Matches(text, @"""path""\s+""(?<path>(?:\\.|[^""\\])*)"""))
        {
            string library = Regex.Unescape(m.Groups["path"].Value).Replace("\\\\", "\\");
            if (Directory.Exists(library)) yield return library;
        }
    }

    private static void AddRegistrySteamPath(List<string> roots, RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
            {
                roots.Add(value);
            }
        }
        catch { }
    }

    private static string NormalizeSteamPath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Trim().TrimEnd(Path.DirectorySeparatorChar);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files = Array.Empty<string>();
            string[] dirs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir, pattern); } catch { }
            try { dirs = Directory.GetDirectories(dir); } catch { }

            foreach (var file in files) yield return file;
            foreach (var child in dirs) pending.Push(child);
        }
    }

    private static string GetServerSearchToken(string serverName, string host)
    {
        string source = string.IsNullOrWhiteSpace(serverName) ? host : serverName;
        var match = Regex.Match(source, @"[A-Za-z0-9]{3,}");
        return match.Success ? match.Value : string.Empty;
    }

    private static string? ResolveParserExecutable()
    {
        string baseDir = AppContext.BaseDirectory;
        string? embeddedParser = ExtractEmbeddedParserRuntime();
        if (embeddedParser != null) return embeddedParser;

        string[] candidates =
        {
            Path.Combine(baseDir, "MapParser.exe"),
            Path.Combine(baseDir, "MapParser", "MapParser.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MapParser", "bin", "Debug", "net8.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MapParser", "bin", "Debug", "net9.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MapParser", "bin", "Release", "net8.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MapParser", "bin", "Release", "net9.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "MapParser", "bin", "Debug", "net8.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "MapParser", "bin", "Debug", "net9.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "MapParser", "bin", "Release", "net8.0", "MapParser.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "MapParser", "bin", "Release", "net9.0", "MapParser.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ExtractEmbeddedParserRuntime()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string[] resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Map3DParser/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (resourceNames.Length == 0) return null;

        string targetRoot = Path.Combine(DataManager.CacheDir, "map3d-parser-runtime");
        try
        {
            Directory.CreateDirectory(targetRoot);
            TryHideDirectory(targetRoot);

            foreach (string resourceName in resourceNames)
            {
                string relative = resourceName["Map3DParser/".Length..].Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..")) continue;

                string targetPath = Path.GetFullPath(Path.Combine(targetRoot, relative));
                if (!targetPath.StartsWith(Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                using Stream? source = assembly.GetManifestResourceStream(resourceName);
                if (source == null) continue;
                using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                source.CopyTo(target);
            }

            string exePath = Path.Combine(targetRoot, "MapParser.exe");
            return File.Exists(exePath) ? exePath : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryHideDirectory(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch { }
    }

    private static async Task<(bool Success, int ExitCode, string? Error)> RunParserAsync(string parserPath, string mapPath, string outputDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = parserPath,
            WorkingDirectory = Path.GetDirectoryName(parserPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add(mapPath);
        psi.ArgumentList.Add("--output-dir");
        psi.ArgumentList.Add(outputDir);

        using var process = Process.Start(psi);
        if (process == null) return (false, -1, "Failed to start parser.");

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        await Task.WhenAll(stderrTask, stdoutTask).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        string stderr = stderrTask.Result ?? string.Empty;
        string stdout = stdoutTask.Result ?? string.Empty;

        string logPath = Path.Combine(outputDir, "parser_log.txt");
        string logContent = $"--- STDOUT ---\n{stdout}\n\n--- STDERR ---\n{stderr}\n\nExitCode: {process.ExitCode}";
        try { await File.WriteAllTextAsync(logPath, logContent, ct).ConfigureAwait(false); } catch { }

        bool success = process.ExitCode == 0 && File.Exists(Path.Combine(outputDir, "map_resolved.json"));
        return (success, process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? null : stderr.Trim());
    }

    private static MapMatchScore ScoreParsedMap(string resolvedPath, IReadOnlyList<Map3DReferenceMonument>? refs, int worldSize)
    {
        if (refs == null || refs.Count == 0 || worldSize <= 0 || !File.Exists(resolvedPath)) return default;

        using var doc = JsonDocument.Parse(File.ReadAllText(resolvedPath));
        var parsed = new List<ParsedPoint>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string name = el.TryGetProperty("i", out var i) ? i.GetString() ?? string.Empty : string.Empty;
            string category = el.TryGetProperty("c", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!LooksLikeMatchablePoint(name, category)) continue;
            if (!el.TryGetProperty("x", out var xEl) || !el.TryGetProperty("y", out var yEl)) continue;
            parsed.Add(new ParsedPoint(ReadDouble(xEl), ReadDouble(yEl), NormalizeName(name)));
        }

        if (parsed.Count == 0) return default;

        var refList = refs
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Take(12)
            .Select(r => new Map3DReferenceMonument(r.X, r.Y, NormalizeName(r.Name)))
            .ToList();

        return new[]
            {
                ScoreTransform(refList, parsed, worldSize, 0),
                ScoreTransform(refList, parsed, worldSize, 1),
                ScoreTransform(refList, parsed, worldSize, 2),
                ScoreTransform(refList, parsed, worldSize, 3)
            }
            .OrderByDescending(s => s.MatchedCount)
            .ThenBy(s => s.TotalDistance)
            .FirstOrDefault();
    }

    private static MapMatchScore ScoreTransform(List<Map3DReferenceMonument> refs, List<ParsedPoint> parsed, int worldSize, int transform)
    {
        int matched = 0;
        double distance = 0;

        foreach (var r in refs)
        {
            double best = double.MaxValue;
            foreach (var p in parsed)
            {
                if (!NamesCompatible(r.Name, p.Name)) continue;
                var (x, y) = TransformPoint(p.X, p.Y, worldSize, transform);
                double d = Math.Sqrt(Math.Pow(r.X - x, 2) + Math.Pow(r.Y - y, 2));
                if (d < best) best = d;
            }

            if (best <= MatchDistanceWorldUnits)
            {
                matched++;
                distance += best;
            }
        }

        return new MapMatchScore(matched, distance);
    }

    private static (double X, double Y) TransformPoint(double x, double y, int worldSize, int transform)
    {
        double half = worldSize / 2.0;
        return transform switch
        {
            0 => (x, y),
            1 => (x + half, y + half),
            2 => (x + half, half - y),
            3 => (half - x, y + half),
            _ => (x, y)
        };
    }

    private static bool IsGoodMatch(MapMatchScore score, IReadOnlyList<Map3DReferenceMonument>? refs)
    {
        int available = refs?.Count(r => !string.IsNullOrWhiteSpace(r.Name)) ?? 0;
        int required = Math.Min(3, Math.Max(1, available));
        return score.MatchedCount >= required;
    }

    private static bool LooksLikeMatchablePoint(string name, string category)
    {
        string n = (name + " " + category).ToLowerInvariant();
        return n.Contains("monument") || n.Contains("harbor") || n.Contains("airfield") || n.Contains("sphere") ||
               n.Contains("dome") || n.Contains("junkyard") || n.Contains("launch") || n.Contains("tunnel") ||
               n.Contains("quarry") || n.Contains("cave") || n.Contains("iceberg") || n.Contains("ice_lake") ||
               n.Contains("god") || n.Contains("anvil") || n.Contains("bandit") || n.Contains("compound") ||
               n.Contains("fishing") || n.Contains("stables") || n.Contains("water_treatment") || n.Contains("gas_station") ||
               n.Contains("supermarket") || n.Contains("lighthouse") || n.Contains("satellite") || n.Contains("military") ||
               n.Contains("silo") || n.Contains("powerplant");
    }

    private static bool NamesCompatible(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase)) return true;
        var aw = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).ToList();
        var bw = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).ToList();
        return aw.Any(w => bw.Contains(w));
    }

    private static string NormalizeName(string value)
    {
        value = value.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        value = Regex.Replace(value, @"\b(root|prefab|monument|assets|bundled|scene|small|large|a|b|c|d|e|f|g|h|1|2|3)\b", " ");
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value;
    }

    private static double ReadDouble(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var value) ? value : 0;
    }

    private static void CopyParserOutput(string fromDir, string toDir)
    {
        foreach (var name in new[] { "map_data.json", "map_resolved.json", "map_raw.json" })
        {
            string from = Path.Combine(fromDir, name);
            if (File.Exists(from)) File.Copy(from, Path.Combine(toDir, name), overwrite: true);
        }
    }


    public static (int DeletedFiles, int DeletedDirectories) DeleteAllCachedMapData()
    {
        int deletedFiles = 0;
        int deletedDirectories = 0;

        void DeleteTree(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                deletedFiles += Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
                deletedDirectories += Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Count() + 1;
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best effort cleanup: keep this action non-fatal if a viewer process still holds a file.
            }
        }

        DeleteTree(Path.Combine(DataManager.AppDir, "3DMaps"));
        DeleteTree(Path.Combine(DataManager.AppDir, "Map3DViewer", "maps"));
        Directory.CreateDirectory(Path.Combine(DataManager.AppDir, "3DMaps"));

        return (deletedFiles, deletedDirectories);
    }    public static string GetPreparedFolderPath(ServerProfile profile, string? rustMapsMapId)
    {
        return Path.Combine(DataManager.AppDir, "3DMaps", BuildServerKey(profile, rustMapsMapId));
    }

    private static string BuildServerKey(ServerProfile profile, string? rustMapsMapId)
    {
        string raw = $"{profile.Host}:{profile.Port}:{rustMapsMapId ?? "unknown"}".ToLowerInvariant();
        using var sha = SHA256.Create();
        string hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..12];
        string name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Host : profile.Name;
        string safeName = new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "server";
        return $"{safeName}_{hash}";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "map" : name;
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    private static async Task SavePngAsync(BitmapSource source, string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(fs);
        await fs.FlushAsync(ct).ConfigureAwait(false);
    }

    private readonly record struct ParsedPoint(double X, double Y, string Name);
    private readonly record struct CachedMap3DManifest(string SelectedMapFile);
    private readonly record struct MapMatchScore(int MatchedCount, double TotalDistance)
    {
        public bool IsBetterThan(MapMatchScore other)
        {
            if (MatchedCount != other.MatchedCount) return MatchedCount > other.MatchedCount;
            if (MatchedCount == 0) return false;
            return TotalDistance < other.TotalDistance || other.TotalDistance <= 0;
        }
    }
}














