using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Authenticates the desktop client against the cloud platform and holds the
    /// resulting session token used as the bearer for all API calls. Real accounts
    /// only — Discord OAuth (browser loopback) or email + password. There is no
    /// anonymous/guest path.
    /// </summary>
    public static class CloudAuthManager
    {
        private const string TokenCacheKey = "cloud_desktop_token";

        /// <summary>Loopback the browser is redirected back to after Discord SSO.</summary>
        private const string LoopbackUrl = "http://localhost:3000/callback/";

        private static readonly HttpClient Http = new();

        /// <summary>The active bearer token, or null when signed out.</summary>
        public static string? CurrentToken { get; private set; }

        /// <summary>The signed-in account, or null when signed out.</summary>
        public static CloudUser? CurrentUser { get; private set; }

        public static bool IsAuthenticated => !string.IsNullOrEmpty(CurrentToken);

        /// <summary>When the desktop token stops being accepted, if the server said so.</summary>
        public static DateTime? TokenExpiresAt { get; private set; }

        public static event Action? AuthenticationChanged;

        /// <summary>How long a successful validation is trusted before re-checking.</summary>
        private static readonly TimeSpan ValidationInterval = TimeSpan.FromMinutes(15);

        private static readonly SemaphoreSlim ValidationLock = new(1, 1);
        private static DateTime _lastValidatedUtc = DateTime.MinValue;

        /// <summary>Restore a persisted token at startup.</summary>
        public static void Initialize()
        {
            var stored = DataManager.LoadCache<TokenStore>(TokenCacheKey);
            if (stored != null && !string.IsNullOrEmpty(stored.Token))
            {
                CurrentToken = stored.Token;
                CurrentUser = stored.User;
                TokenExpiresAt = stored.ExpiresAt;
            }
        }

        /// <summary>
        /// Whether the account can sign in with the given provider ("discord", "email").
        /// The Supabase equivalent decoded the identity list out of the session JWT;
        /// a session token is opaque, so this reads what the API reported instead.
        /// </summary>
        public static bool HasProvider(string provider)
        {
            if (!IsAuthenticated || CurrentUser == null) return false;

            if (string.Equals(provider, "email", StringComparison.OrdinalIgnoreCase) && CurrentUser.HasPassword)
                return true;

            return CurrentUser.Providers.Any(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirm the desktop token is still good. session tokens are opaque and
        /// cannot be refreshed, so there is nothing to renew: the check is whether the
        /// server still accepts it. A rejected token is cleared, which surfaces to the
        /// UI as a signed-out state — the same outcome as a failed Supabase refresh.
        ///
        /// Successful checks are cached, so this stays cheap on hot paths like sync.
        /// </summary>
        public static async Task<bool> EnsureValidSessionAsync()
        {
            if (!IsAuthenticated) return false;

            if (TokenExpiresAt.HasValue && TokenExpiresAt.Value <= DateTime.UtcNow)
            {
                SupabaseAuthManager.AppendLog("[Cloud/Auth] Desktop token expired; signing out.");
                Logout();
                return false;
            }

            if (DateTime.UtcNow - _lastValidatedUtc < ValidationInterval)
                return true;

            await ValidationLock.WaitAsync();
            try
            {
                // Another caller may have validated while this one waited.
                if (DateTime.UtcNow - _lastValidatedUtc < ValidationInterval)
                    return IsAuthenticated;

                var (status, body) = await CloudApiClient.TryCallApiAsync("me", HttpMethod.Get);

                if (status == 401 || status == 403)
                {
                    SupabaseAuthManager.AppendLog($"[Cloud/Auth] Desktop token rejected ({status}); signing out.");
                    Logout();
                    return false;
                }

                if (status < 200 || status >= 300)
                {
                    // Server trouble or no network — keep the session and retry later
                    // rather than signing the user out over a transient failure.
                    SupabaseAuthManager.AppendLog($"[Cloud/Auth] Session check inconclusive ({status}); keeping session.");
                    return true;
                }

                // Refresh the cached identity so provider-gated UI follows changes
                // made on the web dashboard (linking Discord, setting a password).
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        CurrentUser = ParseUser(data);
                        Persist();
                    }
                }
                catch (JsonException)
                {
                    // Keep the cached user; the token is what mattered here.
                }

                _lastValidatedUtc = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Cloud/Auth] Session check failed: {ex.Message}");
                return true;
            }
            finally
            {
                ValidationLock.Release();
            }
        }

        private static void Persist() =>
            DataManager.SaveCache(TokenCacheKey, new TokenStore
            {
                Token = CurrentToken,
                User = CurrentUser,
                ExpiresAt = TokenExpiresAt,
            });

        /// <summary>Exchange email + password for a desktop session token.</summary>
        public static Task<(bool Success, string? Error)> LoginWithEmailAsync(string email, string password)
        {
            return ExchangeAsync("auth/token", new
            {
                email,
                password,
                device_name = DeviceName(),
            });
        }

        /// <summary>
        /// Run the Discord OAuth loopback flow: open the browser to the backend, catch
        /// the one-time code on a local listener, and exchange it for a session token.
        /// </summary>
        public static async Task<(bool Success, string? Error)> LoginWithDiscordAsync()
        {
            var (code, error) = await AwaitDiscordCodeAsync();
            if (string.IsNullOrEmpty(code))
                return (false, error ?? "Discord sign-in was not completed.");

            return await ExchangeAsync("auth/discord/token", new
            {
                code,
                device_name = DeviceName(),
            });
        }

        public static void Logout()
        {
            TeamSyncWebSocketService.Shutdown();
            CloudServerInfo.Reset();
            CurrentToken = null;
            CurrentUser = null;
            TokenExpiresAt = null;
            _lastValidatedUtc = DateTime.MinValue;
            DataManager.SaveCache<TokenStore?>(TokenCacheKey, null);
            DataManager.SaveCache<PlayerWipeTracker.PlayerWipeTrackerCapabilities?>(PlayerWipeTracker.PlayerWipeTrackerCapabilityService.CacheKey, null);
            AuthenticationChanged?.Invoke();
        }

        /// <summary>POST a public auth payload and persist the returned token + user.</summary>
        private static async Task<(bool Success, string? Error)> ExchangeAsync(string routePath, object payload)
        {
            try
            {
                var url = CloudBackend.ApiUrl(DataManager.CLOUD_API_BASEURL, routePath);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    SupabaseAuthManager.HandleUpgradeRequiredResponse(body);
                    return (false, ExtractError(body));
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("token", out var tokenEl) ||
                    tokenEl.GetString() is not { Length: > 0 } token)
                {
                    return (false, "The server response did not contain a token.");
                }

                CurrentToken = token;
                CurrentUser = data.TryGetProperty("user", out var userEl) ? ParseUser(userEl) : null;
                TokenExpiresAt = data.TryGetProperty("expires_at", out var expiresEl)
                                 && expiresEl.ValueKind == JsonValueKind.String
                                 && DateTime.TryParse(expiresEl.GetString(), CultureInfo.InvariantCulture,
                                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresAt)
                    ? expiresAt
                    : null;
                _lastValidatedUtc = DateTime.UtcNow;
                Persist();

                SupabaseAuthManager.AppendLog($"[Cloud/Auth] Signed in as {CurrentUser?.Email ?? CurrentUser?.Id ?? "user"}.");
                TeamSyncWebSocketService.Initialize();
                AuthenticationChanged?.Invoke();
                return (true, null);
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Cloud/Auth] Sign-in failed: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Open the browser to the website sign-in handoff and await the loopback code.
        /// The website (/desktop/connect) owns login/registration — any method, including
        /// Discord — and hands back a one-time code once the user is authenticated. The
        /// desktop no longer drives an OAuth dance of its own.
        /// </summary>
        private static async Task<(string? Code, string? Error)> AwaitDiscordCodeAsync()
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add(LoopbackUrl);

            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                return (null, $"Could not start the local sign-in listener: {ex.Message}");
            }

            try
            {
                var startUrl = $"{DataManager.CLOUD_API_BASEURL.TrimEnd('/')}/desktop/connect" +
                               $"?redirect_uri={Uri.EscapeDataString(LoopbackUrl)}";
                Process.Start(new ProcessStartInfo { FileName = startUrl, UseShellExecute = true });

                // Give the user a few minutes to complete the browser sign-in.
                var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3));
                var code = context.Request.QueryString["code"];

                await WriteBrowserResponseAsync(context.Response, success: !string.IsNullOrEmpty(code));
                BringAppToForeground();

                return string.IsNullOrEmpty(code)
                    ? (null, "Discord authorization did not return a code.")
                    : (code, null);
            }
            catch (TimeoutException)
            {
                return (null, "Discord sign-in timed out.");
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
            finally
            {
                if (listener.IsListening)
                    listener.Stop();
            }
        }

        private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool success)
        {
            var html = success
                ? "<!doctype html><html><head><meta charset='utf-8'><title>Rust+ Desktop</title></head><body><h1>Signed in</h1><p>You can close this tab and return to Rust+ Desktop.</p></body></html>"
                : "<!doctype html><html><head><meta charset='utf-8'><title>Rust+ Desktop</title></head><body><h1>Sign-in failed</h1><p>Something went wrong. Please try again from Rust+ Desktop.</p></body></html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        private static void BringAppToForeground()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Application.Current.MainWindow is not Window window) return;
                if (!window.IsVisible) window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }));
        }

        private static string DeviceName()
        {
            try
            {
                var name = Environment.MachineName;
                return string.IsNullOrWhiteSpace(name) ? "Rust+ Desktop" : name;
            }
            catch
            {
                return "Rust+ Desktop";
            }
        }

        private static CloudUser ParseUser(JsonElement userEl)
        {
            var providers = new List<string>();
            if (userEl.TryGetProperty("providers", out var providersEl) && providersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var provider in providersEl.EnumerateArray())
                {
                    if (provider.ValueKind == JsonValueKind.String && provider.GetString() is { Length: > 0 } name)
                        providers.Add(name);
                }
            }

            return new CloudUser
            {
                Id = GetString(userEl, "id"),
                Email = GetString(userEl, "email"),
                DisplayName = GetString(userEl, "display_name") ?? GetString(userEl, "name"),
                Providers = providers,
                HasPassword = userEl.TryGetProperty("has_password", out var hasPassword)
                              && hasPassword.ValueKind == JsonValueKind.True,
            };
        }

        private static string? GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>Pull a human-readable message out of an API validation/error body.</summary>
        private static string ExtractError(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in errors.EnumerateObject())
                    {
                        if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                            return field.Value[0].GetString() ?? "Sign-in failed.";
                    }
                }

                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? "Sign-in failed.";
            }
            catch (JsonException)
            {
                // Non-JSON body — fall through.
            }

            return "Sign-in failed. Please check your details and try again.";
        }

        public sealed class CloudUser
        {
            public string? Id { get; set; }
            public string? Email { get; set; }
            public string? DisplayName { get; set; }

            /// <summary>
            /// Sign-in methods the account supports ("discord", "email", ...), as
            /// reported by the API. session tokens carry no claims, so this cannot be
            /// derived client-side the way Supabase identities were read from the JWT.
            /// </summary>
            public List<string> Providers { get; set; } = new();

            public bool HasPassword { get; set; }
        }

        private sealed class TokenStore
        {
            public string? Token { get; set; }
            public CloudUser? User { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }
    }
}
