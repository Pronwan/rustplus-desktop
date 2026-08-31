using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusDesk.Services.Emoji;

public record EmojiEntry(
    string Name,
    string Tag,
    string DisplayName,
    bool IsCustomEmoji,
    string? Category = null
)
{
    private ImageSource? _cachedIcon;

    public ImageSource? Icon
    {
        get
        {
            if (_cachedIcon != null) return _cachedIcon;
            var icon = EmojiService.GetIcon(this);
            if (icon != null) _cachedIcon = icon;
            return icon;
        }
    }

    public string? LocalPath => EmojiService.GetCustomEmojiPath(Name);
}

public static class EmojiService
{
    public static readonly Regex EmojiTokenRegex = new(@"\:(?<token>[a-zA-Z0-9_\.\-]+)\:", RegexOptions.Compiled);

    private static readonly Dictionary<string, EmojiEntry> s_customEmojisByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<EmojiEntry> s_customEmojiList = new();
    private static readonly Dictionary<string, ImageSource> s_iconCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string name, string displayName, string category)[] s_rawCustomEmojis =
    [
        ("angry", "Angry Face", "Face"),
        ("coffeecan", "Coffee Can", "Face"),
        ("cool", "Cool Shades", "Face"),
        ("dance", "Dancing", "Face"),
        ("exclamation", "Exclamation", "Emote"),
        ("eyebrow", "Raised Eyebrow", "Face"),
        ("eyes", "Looking Eyes", "Emote"),
        ("funny", "Funny Face", "Face"),
        ("happy", "Happy Face", "Face"),
        ("heart", "Heart", "Emote"),
        ("heartrock", "Heart Rock", "Item"),
        ("hazmat", "Hazmat", "Character"),
        ("trash", "Trash Bin", "Item"),
        ("bush", "Jungle Bush", "Item"),
        ("sick", "Sick Face", "Face"),
        ("laugh", "Laughing Face", "Face"),
        ("light", "Torch Light", "Item"),
        ("love", "Love Heart Eyes", "Face"),
        ("mask", "Bandana Mask", "Character"),
        ("nervous", "Nervous Sweat", "Face"),
        ("neutral", "Neutral Face", "Face"),
        ("scientist", "Scientist", "Character"),
        ("skull", "Skull", "Emote"),
        ("smilecry", "Smile Cry", "Face"),
        ("trumpet", "Trumpet", "Item"),
        ("wave", "Waving Hand", "Emote"),
        ("worried", "Worried Face", "Face"),
        ("yellowpin", "Yellow Pin", "Emote")
    ];

    static EmojiService()
    {
        InitializeCustomEmojis();
    }

    private static void InitializeCustomEmojis()
    {
        s_customEmojisByName.Clear();
        s_customEmojiList.Clear();

        foreach (var (name, displayName, category) in s_rawCustomEmojis)
        {
            var entry = new EmojiEntry(
                Name: name,
                Tag: $":{name}:",
                DisplayName: displayName,
                IsCustomEmoji: true,
                Category: category
            );
            s_customEmojisByName[name] = entry;
            s_customEmojiList.Add(entry);
        }
    }

    public static IReadOnlyList<EmojiEntry> CustomEmojis => s_customEmojiList;

    public static ImageSource? GetIcon(EmojiEntry entry)
    {
        if (entry.IsCustomEmoji)
        {
            return GetCustomEmojiImage(entry.Name);
        }

        // Rust item icon resolution
        return Views.MainWindow.ResolveItemIcon(0, entry.Name, 32);
    }

    private static readonly Dictionary<string, BitmapSource[]> s_animatedFramesCache = new(StringComparer.OrdinalIgnoreCase);

    public static string? GetCustomEmojiWebpPath(string emojiName)
    {
        if (string.IsNullOrWhiteSpace(emojiName)) return null;
        emojiName = emojiName.Trim().Trim(':').ToLowerInvariant();

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "emojis", $"{emojiName}.webp"),
            Path.Combine(Environment.CurrentDirectory, "RustPlusDesktop", "Assets", "emojis", $"{emojiName}.webp"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "emojis", $"{emojiName}.webp"),
            Path.Combine(baseDir, "..", "..", "..", "RustPlusDesktop", "Assets", "emojis", $"{emojiName}.webp")
        };

        foreach (var path in candidates)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        return null;
    }

    public static string? GetCustomEmojiPath(string emojiName) => GetCustomEmojiWebpPath(emojiName) ?? GetCustomEmojiThumbnailPath(emojiName);

    public static BitmapSource[]? GetCustomEmojiFrames(string emojiName)
    {
        if (string.IsNullOrWhiteSpace(emojiName)) return null;
        emojiName = emojiName.Trim().Trim(':').ToLowerInvariant();

        if (s_animatedFramesCache.TryGetValue(emojiName, out var cached))
            return cached;

        var webpPath = GetCustomEmojiWebpPath(emojiName);
        if (webpPath == null || !File.Exists(webpPath)) return null;

        try
        {
            using var image = Image.Load<Rgba32>(webpPath);
            int frameCount = image.Frames.Count;
            if (frameCount == 0) return null;

            var list = new BitmapSource[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                var frame = image.Frames[i];
                var bgraBytes = new byte[frame.Width * frame.Height * 4];
                frame.CopyPixelDataTo(bgraBytes);

                // Convert RGBA -> BGRA32 for WPF with zero alpha noise
                for (int p = 0; p < bgraBytes.Length; p += 4)
                {
                    byte r = bgraBytes[p];
                    byte g = bgraBytes[p + 1];
                    byte b = bgraBytes[p + 2];
                    byte a = bgraBytes[p + 3];

                    if (a <= 8)
                    {
                        bgraBytes[p] = 0;
                        bgraBytes[p + 1] = 0;
                        bgraBytes[p + 2] = 0;
                        bgraBytes[p + 3] = 0;
                    }
                    else
                    {
                        bgraBytes[p] = b;     // Blue
                        bgraBytes[p + 2] = r; // Red
                    }
                }

                var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, bgraBytes, frame.Width * 4);
                bmp.Freeze();
                list[i] = bmp;
            }

            s_animatedFramesCache[emojiName] = list;
            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EmojiService] Failed to load WebP {emojiName}: {ex.Message}");
            return null;
        }
    }

    public static bool TryGetCachedFrames(string emojiName, out BitmapSource[] frames)
    {
        frames = null!;
        if (string.IsNullOrWhiteSpace(emojiName)) return false;
        emojiName = emojiName.Trim().Trim(':').ToLowerInvariant();
        return s_animatedFramesCache.TryGetValue(emojiName, out frames!);
    }

    public static string? GetCustomEmojiThumbnailPath(string emojiName)
    {
        if (string.IsNullOrWhiteSpace(emojiName)) return null;
        emojiName = emojiName.Trim().Trim(':').ToLowerInvariant();

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "emojis", "thumbs", $"{emojiName}.png"),
            Path.Combine(Environment.CurrentDirectory, "RustPlusDesktop", "Assets", "emojis", "thumbs", $"{emojiName}.png"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "emojis", "thumbs", $"{emojiName}.png"),
            Path.Combine(baseDir, "..", "..", "..", "RustPlusDesktop", "Assets", "emojis", "thumbs", $"{emojiName}.png")
        };

        foreach (var path in candidates)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        return null;
    }

    public static Task<BitmapSource[]?> GetCustomEmojiFramesAsync(string emojiName)
    {
        if (string.IsNullOrWhiteSpace(emojiName)) return Task.FromResult<BitmapSource[]?>(null);
        emojiName = emojiName.Trim().Trim(':').ToLowerInvariant();

        if (s_animatedFramesCache.TryGetValue(emojiName, out var cached))
            return Task.FromResult<BitmapSource[]?>(cached);

        return Task.Run(() => GetCustomEmojiFrames(emojiName));
    }

    private static bool s_preloadStarted = false;
    public static void StartBackgroundPreload()
    {
        if (s_preloadStarted) return;
        s_preloadStarted = true;

        Task.Run(() =>
        {
            try
            {
                // Pre-warm thumbnail static icons first (instant)
                foreach (var emoji in CustomEmojis)
                {
                    GetCustomEmojiImage(emoji.Name);
                }

                // Pre-decode animations in parallel in background
                Parallel.ForEach(CustomEmojis, new ParallelOptions { MaxDegreeOfParallelism = 4 }, emoji =>
                {
                    GetCustomEmojiFrames(emoji.Name);
                });
            }
            catch { }
        });
    }

    public static ImageSource? GetCustomEmojiImage(string emojiName)
    {
        if (string.IsNullOrWhiteSpace(emojiName)) return null;

        emojiName = emojiName.Trim().Trim(':').ToLowerInvariant();

        if (s_iconCache.TryGetValue(emojiName, out var cached))
            return cached;

        // 1. Instant static thumbnail (0ms load)
        var thumbPath = GetCustomEmojiThumbnailPath(emojiName);
        if (thumbPath != null && File.Exists(thumbPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(thumbPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                s_iconCache[emojiName] = bmp;
                return bmp;
            }
            catch { }
        }

        // 2. Cached animation first frame
        if (s_animatedFramesCache.TryGetValue(emojiName, out var frames) && frames.Length > 0)
        {
            s_iconCache[emojiName] = frames[0];
            return frames[0];
        }

        return null;
    }

    public static EmojiEntry? ResolveEmojiOrItem(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var clean = token.Trim().Trim(':');

        // 1. Check custom emoji
        if (s_customEmojisByName.TryGetValue(clean, out var customEntry))
        {
            return customEntry;
        }

        // 2. Check Rust items
        var icon = Views.MainWindow.ResolveItemIcon(0, clean, 32);
        if (icon != null)
        {
            var displayName = Views.MainWindow.ResolveItemName(0, clean);
            if (string.IsNullOrWhiteSpace(displayName) || displayName == "(unbekannt)")
            {
                displayName = clean;
            }

            return new EmojiEntry(
                Name: clean,
                Tag: $":{clean}:",
                DisplayName: displayName,
                IsCustomEmoji: false,
                Category: "Item"
            );
        }

        return null;
    }

    public static bool IsValidEmojiOrItem(string token)
    {
        return ResolveEmojiOrItem(token) != null;
    }

    public static IEnumerable<EmojiEntry> Search(string query, int maxResults = 30)
    {
        var clean = (query ?? string.Empty).Trim().Trim(':').ToLowerInvariant();

        var results = new List<EmojiEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Match custom emojis
        foreach (var custom in s_customEmojiList)
        {
            if (string.IsNullOrEmpty(clean) ||
                custom.Name.Contains(clean, StringComparison.OrdinalIgnoreCase) ||
                custom.DisplayName.Contains(clean, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(custom);
                seen.Add(custom.Name);
                if (results.Count >= maxResults) return results;
            }
        }

        // 2. Match Rust items from database
        try
        {
            var itemShortnames = Views.MainWindow.GetAllItemShortNames();
            foreach (var sn in itemShortnames)
            {
                if (seen.Contains(sn)) continue;

                if (string.IsNullOrEmpty(clean) || sn.Contains(clean, StringComparison.OrdinalIgnoreCase))
                {
                    var dispName = Views.MainWindow.ResolveItemName(0, sn);
                    if (string.IsNullOrWhiteSpace(dispName) || dispName == "(unbekannt)")
                    {
                        dispName = sn;
                    }

                    results.Add(new EmojiEntry(
                        Name: sn,
                        Tag: $":{sn}:",
                        DisplayName: dispName,
                        IsCustomEmoji: false,
                        Category: "Item"
                    ));

                    seen.Add(sn);
                    if (results.Count >= maxResults) break;
                }
            }
        }
        catch
        {
            // Non-critical if item list lookup fails
        }

        return results;
    }
}
