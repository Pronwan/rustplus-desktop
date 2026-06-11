using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.Auth
{
    public static class HandshakeService
    {
        private static readonly HttpClient _http = new();
        private const string CacheKeyPrivateKey = "handshake_key";
        private const string CacheKeyJwt = "handshake_jwt";

        public static string? GuestJwt { get; private set; }

        private class HandshakeKeyStore
        {
            public string SteamId { get; set; } = "";
            public string PrivateKeyPem { get; set; } = "";
            public string PublicKeyB64 { get; set; } = "";
        }

        private class HandshakeJwtStore
        {
            public string Token { get; set; } = "";
            public long ExpiresAt { get; set; }
        }

        private class HandshakeResponse
        {
            public string? Token { get; set; }
            public string? RecoveryCode { get; set; }
            public string? NewRecoveryCode { get; set; }
            public string? Error { get; set; }
        }

        public static bool HasLocalKey => DataManager.LoadCache<HandshakeKeyStore>(CacheKeyPrivateKey) != null;

        public static bool HasValidJwt
        {
            get
            {
                var stored = DataManager.LoadCache<HandshakeJwtStore>(CacheKeyJwt);
                if (stored == null || string.IsNullOrEmpty(stored.Token)) return false;
                if (stored.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
                GuestJwt = stored.Token;
                return true;
            }
        }

        public static (string publicKeyB64, string privateKeyPem) GenerateKeyPair()
        {
            using var rsa = RSA.Create(2048);
            var publicKeyB64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            var privateKeyPem = rsa.ExportRSAPrivateKeyPem();
            return (publicKeyB64, privateKeyPem);
        }

        public static string GetClientHash()
        {
            try
            {
                var path = typeof(HandshakeService).Assembly.Location;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    path = Environment.ProcessPath;
                }

                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                    return "";

                using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(stream);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        public static string SignData(string privateKeyPem, string data)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var sig = rsa.SignData(Encoding.UTF8.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(sig);
        }

        /// <summary>
        /// First-time registration: New keypair, HMAC proof, store JWT + recovery code.
        /// </summary>
        public static async Task<(bool success, string? error, string? recoveryCode)> RegisterAsync(string steamId)
        {
            try
            {
                var (publicKeyB64, privateKeyPem) = GenerateKeyPair();
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                var secretHex = DataManager.OVERLAY_SYNC_SECRET_HEX;
                var clientHash = GetClientHash();
                var hmacSig = DataManager.HmacSha256Hex(secretHex, $"{steamId}{timestamp}{publicKeyB64}{clientHash}");

                var payload = new
                {
                    steam_id = steamId,
                    client_public_key = publicKeyB64,
                    hmac_signature = hmacSig,
                    timestamp,
                    client_hash = clientHash
                };

                var response = await CallEdgeFunctionAsync(payload);
                if (response == null)
                    return (false, "No response from handshake server", null);

                if (!string.IsNullOrEmpty(response.Error))
                    return (false, response.Error, null);

                if (string.IsNullOrEmpty(response.Token))
                    return (false, "Server returned no token", null);

                // Save keypair
                DataManager.SaveCache(CacheKeyPrivateKey, new HandshakeKeyStore
                {
                    SteamId = steamId,
                    PrivateKeyPem = privateKeyPem,
                    PublicKeyB64 = publicKeyB64
                });

                // Save JWT
                SaveJwt(response.Token);
                GuestJwt = response.Token;

                return (true, null, response.RecoveryCode);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        /// <summary>
        /// Quick session refresh: sign challenge with stored private key.
        /// </summary>
        public static async Task<(bool success, string? error)> RefreshAsync()
        {
            try
            {
                var keyStore = DataManager.LoadCache<HandshakeKeyStore>(CacheKeyPrivateKey);
                if (keyStore == null || string.IsNullOrEmpty(keyStore.PrivateKeyPem))
                    return (false, "No local keypair found");

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                var nonce = Guid.NewGuid().ToString("N");
                var clientHash = GetClientHash();
                var signature = SignData(keyStore.PrivateKeyPem, timestamp + nonce + clientHash);

                var payload = new
                {
                    steam_id = keyStore.SteamId,
                    signature,
                    timestamp,
                    nonce,
                    client_hash = clientHash
                };

                var response = await CallEdgeFunctionAsync(payload);
                if (response == null)
                    return (false, "No response from handshake server");

                if (!string.IsNullOrEmpty(response.Error))
                    return (false, response.Error);

                if (string.IsNullOrEmpty(response.Token))
                    return (false, "Server returned no token");

                SaveJwt(response.Token);
                GuestJwt = response.Token;
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Device recovery: mnemonic proves identity, register new keypair.
        /// </summary>
        public static async Task<(bool success, string? error, string? newRecoveryCode)> RecoverAsync(
            string steamId, string mnemonic)
        {
            try
            {
                var (newPublicKeyB64, newPrivateKeyPem) = GenerateKeyPair();
                var mnemonicBytes = Encoding.UTF8.GetBytes(mnemonic);
                var clientHash = GetClientHash();
                var recoverySig = HmacSha256Bytes(mnemonicBytes, steamId + newPublicKeyB64 + clientHash);

                var payload = new
                {
                    steam_id = steamId,
                    new_public_key = newPublicKeyB64,
                    recovery_signature = recoverySig,
                    mnemonic_token = mnemonic,
                    client_hash = clientHash
                };

                var response = await CallEdgeFunctionAsync(payload);
                if (response == null)
                    return (false, "No response from handshake server", null);

                if (!string.IsNullOrEmpty(response.Error))
                    return (false, response.Error, null);

                if (string.IsNullOrEmpty(response.Token))
                    return (false, "Server returned no token", null);

                // Save new keypair
                DataManager.SaveCache(CacheKeyPrivateKey, new HandshakeKeyStore
                {
                    SteamId = steamId,
                    PrivateKeyPem = newPrivateKeyPem,
                    PublicKeyB64 = newPublicKeyB64
                });

                SaveJwt(response.Token);
                GuestJwt = response.Token;

                return (true, null, response.NewRecoveryCode);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        private static async Task<HandshakeResponse?> CallEdgeFunctionAsync(object payload)
        {
            if (SupabaseAuthManager.IsUpgradeRequiredSnackbarShown)
                return null;

            var json = JsonSerializer.Serialize(payload);
            var url = $"{DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/auth-handshake";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", DataManager.SUPABASE_ANON_KEY);
            request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var errEl) && errEl.GetString() == "upgrade_required")
                    {
                        if (!SupabaseAuthManager.IsUpgradeRequiredSnackbarShown)
                        {
                            SupabaseAuthManager.IsUpgradeRequiredSnackbarShown = true;
                            string message = root.TryGetProperty("message", out var msgEl)
                                ? msgEl.GetString() ?? "An update is required to use cloud features."
                                : "An update is required to use cloud features.";
                            string upgradeUrl = root.TryGetProperty("upgrade_url", out var urlEl)
                                ? urlEl.GetString() ?? "https://github.com/JawadYzbk/rustplus-desktop/releases/latest"
                                : "https://github.com/JawadYzbk/rustplus-desktop/releases/latest";

                            if (System.Windows.Application.Current != null)
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (System.Windows.Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                                    {
                                        mainWin.ShowUpgradeRequiredSnackbar(message, upgradeUrl);
                                    }
                                });
                            }
                        }
                    }
                }
                catch { /* Ignore JSON parse errors */ }
            }

            return JsonSerializer.Deserialize<HandshakeResponse>(body);
        }

        private static void SaveJwt(string token)
        {
            // Parse JWT payload (2nd segment) to get expiration
            var parts = token.Split('.');
            long expiresAt = 0;
            if (parts.Length == 3)
            {
                try
                {
                    var padded = parts[1].Replace('-', '+').Replace('_', '/');
                    switch (padded.Length % 4)
                    {
                        case 2: padded += "=="; break;
                        case 3: padded += "="; break;
                    }
                    var payloadBytes = Convert.FromBase64String(padded);
                    using var doc = JsonDocument.Parse(payloadBytes);
                    if (doc.RootElement.TryGetProperty("exp", out var exp))
                        expiresAt = exp.GetInt64();
                }
                catch { }
            }

            DataManager.SaveCache(CacheKeyJwt, new HandshakeJwtStore
            {
                Token = token,
                ExpiresAt = expiresAt
            });
        }

        private static string HmacSha256Bytes(byte[] keyBytes, string data)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// Clear stored handshake credentials (sign out guest).
        /// </summary>
        public static void Clear()
        {
            DataManager.SaveCache<HandshakeKeyStore>(CacheKeyPrivateKey, null);
            DataManager.SaveCache<HandshakeJwtStore>(CacheKeyJwt, null);
            GuestJwt = null;
        }
    }
}
