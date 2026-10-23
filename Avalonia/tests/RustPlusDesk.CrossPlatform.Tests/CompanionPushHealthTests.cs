using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Infrastructure.Accounts;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionPushHealthTests
{
    [TestMethod]
    public async Task Healthy_RequiresExactProbeAtMatchingSource_AndDoesNotExposeTokens()
    {
        using var fixture = new Fixture { Deliver = true, RequireMatchingSource = true };
        var result = await fixture.Health.CheckAsync(fixture.Files.Profiles, CancellationToken.None);
        Assert.AreEqual(PushHealthOutcome.Healthy, result.Outcome);
        Assert.IsFalse(result.Message.Contains("fixture-expo", StringComparison.Ordinal));
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.IsTrue(fixture.Health.TryConsume(fixture.Files.Profiles, fixture.Title));
        Assert.IsFalse(fixture.Health.TryConsume(fixture.Files.Profiles, "Rust server notification"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExpoAcknowledgementWithoutArrival_IsNotHealthy(bool arrayTicket)
    {
        using var fixture = new Fixture { ArrayTicket = arrayTicket };
        var result = await fixture.Health.CheckAsync(fixture.Files.Profiles, CancellationToken.None);
        Assert.AreEqual(PushHealthOutcome.NotDelivered, result.Outcome);
        Assert.IsTrue(result.NeedsRepair);
        Assert.AreEqual(2, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitUnregisteredTicketOrReceipt_RequestsRepair(bool atReceipt)
    {
        using var fixture = new Fixture { TicketError = atReceipt ? null : "DeviceNotRegistered", ReceiptError = atReceipt ? "DeviceNotRegistered" : null };
        var result = await fixture.Health.CheckAsync(fixture.Files.Profiles, CancellationToken.None);
        Assert.AreEqual(PushHealthOutcome.TokenRejected, result.Outcome);
        Assert.IsTrue(result.NeedsRepair);
    }

    [TestMethod]
    [DataRow("network")]
    [DataRow("send-http")]
    [DataRow("receipt-http")]
    [DataRow("malformed")]
    [DataRow("provider-error")]
    public async Task TransientOrMalformedResponse_IsInconclusiveAndNeverTriggersRepair(string failure)
    {
        using var fixture = new Fixture { Failure = failure };
        var result = await fixture.Health.CheckAsync(fixture.Files.Profiles, CancellationToken.None);
        Assert.AreEqual(PushHealthOutcome.Inconclusive, result.Outcome);
        Assert.IsFalse(result.NeedsRepair);
        Assert.IsFalse(result.Message.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(AccountState.Expired, PushHealthOutcome.SignInRequired)]
    [DataRow(AccountState.Rejected, PushHealthOutcome.SignInRequired)]
    [DataRow(AccountState.Missing, PushHealthOutcome.SignInRequired)]
    [DataRow(AccountState.Offline, PushHealthOutcome.Inconclusive)]
    public async Task UnverifiedAccount_DoesNotSendExpoProbe(AccountState account, PushHealthOutcome expected)
    {
        using var fixture = new Fixture { Account = account };
        var result = await fixture.Health.CheckAsync(fixture.Files.Profiles, CancellationToken.None);
        Assert.AreEqual(expected, result.Outcome);
        Assert.AreEqual(0, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task Cancellation_CleansPendingProbeAndPreservesConfiguration()
    {
        using var fixture = new Fixture { HoldArrival = true };
        string original = await File.ReadAllTextAsync(fixture.Files.Config);
        using var cancel = new CancellationTokenSource();
        var running = fixture.Health.CheckAsync(fixture.Files.Profiles, cancel.Token);
        await fixture.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(4));
        cancel.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => running);
        Assert.IsTrue(fixture.Health.TryConsume(fixture.Files.Profiles, fixture.Title));
        Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Files.Config));
    }

    private sealed class Fixture : IDisposable
    {
        public PairingFiles Files { get; } = new();
        public bool Deliver { get; init; }
        public bool RequireMatchingSource { get; init; }
        public bool ArrayTicket { get; init; }
        public bool HoldArrival { get; init; }
        public string? TicketError { get; init; }
        public string? ReceiptError { get; init; }
        public string? Failure { get; init; }
        public AccountState Account { get; init; } = AccountState.Active;
        public string? Title { get; private set; }
        public List<string> Requests { get; } = [];
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CompanionPushHealth Health { get; }
        private readonly HttpClient http;
        public Fixture()
        {
            File.WriteAllText(Files.Config, "{\"expo_push_token\":\"fixture-expo\"}");
            http = new HttpClient(new Handler(async (request, ct) =>
            {
                if (Failure == "network") throw new HttpRequestException("private response body and token");
                Requests.Add(request.RequestUri!.AbsolutePath);
                bool send = request.RequestUri.AbsolutePath.EndsWith("/send", StringComparison.Ordinal);
                if (send)
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    Title = body.RootElement.GetProperty("title").GetString();
                    Assert.AreEqual("fixture-expo", body.RootElement.GetProperty("to").GetString());
                    Assert.AreEqual(JsonValueKind.Object, body.RootElement.GetProperty("data").ValueKind);
                    if (Deliver && !RequireMatchingSource) Health!.TryConsume(Files.Profiles, Title);
                    string ticket = JsonSerializer.Serialize(new { status = TicketError is null && Failure != "provider-error" ? "ok" : "error", id = "fixture-ticket", details = new { error = TicketError ?? "MessageRateExceeded" } });
                    return new HttpResponseMessage(Failure == "send-http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                    { Content = new StringContent(Failure == "malformed" ? "private invalid JSON" : "{\"data\":" + (ArrayTicket ? "[" + ticket + "]" : ticket) + "}") };
                }
                return new HttpResponseMessage(Failure == "receipt-http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(new { data = new Dictionary<string, object> { ["fixture-ticket"] = new { status = ReceiptError is null ? "ok" : "error", details = new { error = ReceiptError } } } })) };
            }));
            Health = new CompanionPushHealth(http, new AccountService(() => Account), async (arrival, ct) =>
            {
                Waiting.TrySetResult();
                if (RequireMatchingSource)
                {
                    Assert.IsTrue(Health!.TryConsume(Path.Combine(Files.Directory, "another-profiles.json"), Title));
                    Assert.IsFalse(arrival.IsCompleted);
                    Assert.IsTrue(Health.TryConsume(Files.Profiles, Title));
                }
                if (HoldArrival) { await arrival.WaitAsync(ct); return true; }
                return arrival.IsCompletedSuccessfully;
            });
        }
        public void Dispose() { http.Dispose(); Files.Dispose(); }
    }

    internal sealed class AccountService(Func<AccountState> account) : ICompanionAccountService
    {
        public Task<CompanionAccount> CheckAsync(string path, CancellationToken ct) => Task.FromResult(new CompanionAccount(account()));
        public Task<CompanionAccount> SignInAsync(string path, CancellationToken ct) => throw new AssertFailedException("Health checks must not open Steam login.");
        public Task<CompanionAccount> RenewPushAsync(string path, CancellationToken ct) => throw new AssertFailedException("The probe service must not renew on its own.");
        public Task<CompanionSignOutResult> SignOutAsync(string path, CompanionSignOutScope scope, CancellationToken ct) => throw new AssertFailedException("Health checks must never sign out.");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
}
