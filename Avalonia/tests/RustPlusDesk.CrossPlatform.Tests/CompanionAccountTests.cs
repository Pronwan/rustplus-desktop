using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Infrastructure.Accounts;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionAccountTests
{
    [TestMethod]
    public async Task ConfigurationSave_ReplacesAnExistingDocumentPrivately()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Path, "{}");
        var file = await CompanionConfigFile.ReadAsync(fixture.Path, CancellationToken.None);
        file.Values["fixture"] = true;
        await file.SaveAsync(fixture.Path, CancellationToken.None);
        Assert.IsTrue(JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!["fixture"]!.GetValue<bool>());
    }
    [TestMethod]
    public async Task SignIn_ExchangesVerifiesRegistersThenSaves_WithoutLeakingTokenIntoResult()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Path, """
            {"unknown":{"nested":true},"fcm_credentials":{"extra":123,"gcm":{"future":"keep"},"fcm":{"future":false}}}
            """);
        var result = await fixture.Service.SignInAsync(fixture.Path, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "/login/exchange", "/api/channel/check", "/api/push/register" }, fixture.Requests.Select(item => item.Path).ToArray());
        Assert.AreEqual(AccountState.Active, result.State);
        Assert.AreEqual(Fixture.SteamId, result.SteamId);
        using (var check = JsonDocument.Parse(fixture.Requests.Single(request => request.Path == "/api/channel/check").Body))
            Assert.AreEqual(fixture.Token, check.RootElement.GetProperty("AuthToken").GetString());
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(fixture.Token, StringComparison.Ordinal));
        Assert.IsFalse(result.ToString().Contains("AuthToken", StringComparison.Ordinal));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
        Assert.AreEqual(fixture.Token, saved["rustplus_auth_token"]!.GetValue<string>());
        Assert.IsTrue(saved["unknown"]!["nested"]!.GetValue<bool>());
        Assert.AreEqual("keep", saved["fcm_credentials"]!["gcm"]!["future"]!.GetValue<string>());
        Assert.AreEqual(123, saved["fcm_credentials"]!["extra"]!.GetValue<int>());
        Assert.IsFalse(saved["fcm_credentials"]!["fcm"]!["future"]!.GetValue<bool>());
        Assert.AreEqual("42", saved["fcm_credentials"]!["gcm"]!["androidId"]!.GetValue<string>());
        Assert.AreEqual(1, Directory.GetFiles(fixture.Directory).Length);
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var permissions = new FileInfo(fixture.Path).GetAccessControl();
            Assert.IsTrue(permissions.AreAccessRulesProtected);
            foreach (FileSystemAccessRule rule in permissions.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                Assert.AreEqual(identity.User, rule.IdentityReference);
        }
        else Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.Path));
        Assert.AreEqual("companion-rust.facepunch.com", fixture.Browser.Request!.Host);
        StringAssert.Contains(WebUtility.UrlDecode(fixture.Browser.Request.Query), fixture.Browser.Callback!.AbsoluteUri);
    }

    [TestMethod]
    public async Task SignIn_DirectTokenFragment_DoesNotExchangeCode()
    {
        using var fixture = new Fixture();
        fixture.Browser.Result = uri => new Uri(uri.AbsoluteUri + "#auth_token=" + WebUtility.UrlEncode(fixture.Token));
        await fixture.Service.SignInAsync(fixture.Path, CancellationToken.None);
        Assert.IsFalse(fixture.Requests.Any(request => request.Path == "/login/exchange"));
    }

    [TestMethod]
    public async Task SignIn_ConflictingFile_UnregistersOnlyNewDeviceAndKeepsNewerFile()
    {
        using var fixture = new Fixture();
        fixture.AfterRegister = () => File.WriteAllText(fixture.Path, "{\"newer\":true}");
        var error = await Assert.ThrowsExceptionAsync<CompanionAccountException>(
            () => fixture.Service.SignInAsync(fixture.Path, CancellationToken.None));
        Assert.AreEqual(AccountFailure.ChangedConfiguration, error.Failure);
        Assert.AreEqual("{\"newer\":true}", await File.ReadAllTextAsync(fixture.Path));
        string registered = fixture.Requests.Single(request => request.Path == "/api/push/register").Body;
        string rollback = fixture.Requests.Single(request => request.Path == "/api/push/unregister").Body;
        using var one = JsonDocument.Parse(registered);
        using var two = JsonDocument.Parse(rollback);
        Assert.AreEqual(one.RootElement.GetProperty("DeviceId").GetString(), two.RootElement.GetProperty("DeviceId").GetString());
        Assert.AreEqual(1, Directory.GetFiles(fixture.Directory).Length);
    }

    [TestMethod]
    public async Task SignIn_RejectedToken_AndFailedPush_NeverReplaceExistingConfiguration()
    {
        using var fixture = new Fixture();
        const string original = "{\"existing\":\"private-old-token\"}";
        await File.WriteAllTextAsync(fixture.Path, original);
        fixture.Status = HttpStatusCode.Forbidden;
        await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.SignInAsync(fixture.Path, CancellationToken.None));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        Assert.IsFalse(fixture.Requests.Any(request => request.Path == "/api/push/register"));
        fixture.Status = HttpStatusCode.OK;
        fixture.RegisterStatus = HttpStatusCode.ServiceUnavailable;
        var error = await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.SignInAsync(fixture.Path, CancellationToken.None));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        Assert.IsFalse(error.ToString().Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SignIn_CanceledBrowser_AndCorruptConfiguration_DoNotAcquirePushOrWrite()
    {
        using var fixture = new Fixture();
        fixture.Browser.Result = _ => null;
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => fixture.Service.SignInAsync(fixture.Path, CancellationToken.None));
        Assert.IsFalse(File.Exists(fixture.Path));
        Assert.AreEqual(0, fixture.PushAcquisitions);
        await File.WriteAllTextAsync(fixture.Path, "private invalid input");
        var error = await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.SignInAsync(fixture.Path, CancellationToken.None));
        Assert.AreEqual(AccountFailure.InvalidConfiguration, error.Failure);
        Assert.AreEqual("private invalid input", await File.ReadAllTextAsync(fixture.Path));
        Assert.IsFalse(error.ToString().Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("https://attacker.test/callback/nonce?token=x")]
    [DataRow("http://localhost:3000/callback/other?token=x")]
    [DataRow("http://localhost:3001/callback/nonce?token=x")]
    [DataRow("http://localhost:3000/callback/nonce?code=one&code=two")]
    [DataRow("http://localhost:3000/callback/nonce?token=x&error=rejected")]
    [DataRow("http://user@localhost:3000/callback/nonce?token=x")]
    public void Callback_RejectsWrongOriginStateAndAmbiguousPayload(string candidate)
        => Assert.ThrowsException<CompanionAccountException>(() => CompanionAccountService.ParseCallback(new(candidate), new("http://localhost:3000/callback/nonce")));

    [TestMethod]
    public void Callback_PreservesLiteralAndEscapedBase64PlusSigns()
    {
        var fields = CompanionAccountService.ParseCallback(new("http://localhost:3000/callback/nonce#token=a+b%2Bc"), new("http://localhost:3000/callback/nonce"));
        Assert.AreEqual("a+b+c", fields["token"]);
    }

    [TestMethod]
    public async Task Check_MissingExpiredOfflineAndRejected_AreDistinctAndNeverWrite()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(AccountState.Missing, (await fixture.Service.CheckAsync(fixture.Path, CancellationToken.None)).State);
        Assert.IsFalse(File.Exists(fixture.Path));
        string expired = TokenFor(DateTimeOffset.UtcNow.AddDays(-1));
        await File.WriteAllTextAsync(fixture.Path, JsonSerializer.Serialize(new { rustplus_auth_token = expired }));
        Assert.AreEqual(AccountState.Expired, (await fixture.Service.CheckAsync(fixture.Path, CancellationToken.None)).State);
        Assert.AreEqual(0, fixture.Requests.Count);
        string original = JsonSerializer.Serialize(new { rustplus_auth_token = fixture.Token });
        await File.WriteAllTextAsync(fixture.Path, original);
        fixture.Status = HttpStatusCode.Forbidden;
        Assert.AreEqual(AccountState.Rejected, (await fixture.Service.CheckAsync(fixture.Path, CancellationToken.None)).State);
        fixture.NetworkError = true;
        Assert.AreEqual(AccountState.Offline, (await fixture.Service.CheckAsync(fixture.Path, CancellationToken.None)).State);
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
    }

    [TestMethod]
    public void Claims_SupportsFacepunchSignedTokensAndStandardJwt_WithoutTreatingClaimsAsVerification()
    {
        string signed = TokenFor(DateTimeOffset.UtcNow.AddDays(1));
        string payload = signed.Split('.')[0];
        Assert.AreEqual(Fixture.SteamId, CompanionToken.ReadClaims(signed).SteamId);
        Assert.AreEqual(Fixture.SteamId, CompanionToken.ReadClaims("e30." + payload + ".signature").SteamId);
        Assert.AreEqual("", CompanionToken.ReadClaims("malformed").SteamId);
    }

    private static string TokenFor(DateTimeOffset expiry) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { steamId = Fixture.SteamId, exp = expiry.ToUnixTimeSeconds() }))) + ".fixture-signature";

    [TestMethod]
    public async Task Renewal_VerifiesSavedToken_AndUsesSharedRegistrationWithoutOpeningBrowser()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Path, JsonSerializer.Serialize(new { rustplus_auth_token = fixture.Token, future = true }));
        var result = await fixture.Service.RenewPushAsync(fixture.Path, CancellationToken.None);
        Assert.AreEqual(AccountState.Active, result.State);
        Assert.IsNull(fixture.Browser.Request);
        CollectionAssert.AreEqual(new[] { "/api/channel/check", "/api/push/register" }, fixture.Requests.Select(request => request.Path).ToArray());
        Assert.IsTrue(JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!["future"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task Renewal_RejectedTokenNeverAcquiresNewCredentials_AndConflictKeepsOriginalRegistration()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Path, JsonSerializer.Serialize(new { rustplus_auth_token = fixture.Token }));
        fixture.Status = HttpStatusCode.Forbidden;
        var error = await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.RenewPushAsync(fixture.Path, CancellationToken.None));
        Assert.AreEqual(AccountFailure.Rejected, error.Failure);
        Assert.AreEqual(0, fixture.PushAcquisitions);
        fixture.Status = HttpStatusCode.OK;
        fixture.AfterRegister = () => File.WriteAllText(fixture.Path, "{\"newer\":true}");
        error = await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.RenewPushAsync(fixture.Path, CancellationToken.None));
        Assert.AreEqual(AccountFailure.ChangedConfiguration, error.Failure);
        Assert.IsNull(fixture.Browser.Request);
        Assert.AreEqual("{\"newer\":true}", await File.ReadAllTextAsync(fixture.Path));
        Assert.AreEqual("/api/push/unregister", fixture.Requests[^1].Path);
    }

    [TestMethod]
    [DataRow(CompanionSignOutScope.CurrentDevice, "/api/push/unregister", "DELETE")]
    [DataRow(CompanionSignOutScope.AllDevices, "/api/auth/invalidate", "POST")]
    public async Task SignOut_UsesExpectedRemoteScope_AndRemovesKnownCredentialsPreservingUnknownFields(CompanionSignOutScope scope, string endpoint, string method)
    {
        using var fixture = new Fixture();
        await fixture.Service.SignInAsync(fixture.Path, CancellationToken.None);
        var config = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!.AsObject();
        config["future"] = true;
        config["fcm_credentials"]!["gcm"]!["future"] = "preserve";
        config["fcm_credentials"]!["fcm"]!["extra"] = 1;
        await File.WriteAllTextAsync(fixture.Path, config.ToJsonString());
        var result = await fixture.Service.SignOutAsync(fixture.Path, scope, CancellationToken.None);
        Assert.AreEqual(CompanionSignOutResult.Completed, result);
        Assert.AreEqual(endpoint, fixture.Requests[^1].Path);
        Assert.AreEqual(method, fixture.Methods[^1]);
        using var sent = JsonDocument.Parse(fixture.Requests[^1].Body);
        Assert.AreEqual(fixture.Token, sent.RootElement.GetProperty("AuthToken").GetString());
        if (scope == CompanionSignOutScope.CurrentDevice)
            Assert.AreEqual(config["rustplus_device_id"]!.GetValue<string>(), sent.RootElement.GetProperty("DeviceId").GetString());
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
        Assert.IsTrue(saved["future"]!.GetValue<bool>());
        Assert.AreEqual("preserve", saved["fcm_credentials"]!["gcm"]!["future"]!.GetValue<string>());
        Assert.AreEqual(1, saved["fcm_credentials"]!["fcm"]!["extra"]!.GetValue<int>());
        foreach (string field in new[] { "rustplus_auth_token", "rustplus_device_id", "expo_push_token", "steam_id", "issue_date", "expiry_date" }) Assert.IsNull(saved[field]);
        Assert.IsNull(saved["fcm_credentials"]!["gcm"]!["androidId"]);
        Assert.IsNull(saved["fcm_credentials"]!["gcm"]!["securityToken"]);
        Assert.IsNull(saved["fcm_credentials"]!["fcm"]!["token"]);
    }

    [TestMethod]
    public async Task SignOut_RemoteFailureAndCancellation_PreserveOriginalConfiguration()
    {
        using var fixture = new Fixture();
        await fixture.Service.SignInAsync(fixture.Path, CancellationToken.None);
        string original = await File.ReadAllTextAsync(fixture.Path);
        fixture.SignOutStatus = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.SignOutAsync(fixture.Path, CompanionSignOutScope.CurrentDevice, CancellationToken.None));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        fixture.NetworkError = true;
        await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.SignOutAsync(fixture.Path, CompanionSignOutScope.AllDevices, CancellationToken.None));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => fixture.Service.SignOutAsync(fixture.Path, CompanionSignOutScope.CurrentDevice, cancel.Token));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
    }

    [TestMethod]
    public async Task SignOut_RemoteSuccessAndNewerLocalFile_ReportsPartialCompletionWithoutOverwriting()
    {
        using var fixture = new Fixture();
        await fixture.Service.SignInAsync(fixture.Path, CancellationToken.None);
        fixture.AfterSignOut = () => File.WriteAllText(fixture.Path, "{\"newer\":true}");
        var result = await fixture.Service.SignOutAsync(fixture.Path, CompanionSignOutScope.AllDevices, CancellationToken.None);
        Assert.AreEqual(CompanionSignOutResult.RemoteCompletedLocalCleanupFailed, result);
        Assert.AreEqual("{\"newer\":true}", await File.ReadAllTextAsync(fixture.Path));
    }

    [TestMethod]
    public async Task SignOut_MissingDeviceIdNeverInventsOne_AndRejectedTokenDoesNotClaimRemoteCompletion()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Path, JsonSerializer.Serialize(new { rustplus_auth_token = fixture.Token }));
        var error = await Assert.ThrowsExceptionAsync<CompanionAccountException>(() => fixture.Service.SignOutAsync(fixture.Path, CompanionSignOutScope.CurrentDevice, CancellationToken.None));
        Assert.AreEqual(AccountFailure.MissingDeviceRegistration, error.Failure);
        Assert.AreEqual(0, fixture.Requests.Count);
        fixture.SignOutStatus = HttpStatusCode.Forbidden;
        var result = await fixture.Service.SignOutAsync(fixture.Path, CompanionSignOutScope.AllDevices, CancellationToken.None);
        Assert.AreEqual(CompanionSignOutResult.LocalOnly, result);
        Assert.IsNull(JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!["rustplus_auth_token"]);
    }

    private sealed class Fixture : IDisposable
    {
        public const string SteamId = "76561198000000001";
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rustplus-accounts-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "rustplusjs-config.json");
        public string Token { get; } = TokenFor(DateTimeOffset.UtcNow.AddDays(1));
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode RegisterStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode SignOutStatus { get; set; } = HttpStatusCode.OK;
        public bool NetworkError { get; set; }
        public int PushAcquisitions { get; private set; }
        public Action? AfterRegister { get; set; }
        public Action? AfterSignOut { get; set; }
        public List<(string Path, string Body)> Requests { get; } = [];
        public List<string> Methods { get; } = [];
        public Browser Browser { get; } = new();
        private readonly HttpClient http;
        public CompanionAccountService Service { get; }
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            http = new(new Handler(async (request, ct) =>
            {
                if (NetworkError) throw new HttpRequestException("private request details");
                string path = request.RequestUri!.AbsolutePath;
                Requests.Add((path, await request.Content!.ReadAsStringAsync(ct)));
                Methods.Add(request.Method.Method);
                if (path == "/api/push/register") AfterRegister?.Invoke();
                bool signOut = path is "/api/push/unregister" or "/api/auth/invalidate";
                if (signOut) AfterSignOut?.Invoke();
                return new(path == "/api/channel/check" ? Status : path == "/api/push/register" ? RegisterStatus : signOut ? SignOutStatus : HttpStatusCode.OK)
                { Content = new StringContent(path == "/login/exchange" ? JsonSerializer.Serialize(new { token = Token }) : "{}") };
            }));
            Service = new(http, Browser, _ =>
            {
                PushAcquisitions++;
                return Task.FromResult(new Credentials
                {
                    Gcm = new() { AndroidId = 42, SecurityToken = 43 },
                    Fcm = new() { Token = "fixture-fcm" },
                    ExpoPushToken = "fixture-expo"
                });
            });
        }
        public void Dispose() { http.Dispose(); System.IO.Directory.Delete(Directory, recursive: true); }
    }

    private sealed class Browser : ICompanionLoginBrowser
    {
        public Uri? Request { get; private set; }
        public Uri? Callback { get; private set; }
        public Func<Uri, Uri?> Result { get; set; } = uri => new Uri(uri.AbsoluteUri + "?code=fixture-code");
        public Task<Uri?> AuthenticateAsync(Uri requestUri, Uri callbackUri, CancellationToken cancellationToken)
        { Request = requestUri; Callback = callbackUri; return Task.FromResult(Result(callbackUri)); }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
