using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using RustPlusWeb.Data;
using RustPlusWeb.Realtime;

var builder = WebApplication.CreateBuilder(args);

var databasePath = builder.Configuration["DATABASE_PATH"] ?? "/data/rustplus-web.db";
if (databasePath != ":memory:")
{
    var fullPath = Path.GetFullPath(databasePath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    databasePath = fullPath;
}

Directory.CreateDirectory("/data/keys");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/data/keys"));
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
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var dummyUser = new ApplicationUser { UserName = "unknown-user" };
var dummyPasswordHasher = new PasswordHasher<ApplicationUser>();
var dummyPasswordHash = dummyPasswordHasher.HashPassword(dummyUser, "DummyPassword!234");

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

await InitializeDatabaseAsync(app.Services, builder.Configuration);

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "rustplus-web",
    version = "phase-1-foundation"
}));

app.MapPost("/api/auth/register", async (
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
}).AllowAnonymous();

app.MapPost("/api/auth/login", async (
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
}).AllowAnonymous();

app.MapGet("/api/auth/me", async (
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
    AppDbContext db) =>
{
    var userId = userManager.GetUserId(principal);
    var profiles = await db.ServerProfiles
        .AsNoTracking()
        .Where(profile => profile.OwnerUserId == userId)
        .OrderBy(profile => profile.Name)
        .Select(profile => ToServerResponse(profile))
        .ToListAsync();
    return Results.Ok(profiles);
}).RequireAuthorization();

app.MapPost("/api/servers", async (
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
}).RequireAuthorization();

app.MapDelete("/api/servers/{id:guid}", async (
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
}).RequireAuthorization();

app.MapHub<ServerHub>("/hubs/servers").RequireAuthorization();

app.MapFallbackToFile("index.html");
app.Run();

static async Task InitializeDatabaseAsync(IServiceProvider services, IConfiguration configuration)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

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
    new(profile.Id, profile.Name, profile.Host, profile.Port, profile.CreatedAtUtc, "NotConnected");

public sealed record RegisterRequest(
    string Email, string DisplayName, string Password, string InviteCode, bool RememberMe = true);
public sealed record LoginRequest(string Email, string Password, bool RememberMe = true);
public sealed record CreateServerRequest(string Name, string Host, int Port = 28082);
public sealed record UserResponse(string Id, string Email, string DisplayName, IList<string> Roles);
public sealed record ServerResponse(Guid Id, string Name, string Host, int Port, DateTime CreatedAtUtc, string ConnectionStatus);
