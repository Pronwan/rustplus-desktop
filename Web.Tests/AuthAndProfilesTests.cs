using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RustPlusWeb.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class AuthAndProfilesTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"rustplus-web-test-{Guid.NewGuid():N}.db");
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), $"rustplus-web-keys-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DATABASE_PATH"] = _databasePath,
                    ["DATA_PROTECTION_KEYS_PATH"] = _keysPath,
                    ["ADMIN_EMAIL"] = "admin@test.local",
                    ["ADMIN_PASSWORD"] = "AdminPassword!234",
                    ["WEB_INVITE_CODE"] = "test-invite-code"
                });
            });
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        if (Directory.Exists(_keysPath))
            Directory.Delete(_keysPath, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HealthIsPublicButServerProfilesRequireAuthentication()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var health = await client.GetAsync("/api/health");
        var profiles = await client.GetAsync("/api/servers");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.Unauthorized, profiles.StatusCode);
    }

    [Fact]
    public async Task ReadinessEndpointConfirmsDatabaseIsReachable()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var readiness = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var json = JsonDocument.Parse(await readiness.Content.ReadAsStringAsync());
        Assert.Equal("ready", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CrossOriginStateChangingRequestsAreRejected()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@test.local",
            password = "AdminPassword!234"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var hubResponse = await client.GetAsync("/hubs/servers");
        Assert.Equal(HttpStatusCode.Forbidden, hubResponse.StatusCode);
    }

    [Fact]
    public async Task RegistrationRateLimitRejectsExcessiveAttemptsFromOneAddress()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                email = $"person{attempt}@test.local",
                displayName = "Test User",
                password = "ValidPassword!234",
                inviteCode = "wrong-invite"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "lastperson@test.local",
            displayName = "Test User",
            password = "ValidPassword!234",
            inviteCode = "wrong-invite"
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task RecentSmartAlarmHistoryRequiresAuthenticationAndHonorsLimit()
    {
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/alarms")).StatusCode);

        using var admin = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await admin.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@test.local",
            password = "AdminPassword!234",
            rememberMe = true
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AlarmEvents.AddRange(
                new AlarmEvent { RustServerId = "server-a", Title = "Older alarm", Message = "Earlier", ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-2) },
                new AlarmEvent { RustServerId = "server-b", Title = "Latest alarm", Message = "Recent", ReceivedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var json = JsonDocument.Parse(await (await admin.GetAsync("/api/alarms?limit=1")).Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetArrayLength());
        Assert.Equal("Latest alarm", json.RootElement[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task ServerTimersAreSharedButOnlyOwnersOrAdminsCanDeleteThem()
    {
        using var admin = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await admin.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@test.local",
            password = "AdminPassword!234",
            rememberMe = true
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        Guid serverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var adminUser = await users.FindByEmailAsync("admin@test.local");
            Assert.NotNull(adminUser);

            var server = new PairedServerCredential
            {
                OwnerUserId = adminUser.Id,
                RustServerId = Guid.NewGuid(),
                Name = "Timer Test Server",
                Host = "127.0.0.1",
                Port = 28082,
                PlayerId = "76561198000000000",
                ProtectedPlayerToken = "",
                ConnectionEnabled = false
            };
            db.PairedServerCredentials.Add(server);
            await db.SaveChangesAsync();
            db.PairedDevices.Add(new PairedDevice
            {
                PairedServerCredentialId = server.Id,
                EntityId = "12345",
                Name = "Test Switch",
                Type = "Switch"
            });
            await db.SaveChangesAsync();
            serverId = server.Id;
        }


        var groupCreate = await admin.PostAsJsonAsync($"/api/servers/{serverId}/device-groups", new { name = "Lights" });
        Assert.Equal(HttpStatusCode.Created, groupCreate.StatusCode);
        using var groupCreatedJson = JsonDocument.Parse(await groupCreate.Content.ReadAsStringAsync());
        var groupId = groupCreatedJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(groupId));

        var assignGroup = await admin.PutAsJsonAsync($"/api/servers/{serverId}/devices/12345/group", new { groupId });
        Assert.Equal(HttpStatusCode.OK, assignGroup.StatusCode);
        using var groupsJson = JsonDocument.Parse(await (await admin.GetAsync($"/api/servers/{serverId}/device-groups")).Content.ReadAsStringAsync());
        Assert.Equal(1, groupsJson.RootElement.GetArrayLength());
        Assert.Equal(1, groupsJson.RootElement[0].GetProperty("deviceCount").GetInt32());

        var create = await admin.PostAsJsonAsync($"/api/servers/{serverId}/timers", new
        {
            name = "Cargo",
            durationSeconds = 60
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var timerId = createdJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(timerId));

        using var member = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var registration = await member.PostAsJsonAsync("/api/auth/register", new
        {
            email = "timer-member@test.local",
            displayName = "Timer Member",
            password = "MemberPassword!234",
            inviteCode = "test-invite-code",
            rememberMe = true
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        using var memberGroupsJson = JsonDocument.Parse(await (await member.GetAsync($"/api/servers/{serverId}/device-groups")).Content.ReadAsStringAsync());
        Assert.Equal(1, memberGroupsJson.RootElement.GetArrayLength());

        using var timersJson = JsonDocument.Parse(await (await member.GetAsync($"/api/servers/{serverId}/timers")).Content.ReadAsStringAsync());
        Assert.Equal(1, timersJson.RootElement.GetArrayLength());
        Assert.False(timersJson.RootElement[0].GetProperty("isOwner").GetBoolean());

        var forbiddenDelete = await member.DeleteAsync($"/api/servers/{serverId}/timers/{timerId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        var ownerDelete = await admin.DeleteAsync($"/api/servers/{serverId}/timers/{timerId}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);

        var deleteGroup = await admin.DeleteAsync($"/api/servers/{serverId}/device-groups/{groupId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteGroup.StatusCode);
        using var devicesJson = JsonDocument.Parse(await (await admin.GetAsync($"/api/servers/{serverId}/devices")).Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, devicesJson.RootElement[0].GetProperty("groupId").ValueKind);
    }

    [Fact]
    public async Task ServerProfilesArePrivateToTheirOwner()
    {
        using var admin = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await admin.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@test.local",
            password = "AdminPassword!234",
            rememberMe = true
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var created = await admin.PostAsJsonAsync("/api/servers", new
        {
            name = "Private Test Server",
            host = "127.0.0.1",
            port = 28082
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var profileId = createdJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(profileId));

        using var member = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var registration = await member.PostAsJsonAsync("/api/auth/register", new
        {
            email = "member@test.local",
            displayName = "Test Member",
            password = "MemberPassword!234",
            inviteCode = "test-invite-code",
            rememberMe = true
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        using var listJson = JsonDocument.Parse(await (await member.GetAsync("/api/servers")).Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, listJson.RootElement.ValueKind);
        Assert.Equal(0, listJson.RootElement.GetArrayLength());

        var deleteAttempt = await member.DeleteAsync($"/api/servers/{profileId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAttempt.StatusCode);
    }
}
