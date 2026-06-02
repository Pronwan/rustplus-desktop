using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Services.Data;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using static Supabase.Gotrue.Constants;

namespace RustPlusDesk.Services.Auth
{
    public static class SupabaseAuthManager
    {
        public static Supabase.Client Client { get; private set; }
        public static bool IsPremium { get; private set; }
        public static string CurrentTier { get; private set; } = "free";
        public static string DiscordProviderToken { get; private set; }
        public static bool IsGuestAuthenticated { get; private set; }
        private static readonly SemaphoreSlim SessionRefreshLock = new SemaphoreSlim(1, 1);

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
                    AutoRefreshToken = true,
                    AutoConnectRealtime = true,
                    SessionHandler = new DesktopSessionHandler()
                };

                Client = new Supabase.Client(url, key, options);
                await Client.InitializeAsync();

                // Explicitly restore the persisted Discord session.
                // Client.InitializeAsync() loads the session via SessionHandler but may not
                // call RefreshSession() automatically when the AccessToken is expired.
                // We manually load + SetSession to force a token refresh via the RefreshToken.
                AppendLog("[Supabase] Restoring persisted Discord session...");
                try
                {
                    var saved = DataManager.LoadCache<Session>("supabase_session");
                    if (saved != null &&
                        !string.IsNullOrEmpty(saved.AccessToken) &&
                        !string.IsNullOrEmpty(saved.RefreshToken))
                    {
                        // SetSession will use the RefreshToken to get a fresh AccessToken if needed
                        var restored = await Client.Auth.SetSession(saved.AccessToken, saved.RefreshToken);
                        if (restored != null)
                        {
                            AppendLog($"[Supabase] Discord session restored. User: {restored.User?.Email}");
                        }
                        else
                        {
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
                    AppendLog($"[Supabase] Session restore error: {authEx.Message}. Cloud sync will run with anon key.");
                }

                // If no Discord session, try guest handshake auth
                if (!IsDiscordAuthenticated)
                {
                    await TryInitializeGuestAuthAsync();
                }

                await RefreshUserProfileAsync();
                AppendLog($"[Supabase] Init complete. IsDiscordAuthenticated={IsDiscordAuthenticated}, IsGuestAuthenticated={IsGuestAuthenticated}, IsPremium={IsPremium}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Supabase] Initialization error: {ex.Message}");
            }
        }

        /// <summary>True if any auth session exists (Discord OAuth or guest handshake).</summary>
        public static bool IsAuthenticated => IsDiscordAuthenticated || IsGuestAuthenticated;

        /// <summary>True only when Discord OAuth is connected.</summary>
        public static bool IsDiscordAuthenticated => Client?.Auth?.CurrentSession != null;

        public static async Task<bool> EnsureFreshSessionAsync()
        {
            // Guest JWT refresh — no refresh token, so call the handshake refresh flow
            if (IsGuestAuthenticated)
            {
                try
                {
                    if (HandshakeService.HasValidJwt)
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
            if (session == null) return true;

            var expiresAt = session.CreatedAt.ToUniversalTime().AddSeconds(session.ExpiresIn);
            if (expiresAt > DateTime.UtcNow.AddMinutes(2))
                return true;

            await SessionRefreshLock.WaitAsync();
            try
            {
                session = Client?.Auth?.CurrentSession;
                if (session == null) return true;

                expiresAt = session.CreatedAt.ToUniversalTime().AddSeconds(session.ExpiresIn);
                if (expiresAt > DateTime.UtcNow.AddMinutes(2))
                    return true;

                AppendLog("[Cloud/Debug] Refreshing expired Supabase session...");
                var refreshed = await Client.Auth.RefreshSession();
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

            try
            {
                var destroySession = Client?.Auth?.GetType().GetMethod(
                    "DestroySession",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                if (destroySession != null)
                    destroySession.Invoke(Client.Auth, null);
                else if (Client?.Auth != null)
                    await Client.Auth.SignOut();
            }
            catch
            {
                // Best effort: a broken local session should not keep poisoning anon requests.
            }

            CurrentTier = "free";
            IsPremium = false;
            AppendLog("[Cloud] Discord session expired. Cloud sync continues with anon/free limits until you log in again.");
            return false;
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
                    HandshakeService.Clear();
                    await SyncDiscordRolesAsync();
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Supabase] Login error: {ex.Message}");
                return false;
            }
        }

        public static async Task RefreshUserProfileAsync()
        {
            if (!IsDiscordAuthenticated) return;
            if (!await EnsureFreshSessionAsync()) return;
            string discordId = null;
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
            if (discordId == null) return;

            string steamId = null;
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is RustPlusDesk.Views.MainWindow mainWin)
                    {
                        // Access the _vm or SteamId64 directly from MainWindow
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
                return;
            }
            
            try
            {
                AppendLog($"[Cloud/Debug] Querying user profile for SteamID: {steamId}");
                var response = await Client.From<RustPlusDesk.Models.UserProfileModel>()
                    .Filter("steam_id", Postgrest.Constants.Operator.Equals, steamId)
                    .Single();
                
                if (response != null)
                {
                    CurrentTier = response.SubscriptionTier ?? "free";
                    IsPremium = response.IsManualSupporter || CurrentTier == "supporter" || CurrentTier == "developer" || CurrentTier == "lead_contributor" || CurrentTier == "lead_developer";
                    AppendLog($"[Cloud/Debug] Found existing profile. Tier: {CurrentTier} (IsPremium: {IsPremium})");
                    await TouchProfileAsync(steamId, discordId);
                }
                else
                {
                    var newProfile = new RustPlusDesk.Models.UserProfileModel
                    {
                        SteamId = steamId,
                        UserId = Client.Auth.CurrentUser?.Id,
                        DiscordId = discordId,
                        DiscordName = Client.Auth.CurrentUser?.UserMetadata?.ContainsKey("full_name") == true ? Client.Auth.CurrentUser.UserMetadata["full_name"]?.ToString() : null,
                        SubscriptionTier = "free",
                        SyncAccepted = TrackingService.CloudSyncEnabled,
                        LastActiveAt = DateTime.UtcNow,
                        IsOnline = true
                    };
                    AppendLog($"[Cloud/Debug] No profile found. Creating new user profile for SteamId={steamId}, DiscordId={discordId}");
                    await Client.From<RustPlusDesk.Models.UserProfileModel>().Insert(newProfile);
                    CurrentTier = "free";
                    IsPremium = false;
                    AppendLog("[Cloud] Created new user profile row in database successfully.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Profile query error, attempting to insert new profile: {ex.Message}");
                try
                {
                    var newProfile = new RustPlusDesk.Models.UserProfileModel
                    {
                        SteamId = steamId,
                        UserId = Client.Auth.CurrentUser?.Id,
                        DiscordId = discordId,
                        DiscordName = Client.Auth.CurrentUser?.UserMetadata?.ContainsKey("full_name") == true ? Client.Auth.CurrentUser.UserMetadata["full_name"]?.ToString() : null,
                        SubscriptionTier = "free",
                        SyncAccepted = TrackingService.CloudSyncEnabled,
                        LastActiveAt = DateTime.UtcNow,
                        IsOnline = true
                    };
                    await Client.From<RustPlusDesk.Models.UserProfileModel>().Insert(newProfile);
                    CurrentTier = "free";
                    IsPremium = false;
                    AppendLog("[Cloud] Inserted new user profile row successfully.");
                }
                catch (Exception insertEx)
                {
                    AppendLog($"[Cloud/Error] Failed to create new user profile: {insertEx.Message}");
                }
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

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var url = $"{DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/discord-roles";
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url);
                    request.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Client.Auth.CurrentSession.AccessToken);
                    request.Content = new System.Net.Http.StringContent(jsonBody, Encoding.UTF8, "application/json");

                    var responseMsg = await httpClient.SendAsync(request);
                    var response = await responseMsg.Content.ReadAsStringAsync();
                    if (!responseMsg.IsSuccessStatusCode)
                    {
                        throw new Exception($"HTTP {responseMsg.StatusCode}: {response}");
                    }
                    AppendLog($"[Cloud] Edge Function completed. Response: {response}");
                }
                await RefreshUserProfileAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Error] Failed to sync roles via Edge Function: {ex.Message}");
                await RefreshUserProfileAsync();
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

            if (req.HttpMethod == "GET" && !req.Url.Query.Contains("access_token") && !req.Url.Query.Contains("code"))
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
                await Client.Auth.SetSession(accessToken, refreshToken);
                if (qs["provider_token"] != null)
                {
                    DiscordProviderToken = qs["provider_token"];
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
                ? "<html><body><h1>Authentication Successful!</h1><p>You can close this window and return to Rust+ Desktop.</p></body></html>"
                : "<html><body><h1>Authentication Failed</h1><p>Something went wrong.</p></body></html>";

            var responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            res.ContentLength64 = responseBytes.Length;
            await res.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            res.Close();
            listener.Stop();

            return success;
        }

        public static async Task LogoutAsync()
        {
            if (Client != null && IsAuthenticated)
            {
                IsGuestAuthenticated = false;
                HandshakeService.Clear();
                await Client.Auth.SignOut();
            }
        }

        public static async Task UpdateCloudSyncConsentAsync(bool accepted)
        {
            if (!IsAuthenticated) return;
            if (!await EnsureFreshSessionAsync()) return;
            string steamId = TrackingService.SteamId64;
            if (string.IsNullOrEmpty(steamId) || steamId == "0") return;

            try
            {
                await Client.From<RustPlusDesk.Models.UserProfileModel>()
                    .Filter("steam_id", Postgrest.Constants.Operator.Equals, steamId)
                    .Set(x => x.SyncAccepted, accepted)
                    .Set(x => x.LastActiveAt, DateTime.UtcNow)
                    .Set(x => x.IsOnline, true)
                    .Update();
                AppendLog($"[Cloud] Updated database consent status to: {accepted}");
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Error] Failed to update consent status in database: {ex.Message}");
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
                var teamJson = JsonSerializer.Serialize(teamMembers);
                var teamCount = teamMembers.Count;

                await Client.From<RustPlusDesk.Models.UserProfileModel>()
                    .Filter("steam_id", Postgrest.Constants.Operator.Equals, steamId)
                    .Set(x => x.LastActiveAt, DateTime.UtcNow)
                    .Set(x => x.IsOnline, true)
                    .Set(x => x.CurrentServerKey, serverKey ?? "")
                    .Set(x => x.CurrentServerName, serverName ?? "")
                    .Set(x => x.TeamMemberCount, teamCount)
                    .Set(x => x.TeamMembersJson, teamJson)
                    .Update();
            }
            catch (Exception ex)
            {
                AppendLog($"[Cloud/Debug] Presence update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempt guest handshake auth when no Discord session is available.
        /// Uses stored JWT if valid, refreshes if expired, or registers a new keypair.
        /// </summary>
        private static async Task TryInitializeGuestAuthAsync()
        {
            try
            {
                string steamId = TrackingService.SteamId64;
                if (string.IsNullOrEmpty(steamId) || steamId == "0")
                {
                    AppendLog("[Supabase/Guest] No SteamID yet — skipping guest handshake.");
                    return;
                }

                // Check if we have a valid stored JWT
                if (HandshakeService.HasValidJwt)
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
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Supabase/Guest] Handshake error: {ex.Message}");
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

            var update = Client.From<RustPlusDesk.Models.UserProfileModel>()
                .Filter("steam_id", Postgrest.Constants.Operator.Equals, steamId)
                .Set(x => x.LastActiveAt, DateTime.UtcNow)
                .Set(x => x.IsOnline, true);

            if (!string.IsNullOrWhiteSpace(Client.Auth.CurrentUser?.Id))
                update = update.Set(x => x.UserId, Client.Auth.CurrentUser.Id);

            if (!string.IsNullOrWhiteSpace(discordId))
                update = update.Set(x => x.DiscordId, discordId);

            await update.Update();
        }

        public static async Task<(bool IsAdmin, string? ErrorMessage)> CheckIsAdminDetailedAsync()
        {
            if (Client == null) return (false, "Supabase client not initialized.");
            if (!IsDiscordAuthenticated) return (false, "No active Supabase session (Discord login required).");
            try
            {
                if (!await EnsureFreshSessionAsync()) return (false, "Session expired and could not be refreshed.");
                var result = await Client.Rpc<bool>("is_admin", null);
                return (result, null);
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                if (ex.InnerException != null) errMsg += " -> " + ex.InnerException.Message;
                AppendLog($"[Cloud/Error] Admin check RPC failed: {errMsg}");
                return (false, errMsg);
            }
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
        public void DestroySession() => DataManager.SaveCache<Session>(CacheKey, null);
    }
}







