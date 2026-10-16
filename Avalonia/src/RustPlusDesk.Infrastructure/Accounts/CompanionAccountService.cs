using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Registration;
using RustPlusApi.Fcm.Registration.Steps;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Infrastructure.Accounts;

/// <summary>Facepunch authentication and FCM registration with credentials confined to infrastructure.</summary>
public sealed class CompanionAccountService : ICompanionAccountService
{
    private static readonly Uri Endpoint = new("https://companion-rust.facepunch.com/");
    private readonly HttpClient http;
    private readonly ICompanionLoginBrowser browser;
    private readonly Func<CancellationToken, Task<Credentials>> acquirePush;

    public CompanionAccountService(HttpClient http, ICompanionLoginBrowser browser)
        : this(http, browser, ct => new FcmRegistration(http).AcquireCredentialsAsync(ct)) { }

    internal CompanionAccountService(HttpClient http, ICompanionLoginBrowser browser, Func<CancellationToken, Task<Credentials>> acquirePush)
    { this.http = http; this.browser = browser; this.acquirePush = acquirePush; }

    public async Task<CompanionAccount> CheckAsync(string configPath, CancellationToken cancellationToken)
    {
        try
        {
            var config = await CompanionConfigFile.ReadAsync(configPath, cancellationToken).ConfigureAwait(false);
            string? token = config.Text("rustplus_auth_token");
            if (string.IsNullOrWhiteSpace(token)) return new(AccountState.Missing);
            return await CheckTokenAsync(token, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CompanionAccountException) { return new(AccountState.Invalid); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(AccountState.Invalid); }
        catch (Exception) { return new(AccountState.Unavailable); }
    }

    public async Task<CompanionAccount> SignInAsync(string configPath, CancellationToken cancellationToken)
    {
        try
        {
            var config = await CompanionConfigFile.ReadAsync(configPath, cancellationToken).ConfigureAwait(false);
            // The native broker intercepts this exact redirect; it does not open a network listener.
            var callback = new Uri($"http://localhost:3000/callback/{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}");
            var result = await browser.AuthenticateAsync(new Uri(SteamLoginService.BuildLoginUrl(callback.AbsoluteUri)), callback, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null) throw new OperationCanceledException(cancellationToken);
            var fields = ParseCallback(result, callback);
            string token;
            if (fields.TryGetValue("code", out string? code) && !string.IsNullOrWhiteSpace(code))
            {
                using var response = await SendAsync(HttpMethod.Post, "login/exchange", new { code }, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new CompanionAccountException(AccountFailure.Rejected);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (body.Length > 65536) throw new CompanionAccountException(AccountFailure.Rejected);
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                token = root.TryGetProperty("token", out var value) || root.TryGetProperty("AuthToken", out value)
                    ? value.GetString() ?? "" : "";
            }
            else token = fields.GetValueOrDefault("token") ?? fields.GetValueOrDefault("auth_token")
                ?? fields.GetValueOrDefault("rustplus_auth_token") ?? fields.GetValueOrDefault("access_token")
                ?? fields.GetValueOrDefault("id_token") ?? "";
            if (string.IsNullOrWhiteSpace(token) || token.Length > 16384) throw new CompanionAccountException(AccountFailure.Rejected);
            var account = await CheckTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (account.State != AccountState.Active)
                throw new CompanionAccountException(account.State is AccountState.Rejected or AccountState.Expired ? AccountFailure.Rejected : AccountFailure.Unavailable);

            var push = await acquirePush(cancellationToken).ConfigureAwait(false);
            if (push.Gcm is null || push.Gcm.AndroidId == 0 || push.Gcm.SecurityToken == 0
                || string.IsNullOrWhiteSpace(push.Fcm?.Token) || string.IsNullOrWhiteSpace(push.ExpoPushToken))
                throw new CompanionAccountException(AccountFailure.PushRegistration);
            // A new registration can be rolled back without touching the previously configured device.
            string deviceId = "rustplus-" + Guid.NewGuid();
            bool committed = false;
            try
            {
                await new RustCompanionClient(http).RegisterAsync(token, push.ExpoPushToken, deviceId, cancellationToken).ConfigureAwait(false);
                config.Values["rustplus_auth_token"] = token;
                config.Values["rustplus_device_id"] = deviceId;
                config.Values["expo_push_token"] = push.ExpoPushToken;
                var fcm = config.Values["fcm_credentials"] as JsonObject ?? new();
                var gcm = fcm["gcm"] as JsonObject ?? new();
                var registration = fcm["fcm"] as JsonObject ?? new();
                gcm["androidId"] = push.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture);
                gcm["securityToken"] = push.Gcm.SecurityToken.ToString(CultureInfo.InvariantCulture);
                registration["token"] = push.Fcm!.Token;
                if (fcm["gcm"] is not JsonObject) fcm["gcm"] = gcm;
                if (fcm["fcm"] is not JsonObject) fcm["fcm"] = registration;
                if (config.Values["fcm_credentials"] is not JsonObject) config.Values["fcm_credentials"] = fcm;
                if (!string.IsNullOrEmpty(account.SteamId)) config.Values["steam_id"] = account.SteamId;
                else config.Values.Remove("steam_id");
                config.Values["issue_date"] = DateTimeOffset.UtcNow.ToString("O");
                config.Values["expiry_date"] = DateTimeOffset.UtcNow.AddDays(15).ToString("O");
                await config.SaveAsync(configPath, cancellationToken).ConfigureAwait(false);
                committed = true;
                return account;
            }
            finally
            {
                if (!committed)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        using var response = await SendAsync(HttpMethod.Delete, "api/push/unregister",
                            new { AuthToken = token, DeviceId = deviceId }, cleanup.Token).ConfigureAwait(false);
                    }
                    catch (Exception) { /* Local credentials remain unchanged if remote rollback is unavailable. */ }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (CompanionAccountException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw new CompanionAccountException(AccountFailure.InvalidConfiguration); }
        catch (Exception) { throw new CompanionAccountException(AccountFailure.Unavailable); }
    }

    private async Task<CompanionAccount> CheckTokenAsync(string token, CancellationToken cancellationToken)
    {
        var claims = CompanionToken.ReadClaims(token);
        if (claims.ExpiresAt <= DateTimeOffset.UtcNow) return new(AccountState.Expired, claims.SteamId, claims.ExpiresAt);
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "api/channel/check", new { AuthToken = token }, cancellationToken).ConfigureAwait(false);
            var state = response.StatusCode switch
            {
                HttpStatusCode.OK => AccountState.Active,
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => AccountState.Rejected,
                _ => AccountState.Unavailable
            };
            return new(state, claims.SteamId, claims.ExpiresAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(AccountState.Offline, claims.SteamId, claims.ExpiresAt); }
    }

    private async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string relative, T payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Endpoint, relative))
        { Content = JsonContent.Create(payload, options: JsonSerializerOptions.Default) };
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/4.9.2");
        return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static Dictionary<string, string> ParseCallback(Uri result, Uri expected)
    {
        if (!result.IsAbsoluteUri || result.Scheme != expected.Scheme || result.Host != expected.Host || result.Port != expected.Port
            || result.AbsolutePath != expected.AbsolutePath || !string.IsNullOrEmpty(result.UserInfo) || result.AbsoluteUri.Length > 32768)
            throw new CompanionAccountException(AccountFailure.Rejected);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in (result.Query.TrimStart('?') + "&" + result.Fragment.TrimStart('#')).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            string value = separator < 0 ? "" : Uri.UnescapeDataString(pair[(separator + 1)..]);
            if (!fields.TryAdd(key, value)) throw new CompanionAccountException(AccountFailure.Rejected);
        }
        if (fields.ContainsKey("error")) throw new CompanionAccountException(AccountFailure.Rejected);
        return fields;
    }
}
