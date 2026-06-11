using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;
using System.Linq;

namespace RustPlusDesk.Services.Data
{
    public static class DataManager
    {
        public static string OVERLAY_SYNC_SECRET_HEX => Decrypt(ObfuscatedSecrets.ObfuscatedSecret);
        public static string OVERLAY_SYNC_BASEURL => Decrypt(ObfuscatedSecrets.ObfuscatedUrl);
        public static string SUPABASE_URL => Decrypt(ObfuscatedSecrets.ObfuscatedSupabaseUrl);
        public static string SUPABASE_ANON_KEY => Decrypt(ObfuscatedSecrets.ObfuscatedSupabaseAnonKey);
        public const int OVERLAY_MAX_BYTES = 350_000;

        private static string Decrypt(byte[] encrypted)
        {
            byte[] decrypted = new byte[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++)
            {
                decrypted[i] = (byte)(encrypted[i] ^ ObfuscatedSecrets.ObfuscationKey[i % ObfuscatedSecrets.ObfuscationKey.Length]);
            }
            return Encoding.UTF8.GetString(decrypted);
        }

        public static string AppDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");

        public static string ProfilesPath => Path.Combine(AppDir, "profiles.json");

        public static string CacheDir => Path.Combine(AppDir, "cache");

        public static string GetOverlayJsonPath(string serverKey, ulong steamId)
        {
            var baseDir = Path.Combine(AppDir, "Overlays", serverKey);
            Directory.CreateDirectory(baseDir);
            return Path.Combine(baseDir, $"{steamId}.json");
        }

        public static long UnixNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new ArgumentException("invalid hex length");

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        public static string HmacSha256Hex(string keyHex, string dataUtf8)
        {
            var keyBytes = HexToBytes(keyHex);
            var payloadBytes = Encoding.UTF8.GetBytes(dataUtf8);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                var hash = hmac.ComputeHash(payloadBytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static async Task UploadPayloadAsync(ulong steamId, string serverKey, string overlayB64)
        {
            // If offline / no Supabase keys / not logged in -> Skip cloud sync or fallback
            if (SupabaseAuthManager.Client == null || !SupabaseAuthManager.IsAuthenticated)
            {
                return;
            }

            try
            {
                int uncompressedSize = 0;
                try
                {
                    var bytes = Convert.FromBase64String(overlayB64);
                    uncompressedSize = bytes.Length;
                }
                catch { uncompressedSize = overlayB64.Length; }

                var payload = new
                {
                    server_key = serverKey,
                    steam_id = steamId.ToString(),
                    map_overlay = new
                    {
                        overlay_data = overlayB64,
                        uncompressed_size = uncompressedSize
                    }
                };

                await SupabaseAuthManager.CallEdgeFunctionAsync("overlay", HttpMethod.Post, payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataManager] Upload error: {ex.Message}");
            }
        }

        public static async Task<string?> FetchPayloadAsync(ulong steamId, string serverKey)
        {
            if (SupabaseAuthManager.Client == null)
            {
                return null;
            }

            try
            {
                var queryParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["server_key"] = serverKey,
                    ["steam_id"] = steamId.ToString()
                };

                var body = await SupabaseAuthManager.CallEdgeFunctionAsync("overlay", HttpMethod.Get, null, queryParams);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("map_overlay", out var mapEl) && mapEl.ValueKind == JsonValueKind.Object)
                {
                    var mapRow = JsonSerializer.Deserialize<RustPlusDesk.Models.MapOverlayModel>(mapEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return mapRow?.OverlayData;
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataManager] Fetch error: {ex.Message}");
                return null;
            }
        }

        private static readonly object FileLock = new object();

        public static void SaveCache<T>(string key, T data)
        {
            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    var path = Path.Combine(CacheDir, key + ".json");
                    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SaveCache Error ({key}): {ex.Message}");
                }
            }
        }

        public static T? LoadCache<T>(string key)
        {
            lock (FileLock)
            {
                try
                {
                    var path = Path.Combine(CacheDir, key + ".json");
                    if (!File.Exists(path)) return default;
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<T>(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LoadCache Error ({key}): {ex.Message}");
                    return default;
                }
            }
        }
    }
}
