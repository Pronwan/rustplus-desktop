using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Services;

/// <summary>
/// Centralized, thread-safe Steam avatar loading and caching service.
/// Provides in-memory caching, in-flight request deduplication, thread-safe frozen BitmapImages,
/// and a reactive event notification for UI components across the application.
/// </summary>
public static class AvatarLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<ulong, ImageSource> MemoryCache = new();
    private static readonly ConcurrentDictionary<ulong, Task<ImageSource?>> InflightFetches = new();

    /// <summary>
    /// Event fired whenever an avatar has been successfully loaded and cached.
    /// UI ViewModels can subscribe to this event to update their avatar properties reactively.
    /// </summary>
    public static event Action<ulong, ImageSource>? AvatarLoaded;

    /// <summary>
    /// Synchronously retrieves the cached avatar if available.
    /// </summary>
    public static ImageSource? GetCachedAvatar(ulong steamId)
    {
        if (steamId == 0) return null;
        return MemoryCache.TryGetValue(steamId, out var img) ? img : null;
    }

    /// <summary>
    /// Stores or replaces an avatar in the cache and broadcasts the update.
    /// </summary>
    public static void StoreCachedAvatar(ulong steamId, ImageSource avatar)
    {
        if (steamId == 0 || avatar == null) return;
        MemoryCache[steamId] = avatar;
        AvatarLoaded?.Invoke(steamId, avatar);
    }

    /// <summary>
    /// Clears the avatar cache (e.g. upon server change or connection reset).
    /// </summary>
    public static void ClearCache()
    {
        MemoryCache.Clear();
        InflightFetches.Clear();
    }

    /// <summary>
    /// Asynchronously fetches the Steam avatar for the given Steam ID.
    /// Deduplicates in-flight requests to prevent redundant network calls.
    /// </summary>
    public static async Task<ImageSource?> GetOrLoadAvatarAsync(ulong steamId, CancellationToken ct = default)
    {
        if (steamId == 0) return null;

        // 1. Check memory cache
        if (MemoryCache.TryGetValue(steamId, out var cached) && cached != null)
        {
            return cached;
        }

        // 2. Deduplicate in-flight fetches
        return await InflightFetches.GetOrAdd(steamId, async id =>
        {
            try
            {
                var img = await FetchSteamAvatarInternalAsync(id, ct).ConfigureAwait(false);
                if (img != null)
                {
                    MemoryCache[id] = img;
                    AvatarLoaded?.Invoke(id, img);
                }
                return img;
            }
            finally
            {
                InflightFetches.TryRemove(id, out _);
            }
        }).ConfigureAwait(false);
    }

    private static async Task<ImageSource?> FetchSteamAvatarInternalAsync(ulong steamId, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var xml = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1", ct)
                .ConfigureAwait(false);
            NetworkTrafficMonitor.Instance.RecordInbound("Steam Community", $"Steam Profile XML ({steamId})", System.Text.Encoding.UTF8.GetByteCount(xml));

            string url = "";
            var mFull = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
            var mMedium = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.IgnoreCase);

            if (mFull.Success) url = mFull.Groups[1].Value;
            else if (mMedium.Success) url = mMedium.Groups[1].Value;

            if (string.IsNullOrWhiteSpace(url)) return null;

            var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            sw.Stop();
            NetworkTrafficMonitor.Instance.RecordInbound("Steam Community", $"Steam Avatar Image ({steamId})", bytes.Length, sw.ElapsedMilliseconds, "200 OK", Path.GetFileName(url));

            return BytesToImage(bytes);
        }
        catch
        {
            // Steam profile may be private, rate-limited, or network offline.
            return null;
        }
    }

    /// <summary>
    /// Decodes image bytes into a frozen BitmapImage that is safe for cross-thread access.
    /// </summary>
    public static ImageSource? BytesToImage(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;

        try
        {
            var bi = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze(); // Crucial for cross-thread safety in WPF
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
