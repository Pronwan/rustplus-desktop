using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using RustPlusWeb.Data;
using RustPlusWeb.Realtime;
using RustPlusWeb.Services;

var builder = WebApplication.CreateBuilder(args);

var databasePath = builder.Configuration["DATABASE_PATH"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "rustplus-web.db");
if (databasePath != ":memory:")
{
    var fullPath = Path.GetFullPath(databasePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    databasePath = fullPath;
}

var keysPath = builder.Configuration["DATA_PROTECTION_KEYS_PATH"] ?? Path.Combine(Path.GetDirectoryName(databasePath)!, "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "RustPlusWeb.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("fcm-configuration", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<RustPlusConnectionManager>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RustPlusConnectionManager>());
builder.Services.AddSingleton<RustPlusFcmService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RustPlusFcmService>());
builder.Services.AddHostedService<ServerTimerService>();
builder.Services.AddProblemDetails();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var dummyUser = new ApplicationUser { UserName = "unknown-user" };
var dummyPasswordHasher = new PasswordHasher<ApplicationUser>();
var dummyPasswordHash = dummyPasswordHasher.HashPassword(dummyUser, "DummyPassword!234");

var app = builder.Build();

// Behind a reverse proxy every request reaches the app from the proxy's address, which would put
// all users in one login rate-limit bucket. The proxy passes the real client address in
// X-Forwarded-For; it is only believed when the direct peer is listed in WEB_TRUSTED_PROXIES
// (IP addresses or CIDR ranges), because any client can send that header itself. This runs before
// everything else so the rate limiter sees the resolved address.
var trustedProxies = TrustedProxyList.Parse(app.Configuration["WEB_TRUSTED_PROXIES"]);
if (trustedProxies.IsEmpty)
{
    app.Logger.LogWarning(
        "WEB_TRUSTED_PROXIES is not set. Rate limits use the address of the direct connection, so behind a " +
        "reverse proxy all users share one limit. Set it to the proxy's address or network, for example 172.18.0.0/16.");
}
else
{
    app.Use(async (context, next) =>
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"];
        if (forwardedFor.Count > 0)
        {
            context.Connection.RemoteIpAddress = trustedProxies.ResolveClient(
                context.Connection.RemoteIpAddress, forwardedFor.ToString());
        }

        await next();
    });
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isApiRequest = path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs");
    var isStateChanging = HttpMethods.IsPost(context.Request.Method) ||
        HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsPatch(context.Request.Method) ||
        HttpMethods.IsDelete(context.Request.Method);

    var shouldCheckOrigin = path.StartsWithSegments("/hubs") ||
        (path.StartsWithSegments("/api") && isStateChanging);
    if (shouldCheckOrigin &&
        context.Request.Headers.TryGetValue("Origin", out var originHeader))
    {
        if (!Uri.TryCreate(originHeader.ToString(), UriKind.Absolute, out var origin) ||
            !string.Equals(origin.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws: wss:; font-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    if (isApiRequest)
        context.Response.Headers["Cache-Control"] = "no-store";

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

await InitializeDatabaseAsync(app.Services, builder.Configuration);

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "rustplus-web",
    version = "web-port"
}));

// Readiness includes the persistent store, unlike liveness. Do not expose exception
// details or connection strings through this unauthenticated operational endpoint.
app.MapGet("/api/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return Results.Problem("The service is not ready.", statusCode: StatusCodes.Status503ServiceUnavailable);

        await db.Database.ExecuteSqlRawAsync("SELECT 1;", cancellationToken);
        return Results.Ok(new { status = "ready", service = "rustplus-web" });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception)
    {
        return Results.Problem("The service is not ready.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/auth/register", async Task<IResult> (
    RegisterRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IConfiguration configuration) =>
{
    var configuredInvite = configuration["WEB_INVITE_CODE"];
    if (string.IsNullOrWhiteSpace(configuredInvite))
        return Results.Problem("Registration is disabled until WEB_INVITE_CODE is configured.", statusCode: 503);

    if (string.IsNullOrWhiteSpace(request.InviteCode) ||
        !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configuredInvite),
            Encoding.UTF8.GetBytes(request.InviteCode)))
        return Results.Unauthorized();

    var email = request.Email.Trim();
    var displayName = request.DisplayName.Trim();
    if (!new EmailAddressAttribute().IsValid(email) || email.Length > 254 ||
        displayName.Length is < 1 or > 80 || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Enter a valid email, display name, and password." });

    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        DisplayName = displayName,
        EmailConfirmed = false
    };

    var created = await userManager.CreateAsync(user, request.Password);
    if (!created.Succeeded)
        return Results.BadRequest(new { errors = created.Errors.Select(error => error.Description) });

    var roleResult = await userManager.AddToRoleAsync(user, "Member");
    if (!roleResult.Succeeded)
    {
        await userManager.DeleteAsync(user);
        return Results.Problem("Could not assign the account role.", statusCode: 500);
    }

    await signInManager.SignInAsync(user, isPersistent: request.RememberMe);
    return Results.Ok(await ToUserResponseAsync(user, userManager));
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async Task<IResult> (
    LoginRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    var email = request.Email.Trim();
    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        _ = dummyPasswordHasher.VerifyHashedPassword(dummyUser, dummyPasswordHash, request.Password);
        return Results.Unauthorized();
    }

    var result = await signInManager.PasswordSignInAsync(
        user.UserName!, request.Password, request.RememberMe, lockoutOnFailure: true);
    if (!result.Succeeded)
        return Results.Unauthorized();

    return Results.Ok(await ToUserResponseAsync(user, userManager));
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapGet("/api/alarms", async Task<IResult> (
    int? limit,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(limit ?? 50, 1, 100);
    var alarms = await db.AlarmEvents.AsNoTracking()
        .OrderByDescending(alarm => alarm.ReceivedAtUtc)
        .ThenByDescending(alarm => alarm.Id)
        .Take(take)
        .Select(alarm => new
        {
            serverId = alarm.RustServerId,
            alarm.Title,
            alarm.Message,
            receivedAtUtc = alarm.ReceivedAtUtc
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(alarms);
}).RequireAuthorization();

app.MapGet("/api/auth/me", async Task<IResult> (
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.GetUserAsync(principal);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await ToUserResponseAsync(user, userManager));
}).RequireAuthorization();

app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/servers", async (
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    RustPlusConnectionManager connections) =>
{
    var userId = userManager.GetUserId(principal);
    var savedProfiles = await db.ServerProfiles
        .AsNoTracking()
        .Where(profile => profile.OwnerUserId == userId)
        .OrderBy(profile => profile.Name)
        .ToListAsync();
    var pairedProfiles = await db.PairedServerCredentials
        .AsNoTracking()
        .OrderBy(profile => profile.Name)
        .ToListAsync();

    var result = savedProfiles.Select(ToServerResponse)
        .Concat(pairedProfiles.Select(profile => ToPairedServerResponse(profile, profile.ConnectionEnabled ? connections.GetStatus(profile.Id) : "Disabled")))
        .OrderBy(profile => profile.Name)
        .ToList();
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/servers/{id:guid}/snapshot", async Task<IResult> (
    Guid id,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    RustPlusConnectionManager connections,
    CancellationToken cancellationToken) =>
{
    var paired = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (paired)
    {
        var snapshot = await connections.GetSnapshotAsync(id, cancellationToken);
        return snapshot is null
            ? Results.Problem("The Rust+ connection is not ready yet.", statusCode: 503)
            : Results.Ok(snapshot);
    }

    var userId = userManager.GetUserId(principal);
    var saved = await db.ServerProfiles.AsNoTracking()
        .AnyAsync(profile => profile.Id == id && profile.OwnerUserId == userId, cancellationToken);
    return saved
        ? Results.Conflict(new { error = "This saved profile has not been paired with Rust+ yet." })
        : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/api/servers/{id:guid}/timers", async Task<IResult> (
    Guid id,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var userId = userManager.GetUserId(principal);
    var timers = await db.ServerTimers.AsNoTracking()
        .Where(timer => timer.PairedServerCredentialId == id)
        .OrderBy(timer => timer.ExpiresAtUtc)
        .ToListAsync(cancellationToken);
    return Results.Ok(timers.Select(timer => ToTimerResponse(timer, timer.OwnerUserId == userId)).ToList());
}).RequireAuthorization();

app.MapPost("/api/servers/{id:guid}/timers", async Task<IResult> (
    Guid id,
    CreateServerTimerRequest request,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IHubContext<ServerHub> hub,
    CancellationToken cancellationToken) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
        return Results.BadRequest(new { error = "Timer names must contain 1 to 80 characters." });
    if (request.DurationSeconds is < 1 or > 86400)
        return Results.BadRequest(new { error = "Timer duration must be between 1 second and 24 hours." });

    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var ownerUserId = userManager.GetUserId(principal);
    if (string.IsNullOrWhiteSpace(ownerUserId))
        return Results.Unauthorized();

    var timer = new ServerTimer
    {
        PairedServerCredentialId = id,
        OwnerUserId = ownerUserId,
        Name = name,
        CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddSeconds(request.DurationSeconds)
    };
    db.ServerTimers.Add(timer);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.All.SendAsync("TimersChanged", new { serverId = id }, cancellationToken);
    return Results.Created($"/api/servers/{id}/timers", ToTimerResponse(timer, true));
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapDelete("/api/servers/{id:guid}/timers/{timerId:guid}", async Task<IResult> (
    Guid id,
    Guid timerId,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IHubContext<ServerHub> hub,
    CancellationToken cancellationToken) =>
{
    var timer = await db.ServerTimers.SingleOrDefaultAsync(item =>
        item.Id == timerId && item.PairedServerCredentialId == id, cancellationToken);
    if (timer is null)
        return Results.NotFound();

    var userId = userManager.GetUserId(principal);
    if (timer.OwnerUserId != userId && !principal.IsInRole("Admin"))
        return Results.Forbid();

    db.ServerTimers.Remove(timer);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.All.SendAsync("TimersChanged", new { serverId = id }, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapGet("/api/servers/{id:guid}/chat", async Task<IResult> (
    Guid id,
    AppDbContext db,
    RustPlusConnectionManager connections,
    CancellationToken cancellationToken) =>
{
    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var messages = await connections.GetTeamChatAsync(id, cancellationToken);
    return messages is null
        ? Results.Problem("The Rust+ connection is not ready or team chat is unavailable.", statusCode: 503)
        : Results.Ok(messages);
}).RequireAuthorization();

app.MapPost("/api/servers/{id:guid}/chat", async Task<IResult> (
    Guid id,
    SendTeamMessageRequest request,
    AppDbContext db,
    RustPlusConnectionManager connections,
    CancellationToken cancellationToken) =>
{
    var message = request.Message?.Trim();
    if (string.IsNullOrWhiteSpace(message) || message.Length > 200)
        return Results.BadRequest(new { error = "Team chat messages must contain 1 to 200 characters." });

    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var result = await connections.SendTeamMessageAsync(id, message, cancellationToken);
    if (result is null)
        return Results.Problem("The Rust+ connection is not ready yet.", statusCode: 503);
    if (!result.Success)
        return Results.Problem(result.Error ?? "The team chat message could not be sent.", statusCode: 502);

    return Results.Ok(new { success = true });
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapGet("/api/servers/{id:guid}/device-groups", async Task<IResult> (
    Guid id,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var groups = await db.DeviceGroups.AsNoTracking()
        .Where(group => group.PairedServerCredentialId == id)
        .OrderBy(group => group.Name)
        .Select(group => new
        {
            group.Id,
            group.Name,
            group.CreatedAtUtc,
            DeviceCount = db.PairedDevices.Count(device => device.GroupId == group.Id)
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(groups);
}).RequireAuthorization();

app.MapPost("/api/servers/{id:guid}/device-groups", async Task<IResult> (
    Guid id,
    CreateDeviceGroupRequest request,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IHubContext<ServerHub> hub,
    CancellationToken cancellationToken) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
        return Results.BadRequest(new { error = "Group names must contain 1 to 50 characters." });

    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var existingNames = await db.DeviceGroups.AsNoTracking()
        .Where(group => group.PairedServerCredentialId == id)
        .Select(group => group.Name)
        .ToListAsync(cancellationToken);
    if (existingNames.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { error = "A device group with that name already exists." });

    var userId = userManager.GetUserId(principal);
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    var group = new DeviceGroup
    {
        PairedServerCredentialId = id,
        Name = name,
        CreatedByUserId = userId,
        CreatedAtUtc = DateTime.UtcNow
    };
    db.DeviceGroups.Add(group);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.All.SendAsync("DevicesChanged", new { serverId = id }, cancellationToken);
    return Results.Created($"/api/servers/{id}/device-groups", new
    {
        group.Id, group.Name, group.CreatedAtUtc, deviceCount = 0
    });
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapDelete("/api/servers/{id:guid}/device-groups/{groupId:guid}", async Task<IResult> (
    Guid id,
    Guid groupId,
    AppDbContext db,
    IHubContext<ServerHub> hub,
    CancellationToken cancellationToken) =>
{
    var group = await db.DeviceGroups.SingleOrDefaultAsync(item =>
        item.Id == groupId && item.PairedServerCredentialId == id, cancellationToken);
    if (group is null)
        return Results.NotFound();

    var devices = await db.PairedDevices
        .Where(device => device.PairedServerCredentialId == id && device.GroupId == groupId)
        .ToListAsync(cancellationToken);
    foreach (var device in devices)
        device.GroupId = null;
    db.DeviceGroups.Remove(group);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.All.SendAsync("DevicesChanged", new { serverId = id }, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapPut("/api/servers/{id:guid}/devices/{entityId}/group", async Task<IResult> (
    Guid id,
    string entityId,
    SetDeviceGroupRequest request,
    AppDbContext db,
    IHubContext<ServerHub> hub,
    CancellationToken cancellationToken) =>
{
    var device = await db.PairedDevices.SingleOrDefaultAsync(item =>
        item.PairedServerCredentialId == id && item.EntityId == entityId, cancellationToken);
    if (device is null)
        return Results.NotFound();

    if (request.GroupId.HasValue)
    {
        var groupExists = await db.DeviceGroups.AsNoTracking().AnyAsync(group =>
            group.Id == request.GroupId.Value && group.PairedServerCredentialId == id, cancellationToken);
        if (!groupExists)
            return Results.BadRequest(new { error = "The group does not belong to this server." });
    }

    device.GroupId = request.GroupId;
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.All.SendAsync("DevicesChanged", new { serverId = id }, cancellationToken);
    return Results.Ok(new { entityId, groupId = device.GroupId });
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapGet("/api/servers/{id:guid}/devices", async Task<IResult> (
    Guid id,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var exists = await db.PairedServerCredentials.AsNoTracking()
        .AnyAsync(profile => profile.Id == id, cancellationToken);
    if (!exists)
        return Results.NotFound();

    var devices = await db.PairedDevices.AsNoTracking()
        .Where(device => device.PairedServerCredentialId == id)
        .OrderBy(device => device.Name)
        .ToListAsync(cancellationToken);
    return Results.Ok(devices.Select(device => new
    {
        device.Id,
        device.EntityId,
        device.Name,
        device.Type,
        device.GroupId,
        device.CreatedAtUtc
    }).ToList());
}).RequireAuthorization();

app.MapGet("/api/servers/{id:guid}/devices/{entityId}/info", async Task<IResult> (
    Guid id,
    string entityId,
    AppDbContext db,
    RustPlusConnectionManager connections,
    CancellationToken cancellationToken) =>
{
    if (!ulong.TryParse(entityId, out var numericEntityId))
        return Results.BadRequest(new { error = "Invalid device ID." });

    var device = await db.PairedDevices.AsNoTracking().SingleOrDefaultAsync(item =>
        item.PairedServerCredentialId == id && item.EntityId == entityId, cancellationToken);
    if (device is null)
        return Results.NotFound();
    if (device.Type is not ("Switch" or "Alarm" or "StorageMonitor"))
        return Results.BadRequest(new { error = "This device type does not expose details through the Rust+ API." });

    var info = await connections.GetDeviceInfoAsync(id, numericEntityId, device.Type, cancellationToken);
    return info is null
        ? Results.Problem("The Rust+ connection is not ready or device details are unavailable.", statusCode: 503)
        : Results.Ok(new { entityId, device.Type, info });
}).RequireAuthorization();

app.MapPost("/api/servers/{id:guid}/devices/{entityId}/toggle", async Task<IResult> (
    Guid id,
    string entityId,
    SetDeviceStateRequest request,
    AppDbContext db,
    RustPlusConnectionManager connections,
    CancellationToken cancellationToken) =>
{
    if (!ulong.TryParse(entityId, out var numericEntityId))
        return Results.BadRequest(new { error = "Invalid device ID." });

    var device = await db.PairedDevices.AsNoTracking().SingleOrDefaultAsync(item =>
        item.PairedServerCredentialId == id && item.EntityId == entityId, cancellationToken);
    if (device is null)
        return Results.NotFound();
    if (!string.Equals(device.Type, "Switch", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Only smart switches can be controlled from this endpoint." });

    var result = await connections.SetSwitchAsync(id, numericEntityId, request.IsActive, cancellationToken);
    if (result is null)
        return Results.Problem("The Rust+ connection is not ready yet.", statusCode: 503);
    if (!result.Success)
        return Results.Problem(result.Error ?? "The switch command failed.", statusCode: 502);

    
    return Results.Ok(new { success = true, isActive = result.IsActive });
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapGet("/api/admin/fcm-status", (RustPlusFcmService fcm) =>
    Results.Ok(new { configured = fcm.IsConfigured, listening = fcm.IsListening }))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/api/admin/fcm-credentials", async Task<IResult> (
    UploadFcmCredentialsRequest request,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    RustPlusFcmService fcm,
    CancellationToken cancellationToken) =>
{
    var ownerUserId = userManager.GetUserId(principal);
    if (string.IsNullOrWhiteSpace(ownerUserId))
        return Results.Unauthorized();

    try
    {
        await fcm.ConfigureAsync(request.CredentialsJson, ownerUserId, cancellationToken);
        return Results.Ok(new { configured = fcm.IsConfigured, listening = fcm.IsListening });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (Exception)
    {
        return Results.Problem("Credentials were saved, but the FCM listener could not connect. Check the file and server network, then retry.", statusCode: 503);
    }
}).RequireAuthorization(policy => policy.RequireRole("Admin")).RequireRateLimiting("fcm-configuration");

app.MapPost("/api/paired-servers/{id:guid}/connection", async Task<IResult> (
    Guid id,
    SetConnectionRequest request,
    AppDbContext db,
    RustPlusConnectionManager connections,
    CancellationToken cancellationToken) =>
{
    var profile = await db.PairedServerCredentials.SingleOrDefaultAsync(
        item => item.Id == id, cancellationToken);
    if (profile is null)
        return Results.NotFound();

    profile.ConnectionEnabled = request.Enabled;
    await db.SaveChangesAsync(cancellationToken);
    await connections.SetConnectionEnabledAsync(id, request.Enabled, cancellationToken);
    return Results.Ok(new { id, connectionStatus = connections.GetStatus(id) });
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapDelete("/api/paired-servers/{id:guid}", async Task<IResult> (
    Guid id,
    AppDbContext db,
    RustPlusConnectionManager connections,
    IHubContext<ServerHub> hub,
    CancellationToken cancellationToken) =>
{
    var profile = await db.PairedServerCredentials.SingleOrDefaultAsync(
        item => item.Id == id, cancellationToken);
    if (profile is null)
        return Results.NotFound();

    await connections.DisconnectAsync(id, cancellationToken);
    var devices = await db.PairedDevices
        .Where(device => device.PairedServerCredentialId == id)
        .ToListAsync(cancellationToken);
    db.PairedDevices.RemoveRange(devices);
    var timers = await db.ServerTimers
        .Where(timer => timer.PairedServerCredentialId == id)
        .ToListAsync(cancellationToken);
    db.ServerTimers.RemoveRange(timers);
    var groups = await db.DeviceGroups
        .Where(group => group.PairedServerCredentialId == id)
        .ToListAsync(cancellationToken);
    db.DeviceGroups.RemoveRange(groups);
    db.PairedServerCredentials.Remove(profile);
    await db.SaveChangesAsync(cancellationToken);
    await hub.Clients.All.SendAsync("ServersChanged", cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapGet("/api/admin/users", async (UserManager<ApplicationUser> userManager) =>
{
    var accounts = await userManager.Users.OrderBy(user => user.Email).ToListAsync();
    var result = new List<object>();
    foreach (var account in accounts)
    {
        result.Add(new
        {
            account.Id,
            Email = account.Email ?? "",
            account.DisplayName,
            Roles = await userManager.GetRolesAsync(account)
        });
    }
    return Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPut("/api/admin/users/{id}/role", async Task<IResult> (
    string id,
    SetUserRoleRequest request,
    UserManager<ApplicationUser> userManager) =>
{
    var role = request.Role.Trim();
    if (role is not ("Admin" or "Member" or "ViewOnly"))
        return Results.BadRequest(new { error = "Role must be Admin, Member, or ViewOnly." });

    var target = await userManager.FindByIdAsync(id);
    if (target is null)
        return Results.NotFound();

    var currentRoles = await userManager.GetRolesAsync(target);
    if (currentRoles.Contains("Admin") && role != "Admin")
    {
        var administrators = await userManager.GetUsersInRoleAsync("Admin");
        if (administrators.Count <= 1)
            return Results.Conflict(new { error = "The last administrator cannot be demoted." });
    }

    var removed = await userManager.RemoveFromRolesAsync(target, currentRoles);
    if (!removed.Succeeded)
        return Results.Problem("Could not update the account role.", statusCode: 500);

    var added = await userManager.AddToRoleAsync(target, role);
    if (!added.Succeeded)
        return Results.Problem("Could not assign the new account role.", statusCode: 500);

    return Results.Ok(await ToUserResponseAsync(target, userManager));
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPost("/api/servers", async Task<IResult> (
    CreateServerRequest request,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IHubContext<ServerHub> hub) =>
{
    var name = request.Name.Trim();
    var host = request.Host.Trim();
    if (name.Length is < 1 or > 80 || host.Length is < 1 or > 255 ||
        request.Port is < 1 or > 65535 ||
        host.Contains("://", StringComparison.Ordinal) ||
        host.Any(char.IsWhiteSpace))
        return Results.BadRequest(new { error = "Provide a name, hostname/IP without a scheme, and a valid port." });

    var userId = userManager.GetUserId(principal);
    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    var duplicate = await db.ServerProfiles.AnyAsync(profile =>
        profile.OwnerUserId == userId && profile.Name.ToLower() == name.ToLower());
    if (duplicate)
        return Results.Conflict(new { error = "You already have a server profile with that name." });

    var profile = new ServerProfile
    {
        OwnerUserId = userId,
        Name = name,
        Host = host,
        Port = request.Port
    };
    db.ServerProfiles.Add(profile);
    await db.SaveChangesAsync();
    await hub.Clients.Group($"user:{userId}").SendAsync("ServersChanged");
    return Results.Created($"/api/servers/{profile.Id}", ToServerResponse(profile));
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapDelete("/api/servers/{id:guid}", async Task<IResult> (
    Guid id,
    System.Security.Claims.ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IHubContext<ServerHub> hub) =>
{
    var userId = userManager.GetUserId(principal);
    var profile = await db.ServerProfiles.SingleOrDefaultAsync(item =>
        item.Id == id && item.OwnerUserId == userId);
    if (profile is null)
        return Results.NotFound();

    db.ServerProfiles.Remove(profile);
    await db.SaveChangesAsync();
    await hub.Clients.Group($"user:{userId}").SendAsync("ServersChanged");
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin", "Member"));

app.MapHub<ServerHub>("/hubs/servers").RequireAuthorization();

app.MapFallbackToFile("index.html");
app.Run();

static async Task EnsureColumnAsync(AppDbContext db, string tableName, string columnName, string columnDefinition)
{
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    try
    {
        var exists = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinition};";
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        await connection.CloseAsync();
    }
}

static async Task InitializeDatabaseAsync(IServiceProvider services, IConfiguration configuration)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "PairedServerCredentials" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_PairedServerCredentials" PRIMARY KEY,
            "OwnerUserId" TEXT NOT NULL,
            "RustServerId" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Host" TEXT NOT NULL,
            "Port" INTEGER NOT NULL,
            "PlayerId" TEXT NOT NULL,
            "ProtectedPlayerToken" TEXT NOT NULL,
            "ConnectionEnabled" INTEGER NOT NULL DEFAULT 1,
            "CreatedAtUtc" TEXT NOT NULL
        );
        """);
    await EnsureColumnAsync(db, "PairedServerCredentials", "ConnectionEnabled", "INTEGER NOT NULL DEFAULT 1");
    await db.Database.ExecuteSqlRawAsync("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PairedServerCredentials_RustServerId"
        ON "PairedServerCredentials" ("RustServerId");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "PairedDevices" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_PairedDevices" PRIMARY KEY,
            "PairedServerCredentialId" TEXT NOT NULL,
            "EntityId" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Type" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL
        );
        """);
    await EnsureColumnAsync(db, "PairedDevices", "GroupId", "TEXT NULL");
    await db.Database.ExecuteSqlRawAsync("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PairedDevices_PairedServerCredentialId_EntityId"
        ON "PairedDevices" ("PairedServerCredentialId", "EntityId");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "ServerTimers" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_ServerTimers" PRIMARY KEY,
            "PairedServerCredentialId" TEXT NOT NULL,
            "OwnerUserId" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "ExpiresAtUtc" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "Fired" INTEGER NOT NULL
        );
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS "IX_ServerTimers_PairedServerCredentialId_ExpiresAtUtc"
        ON "ServerTimers" ("PairedServerCredentialId", "ExpiresAtUtc");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "DeviceGroups" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_DeviceGroups" PRIMARY KEY,
            "PairedServerCredentialId" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "CreatedByUserId" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL
        );
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeviceGroups_PairedServerCredentialId_Name"
        ON "DeviceGroups" ("PairedServerCredentialId", "Name");
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "AlarmEvents" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_AlarmEvents" PRIMARY KEY,
            "RustServerId" TEXT NOT NULL,
            "Title" TEXT NOT NULL,
            "Message" TEXT NOT NULL,
            "ReceivedAtUtc" TEXT NOT NULL
        );
        """);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS "IX_AlarmEvents_ReceivedAtUtc"
        ON "AlarmEvents" ("ReceivedAtUtc");
        """);

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Member", "ViewOnly" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
                throw new InvalidOperationException($"Unable to create required role '{role}'.");
        }
    }

    var adminEmail = configuration["ADMIN_EMAIL"];
    var adminPassword = configuration["ADMIN_PASSWORD"];
    if (string.IsNullOrWhiteSpace(adminEmail) != string.IsNullOrWhiteSpace(adminPassword))
        throw new InvalidOperationException("Set both ADMIN_EMAIL and ADMIN_PASSWORD to bootstrap an administrator.");

    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = adminEmail.Trim();
        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = "Administrator",
                EmailConfirmed = true
            };
            var created = await userManager.CreateAsync(admin, adminPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException("Administrator bootstrap failed: " +
                    string.Join("; ", created.Errors.Select(error => error.Description)));
        }

        if (!await userManager.IsInRoleAsync(admin, "Admin"))
        {
            var assigned = await userManager.AddToRoleAsync(admin, "Admin");
            if (!assigned.Succeeded)
                throw new InvalidOperationException("Unable to assign the administrator role.");
        }
    }
}

static async Task<UserResponse> ToUserResponseAsync(
    ApplicationUser user, UserManager<ApplicationUser> userManager) =>
    new(user.Id, user.Email ?? "", user.DisplayName,
        await userManager.GetRolesAsync(user));

static ServerResponse ToServerResponse(ServerProfile profile) =>
    new(profile.Id, profile.Name, profile.Host, profile.Port, profile.CreatedAtUtc, "NotConnected", false, false);

static ServerResponse ToPairedServerResponse(PairedServerCredential profile, string status) =>
    new(profile.Id, profile.Name, profile.Host, profile.Port, profile.CreatedAtUtc, status, true, profile.ConnectionEnabled);

static ServerTimerResponse ToTimerResponse(ServerTimer timer, bool isOwner) =>
    new(timer.Id, timer.Name, timer.ExpiresAtUtc, timer.CreatedAtUtc, timer.Fired, isOwner);

public sealed record RegisterRequest(
    string Email, string DisplayName, string Password, string InviteCode, bool RememberMe = true);
public sealed record LoginRequest(string Email, string Password, bool RememberMe = true);
public sealed record CreateServerRequest(string Name, string Host, int Port = 28082);
public sealed record SetUserRoleRequest(string Role);
public sealed record UploadFcmCredentialsRequest(string CredentialsJson);
public sealed record SetDeviceStateRequest(bool? IsActive = null);
public sealed record SetConnectionRequest(bool Enabled);
public sealed record SendTeamMessageRequest(string Message);
public sealed record CreateDeviceGroupRequest(string Name);
public sealed record SetDeviceGroupRequest(Guid? GroupId);
public sealed record CreateServerTimerRequest(string Name, int DurationSeconds);
public sealed record ServerTimerResponse(Guid Id, string Name, DateTime ExpiresAtUtc, DateTime CreatedAtUtc, bool Fired, bool IsOwner);
public sealed record UserResponse(string Id, string Email, string DisplayName, IList<string> Roles);
public sealed record ServerResponse(Guid Id, string Name, string Host, int Port, DateTime CreatedAtUtc, string ConnectionStatus, bool IsPaired, bool ConnectionEnabled);
public partial class Program { }
