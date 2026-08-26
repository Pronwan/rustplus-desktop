using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

/// <summary>
/// Loads the shipped console command catalogue and layers the user's own binds and parameter
/// values on top. The two are stored apart on purpose: shipping new commands, or correcting a
/// description, must never wipe somebody's keybinds.
/// </summary>
public static class ConsoleCommandLibrary
{
    private const string CatalogFileName = "console-commands.json";

    private static readonly string UserStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustPlusDesk", "console-helper.json");

    private static List<ConsoleCommandDef>? _all;
    private static Action<string>? _log;

    public static void SetLogger(Action<string>? log) => _log = log;

    public static IReadOnlyList<ConsoleCommandDef> All => _all ??= Load();

    /// <summary>Commands for one tab, newest-useful first: featured block, then the rest by group.</summary>
    public static IReadOnlyList<ConsoleCommandDef> Featured(string category) =>
        All.Where(c => Matches(c, category) && c.Featured).ToList();

    public static IReadOnlyList<ConsoleCommandGroup> Grouped(string category)
    {
        var groups = new List<ConsoleCommandGroup>();
        foreach (var g in All.Where(c => Matches(c, category) && !c.Featured)
                             .GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "Other" : c.Group)
                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var group = new ConsoleCommandGroup { Title = g.Key };
            foreach (var c in g.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase))
                group.Commands.Add(c);
            groups.Add(group);
        }
        return groups;
    }

    private static bool Matches(ConsoleCommandDef c, string category)
        => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase);

    private static List<ConsoleCommandDef> Load()
    {
        var list = new List<ConsoleCommandDef>();
        try
        {
            var path = ResolveCatalogPath();
            if (path == null)
            {
                _log?.Invoke($"[console-helper] {CatalogFileName} not found - the panel will be empty.");
                return list;
            }

            var json = File.ReadAllText(path);
            var catalog = JsonSerializer.Deserialize<ConsoleCommandCatalog>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (catalog?.Commands != null) list = catalog.Commands;
            _log?.Invoke($"[console-helper] loaded {list.Count} commands from {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[console-helper] failed to read {CatalogFileName}: {ex.Message}");
            return list;
        }

        var state = LoadUserState();
        foreach (var c in list)
        {
            c.AttachParamWatchers();
            if (state.TryGetValue(c.Id, out var s)) c.ApplyUserState(s);
            c.UserStateChanged += (_, __) => SaveUserState();
            c.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConsoleCommandDef.BindKey)) SaveUserState();
            };
        }
        return list;
    }

    private static string? ResolveCatalogPath()
    {
        // Same search order the item catalogue uses: next to the exe first, then the working
        // directory, so a dev run out of bin and an installed build both find it.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Data", CatalogFileName),
            Path.Combine(AppContext.BaseDirectory, CatalogFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Data", CatalogFileName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, ConsoleCommandUserState> LoadUserState()
    {
        try
        {
            if (!File.Exists(UserStatePath)) return new();
            var json = File.ReadAllText(UserStatePath);
            return JsonSerializer.Deserialize<Dictionary<string, ConsoleCommandUserState>>(json)
                   ?? new Dictionary<string, ConsoleCommandUserState>();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[console-helper] could not read saved binds: {ex.Message}");
            return new();
        }
    }

    public static void SaveUserState()
    {
        if (_all == null) return;
        try
        {
            var map = new Dictionary<string, ConsoleCommandUserState>();
            foreach (var c in _all)
            {
                var s = c.ToUserState();
                // Only keep entries that actually carry something, so the file stays readable
                // and a reset really does disappear rather than lingering as an empty object.
                if (s.Bind != null || (s.Values != null && s.Values.Count > 0))
                    map[c.Id] = s;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(UserStatePath)!);
            File.WriteAllText(UserStatePath, JsonSerializer.Serialize(map, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[console-helper] could not save binds: {ex.Message}");
        }
    }

    /// <summary>Every bind the user has set, as console lines ready to paste in one go.</summary>
    public static string AllBindLines()
        => string.Join(Environment.NewLine,
            All.Where(c => c.Bindable && c.HasBind).Select(c => c.BindLine));
}
