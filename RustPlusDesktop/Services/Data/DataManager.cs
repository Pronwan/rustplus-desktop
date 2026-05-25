using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Data
{
    public static class DataManager
    {
        public static string OVERLAY_SYNC_SECRET_HEX => Decrypt(ObfuscatedSecrets.ObfuscatedSecret);
        public static string OVERLAY_SYNC_BASEURL => Decrypt(ObfuscatedSecrets.ObfuscatedUrl);
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
            var ts = UnixNow().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sigInput = steamId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + serverKey + "|" + ts + "|" + overlayB64;
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

            var payloadObj = new
            {
                steamId = steamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                serverKey = serverKey,
                ts = ts,
                overlayJsonB64 = overlayB64,
                sig = sig
            };

            var payloadJson = JsonSerializer.Serialize(payloadObj);
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using (var http = new HttpClient())
            {
                var url = OVERLAY_SYNC_BASEURL + "/upload";
                var resp = await http.PostAsync(url, content);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException("Upload failed: HTTP " + (int)resp.StatusCode);
            }
        }

        public static async Task<string?> FetchPayloadAsync(ulong steamId, string serverKey)
        {
            var ts = UnixNow().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sigInput = steamId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + serverKey + "|" + ts;
            var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

            var url = $"{OVERLAY_SYNC_BASEURL}/fetch" +
                      $"?steamId={Uri.EscapeDataString(steamId.ToString(System.Globalization.CultureInfo.InvariantCulture))}" +
                      $"&serverKey={Uri.EscapeDataString(serverKey)}" +
                      $"&ts={Uri.EscapeDataString(ts)}" +
                      $"&sig={Uri.EscapeDataString(sig)}";

            using (var http = new HttpClient())
            {
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    if ((int)resp.StatusCode == 404)
                        return null; // Not found is standard for members who haven't uploaded
                    throw new InvalidOperationException("Fetch failed: HTTP " + (int)resp.StatusCode);
                }

                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("overlayJsonB64", out var b64El))
                    return null;

                return b64El.GetString();
            }
        }

        public static void SaveCache<T>(string key, T data)
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

        public static T? LoadCache<T>(string key)
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
