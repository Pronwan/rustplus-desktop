using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Services.Data;
using WpfUi = Wpf.Ui.Controls;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using static Supabase.Gotrue.Constants;

namespace RustPlusDesk.Services.Auth
{
    public static class SupabaseAuthManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public NativeInputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)] public NativeKeyboardInput Keyboard;
            [FieldOffset(0)] public NativeMouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeKeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        public static Supabase.Client Client { get; private set; } = null!;
        public static bool IsPremium { get; private set; }
        public static string CurrentTier { get; private set; } = "free";
        public static string DiscordProviderToken { get; private set; } = string.Empty;
        public static bool IsGuestAuthenticated { get; private set; }
        private static readonly SemaphoreSlim SessionRefreshLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim CloudSyncConsentLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim ProfileRefreshLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim ProfileTouchLock = new SemaphoreSlim(1, 1);
        private static DateTime LastProfileRefreshUtc = DateTime.MinValue;
        private static string? LastProfileRefreshIdentity;
        private static DateTime LastProfileTouchUtc = DateTime.MinValue;
        private static string? LastProfileTouchIdentity;
        private static string? ConfirmedCloudSyncConsentIdentity;
        private static bool CloudAccountPromptShownThisSession;
        private static bool GuestRegistrationFailedPermanently;
        // ponytail: Supabase hides password presence during OAuth sessions; use a server profile flag if cross-device detection becomes necessary.
        private const string EmailPasswordAccountsCacheKey = "cloud_email_password_accounts";
        private static readonly object EmailPasswordAccountsLock = new();
        private static readonly System.Collections.Generic.HashSet<string> EmailPasswordAccounts = new(
            DataManager.LoadCache<System.Collections.Generic.List<string>>(EmailPasswordAccountsCacheKey) ?? [],
            StringComparer.OrdinalIgnoreCase);

        public static System.Collections.Generic.Dictionary<string, RustPlusDesk.Models.TierLimitModel> TierLimits { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public static async Task FetchTierLimitsAsync(bool forceRefresh = false)
        {
            if (Client == null) return;
            if (!forceRefresh && TierLimits != null && TierLimits.Count > 0) return;
            try
            {
                var body = await CallEdgeFunctionAsync("user-profile/limits", HttpMethod.Get);
                var limits = JsonSerializer.Deserialize<System.Collections.Generic.List<RustPlusDesk.Models.TierLimitModel>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (limits != null)
                {
                    var dict = new System.Collections.Generic.Dictionary<string, RustPlusDesk.Models.TierLimitModel>(StringComparer.OrdinalIgnoreCase);
                    foreach (var limit in limits)
                    {
                        if (limit.TierCode != null)
                        {
                            dict[limit.TierCode] = limit;
                        }
                    }
                    TierLimits = dict;
                    AppendLog($"[Cloud] Loaded {TierLimits.Count} tier limits dynamically from database.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Error] Failed to fetch tier limits: {ex.Message}. Using default limits.");
            }
        }

        public static int GetMaxOverlayBytes()
        {
            if (TierLimits.TryGetValue(CurrentTier, out var limit))
            {
                return limit.MaxOverlayKb.HasValue ? limit.MaxOverlayKb.Value * 1024 : int.MaxValue;
            }
            
            // Fallbacks
            if (IsPremium)
            {
                return 3_000_000; // 3 MB default for premium
            }
            return 300_000; // 300 KB default for free
        }

        public static int GetMaxDevices()
        {
            if (TierLimits.TryGetValue(CurrentTier, out var limit))
            {
                return limit.MaxDevices.HasValue ? limit.MaxDevices.Value : int.MaxValue;
            }
            
            if (IsPremium)
            {
                return int.MaxValue;
            }
            return 10;
        }

        public static int GetMaxBases()
        {
            if (TierLimits.TryGetValue(CurrentTier, out var limit))
            {
                return limit.MaxBases.HasValue ? limit.MaxBases.Value : int.MaxValue;
            }
            
            if (IsPremium)
            {
                return 10;
            }
            return 2;
        }

        public static int GetMaxScreenshotsPerBase()
        {
            if (TierLimits.TryGetValue(CurrentTier, out var limit))
            {
                return limit.MaxScreenshotsPerBase.HasValue ? limit.MaxScreenshotsPerBase.Value : 1;
            }
            
            if (IsPremium)
            {
                return 5;
            }
            return 1;
        }

        private static void ApplyProfileTier(string? tier, bool isManualSupporter, DateTime? premiumUntil)
        {
            var profileTier = tier ?? "free";
            IsPremium = premiumUntil.HasValue
                ? premiumUntil.Value.ToUniversalTime() > DateTime.UtcNow
                : isManualSupporter || (profileTier != "free" && !string.Equals(profileTier, "guest", StringComparison.OrdinalIgnoreCase));
            CurrentTier = IsPremium && string.Equals(profileTier, "free", StringComparison.OrdinalIgnoreCase)
                ? "supporter"
                : profileTier;
        }

        /// <summary>True when the current account has the requested sign-in provider linked.</summary>
        public static bool HasAuthProvider(string provider)
        {
            var user = Client?.Auth?.CurrentUser;
            if (user == null || Client?.Auth?.CurrentSession == null) return false;
            if (string.Equals(provider, "email", StringComparison.OrdinalIgnoreCase))
            {
                if (SessionUsesPassword()) RememberEmailPasswordAccount(user.Id);
                if (IsRememberedEmailPasswordAccount(user.Id)) return true;
            }
            return user.Identities?.Any(i => string.Equals(i.Provider, provider, StringComparison.OrdinalIgnoreCase)) == true ||
                   MetadataListsProvider(user.AppMetadata, provider);
        }

        private static bool IsRememberedEmailPasswordAccount(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            lock (EmailPasswordAccountsLock) return EmailPasswordAccounts.Contains(userId);
        }

        private static void RememberEmailPasswordAccount(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            lock (EmailPasswordAccountsLock)
            {
                if (!EmailPasswordAccounts.Add(userId)) return;
                DataManager.SaveCache(EmailPasswordAccountsCacheKey, EmailPasswordAccounts.ToList());
            }
        }

        private static bool MetadataListsProvider(System.Collections.Generic.Dictionary<string, object>? metadata, string provider) =>
            metadata != null &&
            ((metadata.TryGetValue("provider", out var primary) && string.Equals(primary?.ToString(), provider, StringComparison.OrdinalIgnoreCase)) ||
             (metadata.TryGetValue("providers", out var providers) && providers?.ToString()?.Split('[', ']', ',', '"', ' ', '\r', '\n')
                 .Any(value => string.Equals(value, provider, StringComparison.OrdinalIgnoreCase)) == true));

        /// <summary>True when email/password is linked to the current cloud account.</summary>
        public static bool IsEmailAuthenticated => HasAuthProvider("email");

        public static event Action? AuthenticationChanged;

        private static void NotifyAuthenticationChanged() => AuthenticationChanged?.Invoke();

        public static async Task InitializeAsync()
        {
            try
            {
                var url = DataManager.SUPABASE_URL;
                var key = DataManager.SUPABASE_ANON_KEY;

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
                {
                    Console.WriteLine("[Supabase] Missing credentials in .env. Cloud features disabled.");
                    return;
                }

                var options = new SupabaseOptions
                {
                    AutoRefreshToken = false,
                    AutoConnectRealtime = true,
                    SessionHandler = new DesktopSessionHandler()
                };

                Client = new Supabase.Client(url, key, options);
                await Client.InitializeAsync();
                
                StartKeepAliveTimer();
                StartProfileUpdateTimer();

                // Explicitly restore the persisted Discord session.
                // Client.InitializeAsync() loads the session via SessionHandler but may not
                // call RefreshSession() automatically when the AccessToken is expired.
                // We manually load + SetSession to force a token refresh via the RefreshToken.
                AppendLog("[Supabase] Restoring persisted Discord session...");
                bool hadPersistedAccountSession = false;
                bool accountSessionRestoreFailed = false;
                try
                {
                    var saved = DataManager.LoadCache<Session>("supabase_session");
                    if (saved != null &&
                        !string.IsNullOrEmpty(saved.AccessToken) &&
                        !string.IsNullOrEmpty(saved.RefreshToken))
                    {
                        hadPersistedAccountSession = true;
                        // SetSession will use the RefreshToken to get a fresh AccessToken if needed
                        var restored = await Client.Auth.SetSession(saved.AccessToken, saved.RefreshToken);
                        if (restored != null)
                        {
                            AppendLog($"[Supabase] Discord session restored. User: {restored.User?.Email}");
                        }
                        else
                        {
                            accountSessionRestoreFailed = true;
                            AppendLog("[Supabase] SetSession returned null - refresh token may be expired. Discord login required.");
                        }
                    }
                    else
                    {
                        // Also try RetrieveSessionAsync as secondary attempt
                        var session = await Client.Auth.RetrieveSessionAsync();
                        if (session != null)
                            AppendLog($"[Supabase] Session restored via RetrieveSessionAsync. User: {session.User?.Email}");
                        else
                            AppendLog("[Supabase] No saved session found. Cloud sync will run with anon key (free tier).");
                    }
                }
                catch (Exception authEx)
                {
                    accountSessionRestoreFailed = hadPersistedAccountSession;
                    AppendLog($"[Supabase] Session restore error: {authEx.Message}. Cloud sync will run with anon key.");
                }

                if (accountSessionRestoreFailed)
                {
                    await ClearCurrentSessionAsync();
                    ShowCloudAccountRequiredPromptOnce(sessionExpired: true);
                }
                // Guest auth is only for users without a persisted Discord/email account.
                else if (!IsDiscordAuthenticated && !IsEmailAuthenticated)
                {
                    await TryInitializeGuestAuthAsync();
                }

                await RefreshUserProfileAsync();
                await FetchTierLimitsAsync();
                TeamSyncWebSocketService.Initialize();
                AppendLog($"[Supabase] Init complete. IsDiscordAuthenticated={IsDiscordAuthenticated}, IsGuestAuthenticated={IsGuestAuthenticated}, IsPremium={IsPremium}");
                NotifyAuthenticationChanged();

                // Sync Discord roles on every launch when a Discord session is active,
                // not just after a fresh OAuth login.
                if (IsDiscordAuthenticated)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await SyncDiscordRolesAsync(); }
                        catch (Exception ex) { AppendLog($"[Supabase] Background role sync error: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Supabase] Initialization error: {ex.Message}");
            }
        }

        /// <summary>True if any auth session exists (Discord OAuth, Email, or guest handshake).</summary>
        public static bool IsAuthenticated => IsDiscordAuthenticated || IsEmailAuthenticated || IsGuestAuthenticated;

        /// <summary>True only when Discord OAuth is connected.</summary>
        public static bool IsDiscordAuthenticated => HasAuthProvider("discord");

        private static bool SessionUsesPassword()
        {
            try
            {
                string? token = Client?.Auth?.CurrentSession?.AccessToken;
                if (string.IsNullOrWhiteSpace(token)) return false;
                string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
                return document.RootElement.TryGetProperty("amr", out var methods) &&
                       methods.EnumerateArray().Any(entry =>
                           entry.TryGetProperty("method", out var method) && method.GetString() == "password");
            }
            catch
            {
                return false;
            }
        }

        private static string T(string key, string fallback)
        {
            return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }
        private static DateTime GetJwtExpiration(string token)
        {
            if (string.IsNullOrEmpty(token)) return DateTime.MinValue;
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2) return DateTime.MinValue;
                var payload = parts[1];
                payload = payload.Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var jsonBytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(jsonBytes);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("exp", out var expElement) && expElement.TryGetInt64(out var exp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        private static System.Threading.Timer? _keepAliveTimer;

        public static void StartKeepAliveTimer()
        {
            _keepAliveTimer ??= new System.Threading.Timer(async _ =>
            {
                if (IsAuthenticated)
                {
                    try { await EnsureFreshSessionAsync(); } catch { }
                }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private static System.Threading.Timer? _profileUpdateTimer;
        private static int _profileUpdateBusy = 0;

        public static void StartProfileUpdateTimer()
        {
            _profileUpdateTimer ??= new System.Threading.Timer(async _ =>
            {
                if (System.Threading.Interlocked.Exchange(ref _profileUpdateBusy, 1) == 1) return;
                try
                {
                    if (IsAuthenticated)
                    {
                        string steamId = TrackingService.SteamId64;
                        if (!string.IsNullOrEmpty(steamId) && steamId != "0")
                        {
                            string? discordId = null;
                            if (Client?.Auth?.CurrentUser?.UserMetadata != null)
                            {
                                if (Client.Auth.CurrentUser.UserMetadata.TryGetValue("provider_id", out var pidObj) && pidObj != null)
                                {
                                    discordId = pidObj.ToString();
                                }
                            }
                            if (string.IsNullOrEmpty(discordId))
                            {
                                discordId = Client?.Auth?.CurrentUser?.Identities != null && Client.Auth.CurrentUser.Identities.Count > 0
                                    ? Client.Auth.CurrentUser.Identities[0].Id
                                    : Client?.Auth?.CurrentUser?.Id;
                            }
                            await TouchProfileAsync(steamId, discordId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[Cloud/Debug] Auto profile touch failed: {ex.Message}");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _profileUpdateBusy, 0);
                }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public static async Task<bool> EnsureFreshSessionAsync()
        {
            // Guest JWT refresh — no refresh token, so call the handshake refresh flow
            if (IsGuestAuthenticated)
            {
                try
                {
                    if (HandshakeService.HasValidJwt && HandshakeService.GuestJwt != null)
                    {
                        await SetGuestSessionAsync(HandshakeService.GuestJwt);
                        return true;
                    }

                    if (HandshakeService.HasLocalKey)
                    {
                        var (success, error) = await HandshakeService.RefreshAsync();
                        if (success && HandshakeService.GuestJwt != null)
                        {
                            await SetGuestSessionAsync(HandshakeService.GuestJwt);
                            return true;
                        }
                        AppendLog($"[Cloud/Guest] Refresh failed: {error}. Re-registering.");
                    }

                    // Fall back to fresh registration
                    string steamId = TrackingService.SteamId64;
                    if (!string.IsNullOrEmpty(steamId) && steamId != "0")
                    {
                        var (regSuccess, regError, _) = await HandshakeService.RegisterAsync(steamId);
                        if (regSuccess && HandshakeService.GuestJwt != null)
                        {
                            await SetGuestSessionAsync(HandshakeService.GuestJwt);
                            return true;
                        }
                        AppendLog($"[Cloud/Guest] Re-registration failed: {regError}");
                    }

                    IsGuestAuthenticated = false;
                    CurrentTier = "free";
                    IsPremium = false;
                    return false;
                }
                catch (Exception ex)
                {
                    AppendLog($"[Cloud/Guest] Session refresh error: {ex.Message}");
                    return false;
                }
            }

            // Discord session refresh
            var session = Client?.Auth?.CurrentSession;
            if (session == null)
            {
                if (!IsGuestAuthenticated)
                    await TryInitializeGuestAuthAsync();
                return IsGuestAuthenticated;
            }

            if (string.IsNullOrEmpty(session.AccessToken)) return false;
            var expiresAt = GetJwtExpiration(session.AccessToken);
            if (expiresAt > DateTime.UtcNow.AddMinutes(2))
                return true;

            await SessionRefreshLock.WaitAsync();
            try
            {
                session = Client?.Auth?.CurrentSession;
                if (session == null) return true;

                if (string.IsNullOrEmpty(session.AccessToken)) return false;
                expiresAt = GetJwtExpiration(session.AccessToken);
                if (expiresAt > DateTime.UtcNow.AddMinutes(2))
                    return true;

                AppendLog("[Cloud/Debug] Refreshing expired Supabase session...");
                var auth = Client?.Auth;
                if (auth == null) return false;
                var refreshed = await auth.RefreshSession();
                if (refreshed != null)
                    return true;

                AppendLog("[Cloud/Debug] Supabase session refresh returned no session.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Supabase session refresh failed: {ex.Message}");
            }
            finally
            {
                SessionRefreshLock.Release();
            }

            await ClearCurrentSessionAsync();

            CurrentTier = "free";
            IsPremium = false;
            AppendLog("[Cloud] Account session expired. Please sign in again.");
            ShowCloudAccountRequiredPromptOnce(sessionExpired: true);
            return false;
        }

        private static async Task ClearCurrentSessionAsync()
        {
            try
            {
                var destroySession = Client?.Auth?.GetType().GetMethod(
                    "DestroySession",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (destroySession != null)
                    destroySession.Invoke(Client!.Auth, null);
                else if (Client?.Auth != null)
                    await Client.Auth.SignOut();
            }
            catch
            {
                new DesktopSessionHandler().DestroySession();
            }
            NotifyAuthenticationChanged();
        }

        public static async Task<bool> LoginWithDiscordAsync()
        {
            if (Client == null) return false;

            try
            {
                var callbackUrl = "http://localhost:3000/callback/";
                var state = await Client.Auth.SignIn(Provider.Discord, new SignInOptions { RedirectTo = callbackUrl, Scopes = "identify guilds guilds.members.read email" });

                if (state == null || state.Uri == null) return false;

                // Open browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = state.Uri.ToString(),
                    UseShellExecute = true
                });

                // Start local server to catch the redirect
                bool success = await AwaitOAuthCallback(callbackUrl);
                if (success)
                {
                    // Clear guest auth when Discord login succeeds
                    IsGuestAuthenticated = false;
                    CloudAccountPromptShownThisSession = false;
                    GuestRegistrationFailedPermanently = false;
                    HandshakeService.Clear();
                    await SyncDiscordRolesAsync();
                    NotifyAuthenticationChanged();
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Supabase] Login error: {ex.Message}");
                return false;
            }
        }

        public static async Task<(bool Success, string? Error)> LinkDiscordIdentityAsync()
        {
            if (Client?.Auth?.CurrentSession == null)
                return (false, "Sign in to your cloud account first.");

            try
            {
                const string callbackUrl = "http://localhost:3000/callback/";
                string endpoint = $"{DataManager.SUPABASE_URL.TrimEnd('/')}/auth/v1/user/identities/authorize";
                string query = $"provider=discord&redirect_to={Uri.EscapeDataString(callbackUrl)}" +
                               $"&scopes={Uri.EscapeDataString("identify guilds guilds.members.read email")}" +
                               "&skip_http_redirect=true";

                using var httpClient = new HttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{query}");
                request.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Client.Auth.CurrentSession.AccessToken);

                using var response = await httpClient.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return (false, $"Discord linking failed: {body}");

                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("url", out var urlElement) ||
                    string.IsNullOrWhiteSpace(urlElement.GetString()))
                    return (false, "Supabase did not return a Discord linking URL.");

                Process.Start(new ProcessStartInfo
                {
                    FileName = urlElement.GetString(),
                    UseShellExecute = true
                });

                if (!await AwaitOAuthCallback(callbackUrl))
                    return (false, "Discord linking was not completed.");

                await RefreshUserProfileAsync();
                await SyncDiscordRolesAsync();
                NotifyAuthenticationChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Discord] Identity link failed: {ex.Message}");
                return (false, ex.Message);
            }
        }

        public static async Task<(bool Success, string? Error)> AddEmailLoginAsync(string email, string password)
        {
            if (Client?.Auth?.CurrentUser == null)
                return (false, "Sign in to your cloud account first.");
            if (string.IsNullOrWhiteSpace(email) || password.Length < 6)
                return (false, "Enter a valid email and a password of at least 6 characters.");

            try
            {
                var attributes = new UserAttributes
                {
                    Password = password
                };
                if (!string.Equals(Client.Auth.CurrentUser.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
                    attributes.Email = email.Trim();
                await Client.Auth.Update(attributes);
                RememberEmailPasswordAccount(Client.Auth.CurrentUser.Id);
                NotifyAuthenticationChanged();
                return (true, null);
            }
            catch (Exception ex) when (ex.Message.Contains("same_password", StringComparison.OrdinalIgnoreCase))
            {
                // Supabase does not expose password presence in OAuth user data; this response proves one already exists.
                RememberEmailPasswordAccount(Client.Auth.CurrentUser.Id);
                NotifyAuthenticationChanged();
                AppendLog("[Cloud/Email] Existing email/password login confirmed.");
                return (true, "Email login was already enabled.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Email] Adding email login failed: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Sign in with email + password. On success the session is persisted via DesktopSessionHandler.
        /// Steam ID linkage is handled by RefreshUserProfileAsync (same as Discord flow).
        /// </summary>
        public static async Task<(bool Success, string? Error)> LoginWithEmailAsync(string email, string password)
        {
            if (Client == null) return (false, "Supabase not initialized.");
            try
            {
                var session = await Client.Auth.SignIn(email, password);
                if (session?.User == null)
                    return (false, T("EmailInvalidCredentialsError", "Invalid credentials. Please check your email and password."));

                IsGuestAuthenticated = false;
                CloudAccountPromptShownThisSession = false;
                GuestRegistrationFailedPermanently = false;
                HandshakeService.Clear();
                RememberEmailPasswordAccount(session.User.Id);

                await RefreshUserProfileAsync();
                AppendLog($"[Cloud] Email login successful. User: {session.User.Email}");
                NotifyAuthenticationChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("Email not confirmed"))
                    msg = T("EmailNotConfirmedError", "Email address not confirmed yet. Please click the confirmation link in your inbox.");
                else if (msg.Contains("Invalid login"))
                    msg = T("EmailInvalidCredentialsShortError", "Invalid credentials.");
                AppendLog($"[Cloud/Email] Login error: {ex.Message}");
                return (false, msg);
            }
        }

        /// <summary>
        /// Sends a password reset email to the given address.
        /// </summary>
        public static async Task<(bool Success, string? Error)> SendPasswordResetEmailAsync(string email)
        {
            if (Client == null) return (false, "Supabase not initialized.");
            try
            {
                await Client.Auth.ResetPasswordForEmail(new Supabase.Gotrue.ResetPasswordForEmailOptions(email) { 
                    RedirectTo = "https://rustplusdesktop.cloud/reset-password"
                });
                AppendLog($"[Cloud] Password reset email sent to: {email}");
                return (true, null);
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Email] Reset password error: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Register a new account with email + password.
        /// Supabase sends a confirmation email. Call PollEmailConfirmedAsync after signup to wait for it.
        /// </summary>
        public static async Task<(bool Success, string? Error)> SignUpWithEmailAsync(string email, string password)
        {
            if (Client == null) return (false, "Supabase not initialized.");
            try
            {
                var result = await Client.Auth.SignUp(email, password);
                if (result?.User == null)
                    return (false, T("EmailRegistrationFailed", "Registration failed."));

                AppendLog($"[Cloud] Email sign-up sent. Confirmation required for: {email}");
                return (true, null);
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (msg.Contains("already registered") || msg.Contains("User already registered"))
                    msg = T("EmailAlreadyRegisteredError", "This email address is already registered. Please sign in.");
                AppendLog($"[Cloud/Email] Sign-up error: {ex.Message}");
                return (false, msg);
            }
        }

        /// <summary>
        /// Polls Supabase every 4 seconds to check if the email has been confirmed.
        /// Calls onVerified when confirmed, onProgress on each poll tick.
        /// Max wait: ~5 minutes. Returns true when confirmed, false on timeout or cancellation.
        /// </summary>
        public static async Task<bool> PollEmailConfirmedAsync(
            string email, string password,
            Action? onProgress,
            CancellationToken cancellationToken)
        {
            if (Client == null) return false;
            var deadline = DateTime.UtcNow.AddMinutes(5);
            AppendLog("[Cloud/Email] Waiting for email confirmation...");

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var session = await Client.Auth.SignIn(email, password);
                    if (session?.User?.EmailConfirmedAt != null)
                    {
                        IsGuestAuthenticated = false;
                        CloudAccountPromptShownThisSession = false;
                        GuestRegistrationFailedPermanently = false;
                        HandshakeService.Clear();
                        RememberEmailPasswordAccount(session.User.Id);
                        await RefreshUserProfileAsync();
                        AppendLog("[Cloud/Email] Email confirmed and session active!");
                        NotifyAuthenticationChanged();
                        return true;
                    }
                }
                catch
                {
                    // Not confirmed yet → ignore and keep polling
                }

                onProgress?.Invoke();
                await Task.Delay(4000, CancellationToken.None);
            }

            AppendLog("[Cloud/Email] Email confirmation polling timed out.");
            return false;
        }

        public static async Task RefreshUserProfileAsync(bool forceRefresh = false)
        {
            if (!IsDiscordAuthenticated && !IsEmailAuthenticated) return;

            var identity = $"{Client?.Auth?.CurrentUser?.Id}:{TrackingService.SteamId64}";
            await ProfileRefreshLock.WaitAsync();
            try
            {
                if (!forceRefresh &&
                    identity == LastProfileRefreshIdentity &&
                    DateTime.UtcNow - LastProfileRefreshUtc < TimeSpan.FromMinutes(15))
                    return;

                if (await RefreshUserProfileCoreAsync())
                {
                    LastProfileRefreshIdentity = identity;
                    LastProfileRefreshUtc = DateTime.UtcNow;
                }
            }
            finally
            {
                ProfileRefreshLock.Release();
            }
        }

        private static async Task<bool> RefreshUserProfileCoreAsync()
        {
            // Run for Discord OR Email auth (not anon/guest — they use handshake)
            if (!IsDiscordAuthenticated && !IsEmailAuthenticated) return false;
            if (!await EnsureFreshSessionAsync()) return false;
            string? discordId = null;
            if (Client.Auth.CurrentUser?.UserMetadata != null)
            {
                if (Client.Auth.CurrentUser.UserMetadata.TryGetValue("provider_id", out var pidObj) && pidObj != null)
                {
                    discordId = pidObj.ToString();
                }
            }
            if (string.IsNullOrEmpty(discordId))
            {
                discordId = Client.Auth.CurrentUser?.Identities != null && Client.Auth.CurrentUser.Identities.Count > 0
                    ? Client.Auth.CurrentUser.Identities[0].Id
                    : Client.Auth.CurrentUser?.Id;
            }
            if (discordId == null) return false;

            string? steamId = null;
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                    {
                        var prop = mainWin.GetType().GetField("_vm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (prop != null)
                        {
                            var vm = prop.GetValue(mainWin);
                            var steamIdProp = vm?.GetType().GetProperty("SteamId64");
                            steamId = steamIdProp?.GetValue(vm) as string;
                        }
                    }
                });
            }

            if (string.IsNullOrEmpty(steamId) || steamId == "0")
            {
                steamId = TrackingService.SteamId64;
            }

            if (string.IsNullOrEmpty(steamId) || steamId == "0")
            {
                AppendLog("[Cloud/Debug] No valid SteamID64 available yet to sync user profile.");
                return false;
            }

            // ── Step 1: GET /user-profile ──
            AppendLog($"[Cloud/Debug] Querying user profile for SteamID: {steamId}");
            RustPlusDesk.Models.UserProfileModel? existingProfile = null;
            try
            {
                var queryParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["steam_id"] = steamId
                };
                var body = await CallEdgeFunctionAsync("user-profile", HttpMethod.Get, null, queryParams);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("profile", out var profileEl) && profileEl.ValueKind == JsonValueKind.Object)
                {
                    existingProfile = JsonSerializer.Deserialize<RustPlusDesk.Models.UserProfileModel>(profileEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch
            {
                // Profile might not exist or be hidden by RLS
            }

            if (existingProfile != null)
            {
                var previousTier = CurrentTier;
                ApplyProfileTier(existingProfile.SubscriptionTier, existingProfile.IsManualSupporter, existingProfile.PremiumUntil);
                AppendLog($"[Cloud/Debug] Found existing profile. Tier: {CurrentTier} (IsPremium: {IsPremium})");
                await FetchTierLimitsAsync(forceRefresh: TierLimits.Count == 0 || !string.Equals(previousTier, CurrentTier, StringComparison.OrdinalIgnoreCase));
                await TouchProfileAsync(steamId, discordId);
                return true;
            }

            // ── Step 2: Claim via secure Edge Function (user-profile/claim) ──
            AppendLog($"[Cloud/Debug] Profile not visible via RLS. Attempting claim via Edge Function user-profile/claim for SteamId={steamId}");
            try
            {
                var claimPayload = new
                {
                    steam_id = steamId
                };
                var claimResult = await CallEdgeFunctionAsync("user-profile/claim", HttpMethod.Post, claimPayload);
                if (!string.IsNullOrWhiteSpace(claimResult) && claimResult != "[]" && claimResult != "null")
                {
                    using var doc = JsonDocument.Parse(claimResult);
                    var root = doc.RootElement;
                    JsonElement row = default;
                    bool hasRow = false;

                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        row = root[0];
                        hasRow = true;
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        row = root;
                        hasRow = true;
                    }

                    if (hasRow)
                    {
                        var profileTier = row.TryGetProperty("subscription_tier", out var tierEl) ? tierEl.GetString() : "free";
                        var isManual = row.TryGetProperty("is_manual_supporter", out var manualEl) && manualEl.GetBoolean();
                        DateTime? premiumUntil = row.TryGetProperty("premium_until", out var premiumEl) && premiumEl.ValueKind == JsonValueKind.String && DateTime.TryParse(premiumEl.GetString(), out var parsedPremiumUntil)
                            ? parsedPremiumUntil
                            : null;
                        ApplyProfileTier(profileTier, isManual, premiumUntil);
                        AppendLog($"[Cloud] Claimed guest profile — linked to Discord/Email. Tier: {CurrentTier} (IsPremium: {IsPremium})");
                        await FetchTierLimitsAsync(forceRefresh: TierLimits.Count == 0);
                        await TouchProfileAsync(steamId, discordId);
                        return true;
                    }
                }
                AppendLog("[Cloud/Debug] claim returned empty — profile does not exist. Will create.");
            }
            catch (Exception claimEx)
            {
                AppendLog($"[Cloud/Debug] claim Edge Function error: {claimEx.Message}. Will attempt fresh insert.");
            }

            // ── Step 3: Insert fresh via POST /user-profile ──
            try
            {
                var newProfile = new
                {
                    steam_id = steamId,
                    user_id = Client.Auth.CurrentUser?.Id,
                    discord_id = discordId,
                    discord_name = Client.Auth.CurrentUser?.UserMetadata?.ContainsKey("full_name") == true ? Client.Auth.CurrentUser.UserMetadata["full_name"]?.ToString() : null,
                    subscription_tier = "free",
                    sync_accepted = TrackingService.CloudSyncEnabled,
                    last_active_at = DateTime.UtcNow,
                    is_online = true
                };
                AppendLog($"[Cloud/Debug] No profile found. Creating new user profile for SteamId={steamId}, DiscordId={discordId}");
                await CallEdgeFunctionAsync("user-profile", HttpMethod.Post, newProfile);
                CurrentTier = "free";
                IsPremium = false;
                AppendLog("[Cloud] Created new user profile row in database successfully.");
                return true;
            }
            catch (Exception insertEx)
            {
                AppendLog($"[Cloud/Error] Failed to create new user profile: {insertEx.Message}");
                return false;
            }
        }


        public static async Task SyncDiscordRolesAsync()
        {
            if (!IsDiscordAuthenticated) return;
            if (!await EnsureFreshSessionAsync()) return;

            try
            {
                // Ensure profile row exists first, otherwise Edge Function's update is a no-op
                await RefreshUserProfileAsync();

                AppendLog("[Cloud] Invoking discord-roles Edge Function...");
                var jsonBody = "{}";
                if (!string.IsNullOrEmpty(DiscordProviderToken))
                {
                    jsonBody = $"{{\"providerToken\":\"{DiscordProviderToken}\"}}";
                    AppendLog("[Cloud/Debug] Passing providerToken in body to Edge Function.");
                }
                else
                {
                    AppendLog("[Cloud/Debug] DiscordProviderToken is null/empty, calling Edge Function without it.");
                }

                if (IsUpgradeRequiredSnackbarShown)
                {
                    AppendLog("[Cloud] Skipping discord-roles sync: application update is required.");
                    await RefreshUserProfileAsync();
                    return;
                }

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var url = $"{DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/discord-roles";
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url);
                    request.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
                    var accessToken = Client.Auth.CurrentSession?.AccessToken;
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        AppendLog("[Cloud/Error] Cannot sync roles: missing Supabase access token.");
                        return;
                    }
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
                    request.Content = new System.Net.Http.StringContent(jsonBody, Encoding.UTF8, "application/json");

                    var responseMsg = await httpClient.SendAsync(request);
                    var response = await responseMsg.Content.ReadAsStringAsync();
                    if (!responseMsg.IsSuccessStatusCode)
                    {
                        throw new Exception($"HTTP {responseMsg.StatusCode}: {response}");
                    }
                    AppendLog($"[Cloud] Edge Function completed. Response: {response}");
                }
                await RefreshUserProfileAsync(forceRefresh: true);
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Error] Failed to sync roles via Edge Function: {ex.Message}");
                await RefreshUserProfileAsync(forceRefresh: true);
            }
        }

        private static async Task<bool> AwaitOAuthCallback(string listenUrl)
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add(listenUrl);
            listener.Start();

            Console.WriteLine($"[Supabase] Listening for OAuth callback on {listenUrl}...");

            var context = await listener.GetContextAsync();
            var req = context.Request;
            var res = context.Response;

            if (req.HttpMethod == "GET" && req.Url != null && !req.Url.Query.Contains("access_token") && !req.Url.Query.Contains("code"))
            {
                // Serve interceptor
                var html = @"<!DOCTYPE html><html><body><script>var h=window.location.hash.substring(1);var s=window.location.search.substring(1);if(h)window.location.href='/callback/?'+h;else if(s)window.location.href='/callback/?'+s;else document.body.innerHTML='Auth failed.';</script><p>Authenticating...</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(html);
                res.ContentLength64 = buffer.Length;
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                res.Close();

                context = await listener.GetContextAsync();
                req = context.Request;
                res = context.Response;
            }

            bool success = false;
            var qs = req.QueryString;
            
            if (qs["access_token"] != null && qs["refresh_token"] != null)
            {
                var accessToken = qs["access_token"];
                var refreshToken = qs["refresh_token"];
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                    return false;
                await Client.Auth.SetSession(accessToken, refreshToken);
                if (qs["provider_token"] != null)
                {
                    DiscordProviderToken = qs["provider_token"] ?? string.Empty;
                }
                success = true;
            }
            else if (qs["code"] != null)
            {
                // Depending on PKCE Flow
                // We'll just assume implicit for Discord or manual PKCE wasn't strictly configured in client options yet
                // The new client options usually have PKCE enabled by default in 0.16.2
                // We will attempt exchange, but typically `SetSession` is enough if implicit.
            }

            var responseHtml = success 
                ? "<!doctype html><html><head><meta charset='utf-8'><title>Rust+ Desktop</title></head><body><h1>Authentication successful</h1><p id='status'>Returning to Rust+ Desktop...</p><script>history.replaceState(null,'','/callback/');fetch('/callback/close',{method:'POST'}).finally(function(){window.close();setTimeout(function(){document.getElementById('status').textContent='Login complete. You can close this tab and return to Rust+ Desktop.';},500);});</script></body></html>"
                : "<html><body><h1>Authentication Failed</h1><p>Something went wrong.</p></body></html>";

            var responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            res.Headers[HttpResponseHeader.CacheControl] = "no-store";
            res.ContentLength64 = responseBytes.Length;
            await res.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            res.Close();

            IntPtr callbackBrowserWindow = IntPtr.Zero;
            if (success)
            {
                try
                {
                    var closeContext = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    bool closeCallbackTab = closeContext.Request.HttpMethod == "POST" &&
                                            closeContext.Request.Url?.AbsolutePath == "/callback/close";
                    if (closeCallbackTab) callbackBrowserWindow = GetForegroundWindow();
                    closeContext.Response.StatusCode = closeCallbackTab ? 204 : 404;
                    closeContext.Response.Close();
                }
                catch (TimeoutException)
                {
                    // JavaScript may be disabled; the app can still regain focus.
                }
            }

            listener.Stop();

            if (success)
            {
                if (callbackBrowserWindow != IntPtr.Zero)
                {
                    await Task.Delay(100);
                    AppendLog($"[Cloud/Auth] Browser tab close input: {SendCloseTabInput(callbackBrowserWindow)}/4 events sent.");
                    await Task.Delay(100);
                }

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Application.Current.MainWindow is Window mainWindow)
                    {
                        if (!mainWindow.IsVisible) mainWindow.Show();
                        if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
                        mainWindow.Activate();
                        mainWindow.Topmost = true;
                        mainWindow.Topmost = false;
                        mainWindow.Focus();
                    }
                }));
            }

            return success;
        }

        private static uint SendCloseTabInput(IntPtr callbackBrowserWindow)
        {
            var title = new StringBuilder(256);
            if (GetForegroundWindow() != callbackBrowserWindow ||
                GetWindowText(callbackBrowserWindow, title, title.Capacity) <= 0 ||
                !title.ToString().Contains("Rust+ Desktop", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"[Cloud/Auth] Browser tab close skipped; foreground title was '{title}'.");
                return 0;
            }

            SetForegroundWindow(callbackBrowserWindow);
            const uint keyboardInput = 1;
            const uint keyUp = 2;
            const ushort controlKey = 0x11;
            const ushort wKey = 0x57;
            var inputs = new[]
            {
                new NativeInput { Type = keyboardInput, Data = new NativeInputUnion { Keyboard = new NativeKeyboardInput { VirtualKey = controlKey } } },
                new NativeInput { Type = keyboardInput, Data = new NativeInputUnion { Keyboard = new NativeKeyboardInput { VirtualKey = wKey } } },
                new NativeInput { Type = keyboardInput, Data = new NativeInputUnion { Keyboard = new NativeKeyboardInput { VirtualKey = wKey, Flags = keyUp } } },
                new NativeInput { Type = keyboardInput, Data = new NativeInputUnion { Keyboard = new NativeKeyboardInput { VirtualKey = controlKey, Flags = keyUp } } }
            };
            int inputSize = Marshal.SizeOf<NativeInput>();
            uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
            if (sent != inputs.Length)
                AppendLog($"[Cloud/Auth] SendInput failed with Windows error {Marshal.GetLastWin32Error()} (INPUT size {inputSize}).");
            return sent;
        }

        public static async Task LogoutAsync()
        {
            if (Client != null && IsAuthenticated)
            {
                ConfirmedCloudSyncConsentIdentity = null;
                IsGuestAuthenticated = false;
                HandshakeService.Clear();
                await Client.Auth.SignOut();
                NotifyAuthenticationChanged();
            }
        }

        private static string? GetCloudSyncConsentIdentity()
        {
            string steamId = TrackingService.SteamId64;
            string? userId = Client?.Auth?.CurrentUser?.Id;
            if (string.IsNullOrEmpty(steamId) || steamId == "0" || string.IsNullOrEmpty(userId))
                return null;

            return $"{userId}:{steamId}";
        }

        private static async Task<bool> UpdateCloudSyncConsentCoreAsync(bool accepted)
        {
            if (!accepted)
                ConfirmedCloudSyncConsentIdentity = null;

            if (!IsAuthenticated) return false;
            if (!await EnsureFreshSessionAsync()) return false;

            string steamId = TrackingService.SteamId64;
            string? consentIdentity = GetCloudSyncConsentIdentity();
            if (consentIdentity == null) return false;

            try
            {
                var payload = new
                {
                    steam_id = steamId,
                    sync_accepted = accepted
                };
                await CallEdgeFunctionAsync("user-profile/consent", HttpMethod.Post, payload);
                ConfirmedCloudSyncConsentIdentity = accepted ? consentIdentity : null;
                AppendLog($"[Cloud] Updated database consent status to: {accepted}");
                return true;
            }
            catch (Exception ex)
            {
                if (accepted)
                    ConfirmedCloudSyncConsentIdentity = null;
                AppendLog($"[Cloud/Error] Failed to update consent status in database: {ex.Message}");
                return false;
            }
        }

        private static void PauseCloudSyncAfterConsentFailure()
        {
            // Keep UploadConsentGiven so a temporary network/auth failure does
            // not force the user to accept the disclaimer again. Only pause
            // uploading until they explicitly retry enabling cloud sync.
            ConfirmedCloudSyncConsentIdentity = null;
            TrackingService.CloudSyncEnabled = false;
            AppendLog("[Cloud/Error] Cloud sync paused because consent could not be confirmed. Retry enabling it when the connection is available.");
        }

        public static async Task<bool> UpdateCloudSyncConsentAsync(bool accepted)
        {
            await CloudSyncConsentLock.WaitAsync();
            try
            {
                bool updated = await UpdateCloudSyncConsentCoreAsync(accepted);
                if (accepted && !updated)
                    PauseCloudSyncAfterConsentFailure();
                return updated;
            }
            finally
            {
                CloudSyncConsentLock.Release();
            }
        }

        /// <summary>
        /// Confirms that cloud-sync consent has been persisted for the current
        /// authenticated user and Steam ID before any user-owned data is uploaded.
        /// This prevents autosync from racing the consent update and repeatedly
        /// failing the database ownership/consent RLS policies.
        /// </summary>
        public static async Task<bool> EnsureCloudSyncConsentAsync()
        {
            if (!TrackingService.CloudSyncEnabled || !TrackingService.UploadConsentGiven)
                return false;

            await CloudSyncConsentLock.WaitAsync();
            try
            {
                if (!TrackingService.CloudSyncEnabled || !TrackingService.UploadConsentGiven)
                    return false;

                string? consentIdentity = GetCloudSyncConsentIdentity();
                if (consentIdentity != null &&
                    string.Equals(ConfirmedCloudSyncConsentIdentity, consentIdentity, StringComparison.Ordinal))
                {
                    return true;
                }

                bool updated = await UpdateCloudSyncConsentCoreAsync(true);
                if (!updated)
                    PauseCloudSyncAfterConsentFailure();
                return updated;
            }
            finally
            {
                CloudSyncConsentLock.Release();
            }
        }

        public sealed class CloudTeamMemberDto
        {
            public string SteamId { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsOnline { get; set; }
            public bool IsDead { get; set; }
            public bool IsLeader { get; set; }
        }

        public static async Task UpdatePresenceAsync(string? serverKey, string? serverName, System.Collections.Generic.IReadOnlyCollection<CloudTeamMemberDto> teamMembers)
        {
            if (!IsAuthenticated) return;
            if (!await EnsureFreshSessionAsync()) return;
            string steamId = TrackingService.SteamId64;
            if (string.IsNullOrEmpty(steamId) || steamId == "0") return;

            try
            {
                var teamJson = TrackingService.CloudSyncEnabled ? JsonSerializer.Serialize(teamMembers) : "[]";
                var teamCount = TrackingService.CloudSyncEnabled ? teamMembers.Count : 0;
                var srvKey = TrackingService.CloudSyncEnabled ? (serverKey ?? "") : "";
                var srvName = TrackingService.CloudSyncEnabled ? (serverName ?? "") : "";

                var payload = new
                {
                    steam_id = steamId,
                    is_online = true,
                    current_server_key = srvKey,
                    current_server_name = srvName,
                    team_member_count = teamCount,
                    team_members_json = teamJson
                };
                await CallEdgeFunctionAsync("user-profile/presence", HttpMethod.Post, payload);
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Presence update failed: {ex.Message}");
            }
        }

        public static async Task MarkAppOfflineAsync()
        {
            if (!IsAuthenticated) return;
            if (!await EnsureFreshSessionAsync()) return;
            string steamId = TrackingService.SteamId64;
            if (string.IsNullOrEmpty(steamId) || steamId == "0") return;

            try
            {
                var payload = new
                {
                    steam_id = steamId,
                    is_online = false
                };
                await CallEdgeFunctionAsync("user-profile/presence", HttpMethod.Post, payload);
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] App offline update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempt guest handshake auth when no Discord session is available.
        /// Uses stored JWT if valid, refreshes if expired, or registers a new keypair.
        /// </summary>
        public static async Task TryInitializeGuestAuthAsync()
        {
            if (GuestRegistrationFailedPermanently)
            {
                AppendLog("[Supabase/Guest] Skipping — registration previously failed permanently.");
                return;
            }

            try
            {
                string steamId = TrackingService.SteamId64;
                if (string.IsNullOrEmpty(steamId) || steamId == "0")
                {
                    AppendLog("[Supabase/Guest] No SteamID yet — skipping guest handshake.");
                    return;
                }

                // Check if we have a valid stored JWT
                if (HandshakeService.HasValidJwt && HandshakeService.GuestJwt != null)
                {
                    AppendLog("[Supabase/Guest] Valid stored guest JWT found. Setting guest session.");
                    await SetGuestSessionAsync(HandshakeService.GuestJwt);
                    IsGuestAuthenticated = true;
                    return;
                }

                // Check if we have a stored keypair for refresh
                if (HandshakeService.HasLocalKey)
                {
                    AppendLog("[Supabase/Guest] Stored keypair found — attempting refresh handshake.");
                    var (success, error) = await HandshakeService.RefreshAsync();
                    if (success && HandshakeService.GuestJwt != null)
                    {
                        AppendLog("[Supabase/Guest] Refresh handshake succeeded.");
                        await SetGuestSessionAsync(HandshakeService.GuestJwt);
                        IsGuestAuthenticated = true;
                        return;
                    }
                    AppendLog($"[Supabase/Guest] Refresh failed: {error}. Re-registering.");
                }

                // First-time registration
                AppendLog("[Supabase/Guest] Performing first-time registration handshake.");
                var (regSuccess, regError, recoveryCode) = await HandshakeService.RegisterAsync(steamId);
                if (regSuccess && HandshakeService.GuestJwt != null)
                {
                    AppendLog("[Supabase/Guest] Registration handshake succeeded.");
                    if (!string.IsNullOrEmpty(recoveryCode))
                        AppendLog($"[Supabase/Guest] Recovery code saved. Keep this safe!");
                    await SetGuestSessionAsync(HandshakeService.GuestJwt);
                    IsGuestAuthenticated = true;
                }
                else
                {
                    AppendLog($"[Supabase/Guest] Registration failed: {regError}. Cloud sync disabled.");
                    if (regError == "Server returned no token")
                    {
                        GuestRegistrationFailedPermanently = true;
                        AppendLog("[Supabase/Guest] Registration permanently disabled for this session — server returned no token.");
                    }
                    ShowCloudAccountRequiredPromptOnce();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Supabase/Guest] Handshake error: {ex.Message}");
                ShowCloudAccountRequiredPromptOnce();
            }
        }

        private static void ShowCloudAccountRequiredPromptOnce(bool sessionExpired = false)
        {
            if (CloudAccountPromptShownThisSession || (!sessionExpired && !TrackingService.CloudSyncEnabled))
                return;

            if (!sessionExpired && (IsDiscordAuthenticated || IsEmailAuthenticated || IsGuestAuthenticated))
                return;

            CloudAccountPromptShownThisSession = true;

            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                    {
                        mainWin.UpdateCloudSyncUI();
                        mainWin.UpdateRustMapsUi();
                        var prompt = new RustPlusDesk.Views.Windows.CloudLoginPromptWindow(mainWin, sessionExpired)
                        {
                            Owner = mainWin
                        };
                        prompt.ShowDialog();
                    }
                }));
            }
            catch
            {
                // UI prompt is best effort; cloud sync remains disabled if auth is unavailable.
            }
        }

        /// <summary>
        /// Sets the guest JWT as the active Supabase session so subsequent
        /// data operations (From / Rpc) authenticate with the guest identity.
        /// </summary>
        private static async Task<bool> SetGuestSessionAsync(string jwt)
        {
            try
            {
                if (Client?.Auth == null) return false;
                var session = await Client.Auth.SetSession(jwt, "");
                return session != null;
            }
            catch (Exception ex)
            {
                AppendLog($"[Supabase/Guest] SetSession warning: {ex.Message}");
                return false;
            }
        }

        private static async Task TouchProfileAsync(string steamId, string? discordId = null)
        {
            if (Client?.Auth?.CurrentUser == null && !IsGuestAuthenticated) return;
            await ProfileTouchLock.WaitAsync();
            try
            {
                var identity = $"{Client?.Auth?.CurrentUser?.Id}:{steamId}";
                var minimized = CloudTrafficPolicy.IsMinimized;
                if (identity == LastProfileTouchIdentity &&
                    DateTime.UtcNow - LastProfileTouchUtc < CloudTrafficPolicy.ProfileTouchInterval(minimized))
                    return;

                var payload = new
                {
                    steam_id = steamId
                };
                await CallEdgeFunctionAsync("user-profile/touch", HttpMethod.Post, payload);
                LastProfileTouchIdentity = identity;
                LastProfileTouchUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Touch profile failed: {ex.Message}");
            }
            finally
            {
                ProfileTouchLock.Release();
            }
        }

        public static async Task<RustPlusDesk.Models.TeamFeatureMasterState?> HeartbeatTeamFeaturePresenceAsync(
            string steamId,
            string displayName,
            string serverKey,
            string serverName,
            string teamKey,
            int teamOrderIndex,
            bool wantsChatAlerts,
            bool wantsChatCommands)
        {
            if (Client == null) return null;
            if (!IsDiscordAuthenticated && !IsEmailAuthenticated) return null;
            if (!await EnsureFreshSessionAsync()) return null;

            try
            {
                var payload = new
                {
                    steam_id = steamId,
                    display_name = displayName,
                    server_key = serverKey,
                    server_name = serverName,
                    team_key = teamKey,
                    team_order_index = teamOrderIndex,
                    wants_chat_alerts = wantsChatAlerts,
                    wants_chat_commands = wantsChatCommands
                };

                var body = await CallEdgeFunctionAsync("team-feature/heartbeat", HttpMethod.Post, payload);
                if (string.IsNullOrWhiteSpace(body)) return null;

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var list = JsonSerializer.Deserialize<System.Collections.Generic.List<RustPlusDesk.Models.TeamFeatureMasterState>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return list?.FirstOrDefault();
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<RustPlusDesk.Models.TeamFeatureMasterState>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                return null;
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Team feature heartbeat failed: {ex.Message}");
                return null;
            }
        }

        private static readonly object _masterFetchLock = new();
        private static Task<RustPlusDesk.Models.TeamFeatureMasterState?>? _activeMasterFetchTask;
        private static string? _activeMasterFetchKey;
        private static RustPlusDesk.Models.TeamFeatureMasterState? _cachedMasterState;
        private static string? _cachedMasterKey;
        private static DateTime _cachedMasterExpiry = DateTime.MinValue;

        public static Task<RustPlusDesk.Models.TeamFeatureMasterState?> GetTeamFeatureMasterStateAsync(string serverKey, string teamKey)
        {
            if (Client == null || Client.Auth?.CurrentSession == null)
            {
                return Task.FromResult<RustPlusDesk.Models.TeamFeatureMasterState?>(null);
            }

            var key = $"{serverKey}:{teamKey}";
            lock (_masterFetchLock)
            {
                if (_cachedMasterKey == key && DateTime.UtcNow < _cachedMasterExpiry)
                {
                    return Task.FromResult(_cachedMasterState);
                }

                if (_activeMasterFetchTask != null && _activeMasterFetchKey == key)
                {
                    return _activeMasterFetchTask;
                }

                _activeMasterFetchKey = key;
                _activeMasterFetchTask = GetTeamFeatureMasterStateInternalAsync(serverKey, teamKey, key);
                return _activeMasterFetchTask;
            }
        }

        private static async Task<RustPlusDesk.Models.TeamFeatureMasterState?> GetTeamFeatureMasterStateInternalAsync(string serverKey, string teamKey, string key)
        {
            try
            {
                if (IsAuthenticated)
                    await EnsureFreshSessionAsync();

                var queryParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["server_key"] = serverKey,
                    ["team_key"] = teamKey
                };

                var body = await CallEdgeFunctionAsync("team-feature/master", HttpMethod.Get, null, queryParams);
                if (string.IsNullOrWhiteSpace(body)) return null;

                RustPlusDesk.Models.TeamFeatureMasterState? result = null;
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var list = JsonSerializer.Deserialize<System.Collections.Generic.List<RustPlusDesk.Models.TeamFeatureMasterState>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    result = list?.FirstOrDefault();
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    result = JsonSerializer.Deserialize<RustPlusDesk.Models.TeamFeatureMasterState>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                lock (_masterFetchLock)
                {
                    _cachedMasterKey = key;
                    _cachedMasterState = result;
                    _cachedMasterExpiry = DateTime.UtcNow.AddSeconds(4);
                }
                return result;
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Team feature state fetch failed: {ex.Message}");
                lock (_masterFetchLock)
                {
                    _cachedMasterKey = key;
                    _cachedMasterState = null;
                    _cachedMasterExpiry = DateTime.UtcNow.AddSeconds(4);
                }
                return null;
            }
            finally
            {
                lock (_masterFetchLock)
                {
                    if (_activeMasterFetchKey == key)
                    {
                        _activeMasterFetchTask = null;
                        _activeMasterFetchKey = null;
                    }
                }
            }
        }

        public static async Task<bool> HasActiveTeamFeatureMasterForMemberAsync(string serverKey, string steamId)
        {
            if (Client == null) return false;
            if (string.IsNullOrWhiteSpace(serverKey) || string.IsNullOrWhiteSpace(steamId)) return false;

            try
            {
                if (IsAuthenticated)
                    await EnsureFreshSessionAsync();

                var queryParams = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["server_key"] = serverKey,
                    ["steam_id"] = steamId
                };

                var body = await CallEdgeFunctionAsync("team-feature/has-master", HttpMethod.Get, null, queryParams);
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("has_master", out var hasMasterEl) && hasMasterEl.GetBoolean();
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Active team feature master check failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<(bool IsAdmin, string? ErrorMessage)> CheckIsAdminDetailedAsync()
        {
            if (Client == null) return (false, "Supabase client not initialized.");
            if (!IsDiscordAuthenticated) return (false, "No active Supabase session (Discord login required).");
            try
            {
                if (!await EnsureFreshSessionAsync()) return (false, "Session expired and could not be refreshed.");
                var body = await CallEdgeFunctionAsync("admin/check", HttpMethod.Get);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                bool isAdmin = root.TryGetProperty("is_admin", out var adminEl) && adminEl.GetBoolean();
                return (isAdmin, null);
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                if (ex.InnerException != null) errMsg += " -> " + ex.InnerException.Message;
                AppendLog($"[Cloud/Error] Admin check Edge Function failed: {errMsg}");
                return (false, errMsg);
            }
        }

        public static bool IsUpgradeRequiredSnackbarShown { get; set; } = false;

        private static readonly HttpClient Http = new();

        public static async Task<string> CallEdgeFunctionAsync(
            string functionName,
            HttpMethod method,
            object? payload = null,
            System.Collections.Generic.Dictionary<string, string>? queryParams = null)
        {
            if (Client == null)
                throw new InvalidOperationException("Supabase client not initialized.");

            if (IsUpgradeRequiredSnackbarShown)
                throw new InvalidOperationException("Cloud features are unavailable because an application update is required.");

            try
            {
                await EnsureFreshSessionAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Warning] Failed to ensure fresh session: {ex.Message}");
            }

            var url = $"{DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/{functionName}";
            if (queryParams != null && queryParams.Count > 0)
            {
                var queryStr = string.Join("&", queryParams.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
                url += "?" + queryStr;
            }

            AppendLog($"[Cloud/Debug] API Request: {method} /functions/v1/{functionName}" + (payload != null ? " (with payload)" : ""));

            var req = new HttpRequestMessage(method, url);
            req.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
            req.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
            
            if (Client.Auth?.CurrentSession != null)
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Client.Auth.CurrentSession.AccessToken);
            }

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            AppendLog($"[Cloud/Debug] API Response: {method} /functions/v1/{functionName} -> {(int)resp.StatusCode} {resp.StatusCode}");

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var errEl) && errEl.GetString() == "upgrade_required")
                    {
                        if (!IsUpgradeRequiredSnackbarShown)
                        {
                            IsUpgradeRequiredSnackbarShown = true;
                            string message = root.TryGetProperty("message", out var msgEl)
                                ? msgEl.GetString() ?? "An update is required to use cloud features."
                                : "An update is required to use cloud features.";
                            string upgradeUrl = root.TryGetProperty("upgrade_url", out var urlEl)
                                ? urlEl.GetString() ?? "https://github.com/JawadYzbk/rustplus-desktop/releases/latest"
                                : "https://github.com/JawadYzbk/rustplus-desktop/releases/latest";

                            if (Application.Current != null)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                                    {
                                        mainWin.ShowUpgradeRequiredSnackbar(message, upgradeUrl);
                                    }
                                });
                            }
                        }
                    }
                }
                catch { /* Ignore JSON parse errors */ }

                throw new Exception($"Edge Function {functionName} returned {resp.StatusCode}: {body}");
            }
            return body;
        }

        private static void AppendLog(string msg)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                    {
                        mainWin.AppendLog(msg);
                    }
                });
            }
        }
    }

    public class DesktopSessionHandler : IGotrueSessionPersistence<Session>
    {
        private const string CacheKey = "supabase_session";
        public void SaveSession(Session session) => DataManager.SaveCache(CacheKey, session);
        public Session? LoadSession() => DataManager.LoadCache<Session>(CacheKey);
        public void DestroySession() => DataManager.SaveCache<Session?>(CacheKey, null);
    }
}


