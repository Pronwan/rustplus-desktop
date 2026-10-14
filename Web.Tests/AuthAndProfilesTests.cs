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
            serverId = server.Id;
        }

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

        using var timersJson = JsonDocument.Parse(await (await member.GetAsync($"/api/servers/{serverId}/timers")).Content.ReadAsStringAsync());
        Assert.Equal(1, timersJson.RootElement.GetArrayLength());
        Assert.False(timersJson.RootElement[0].GetProperty("isOwner").GetBoolean());

        var forbiddenDelete = await member.DeleteAsync($"/api/servers/{serverId}/timers/{timerId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        var ownerDelete = await admin.DeleteAsync($"/api/servers/{serverId}/timers/{timerId}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
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
