using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using MiniProject_Everything_1.Components;
using MiniProject_Everything_1.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var externalSecretsFile = builder.Configuration["DEVPULSE_SECRETS_FILE"];
if (!string.IsNullOrWhiteSpace(externalSecretsFile))
{
    var externalSecretsPath = Path.GetFullPath(externalSecretsFile);
    if (!File.Exists(externalSecretsPath))
        throw new InvalidOperationException("The configured DevPulse secrets file could not be found.");

    // This file must be mounted at runtime and kept outside the repository and image.
    builder.Configuration.AddJsonFile(externalSecretsPath, optional: false, reloadOnChange: false);
}
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy =>
    policy.AddAuthenticationSchemes(Program.AdminScheme).RequireAuthenticatedUser()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("admin-login", limiter =>
    { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
    options.AddConcurrencyLimiter("admin-actions", limiter =>
    { limiter.PermitLimit = 2; limiter.QueueLimit = 0; });
    options.AddFixedWindowLimiter("spotify-token", limiter =>
    { limiter.PermitLimit = 30; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
});
var spotify = new SpotifySettings(builder.Configuration["Spotify:ClientId"] ?? "",
    builder.Configuration["Spotify:ClientSecret"] ?? "");
builder.Services.AddSingleton(spotify);
var storage = Path.GetFullPath(builder.Configuration["Storage:DataPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data"));
Directory.CreateDirectory(storage);
builder.Services.AddSingleton(new AppStorage(storage));
var protection = builder.Services.AddDataProtection().SetApplicationName("DevPulse")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storage, "keys")));
if (OperatingSystem.IsWindows() && builder.Configuration.GetValue("Storage:ProtectKeysWithDpapi", false))
    protection.ProtectKeysWithDpapi();
builder.Services.AddSingleton<SpotifySessionStore>();
builder.Services.AddHttpClient("Spotify", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<SpotifyPlayerService>();
var operations = builder.Configuration.GetSection("Operations").Get<OperationsSettings>() ?? new();
var alerts = builder.Configuration.GetSection("Alerts").Get<AlertSettings>() ?? new();
var admin = builder.Configuration.GetSection("Admin").Get<AdminSettings>() ?? new();
builder.Services.AddSingleton(operations);
builder.Services.AddSingleton(alerts);
builder.Services.AddSingleton(admin);
var otlpEndpoint = Uri.TryCreate(builder.Configuration["Telemetry:OtlpEndpoint"], UriKind.Absolute, out var endpoint)
    && endpoint.Scheme is "http" or "https" ? endpoint : null;
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DevPulse"))
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
        if (otlpEndpoint is not null) metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
    })
    .WithTracing(traces =>
    {
        traces.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (otlpEndpoint is not null) traces.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
    });
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/', 2);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var prefixLength))
            throw new InvalidOperationException($"Invalid trusted network '{network}'. Use CIDR notation, for example 10.0.0.0/8.");
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
    }
});
var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "DevPulse.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = false;
        options.LoginPath = "/spotify/login";
        options.Events.OnValidatePrincipal = async context =>
        {
            var store = context.HttpContext.RequestServices.GetRequiredService<SpotifySessionStore>();
            if (!await store.ExistsAsync(context.Principal!, context.HttpContext.RequestAborted))
                context.RejectPrincipal();
        };
    })
    .AddCookie(Program.AdminScheme, options =>
    {
        options.Cookie.Name = "DevPulse.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(4);
        options.LoginPath = "/admin";
    });
if (spotify.IsConfigured)
{
    authentication.AddOAuth("Spotify", options =>
    {
        options.ClientId = spotify.ClientId;
        options.ClientSecret = spotify.ClientSecret;
        options.CallbackPath = "/signin-spotify";
        options.AuthorizationEndpoint = "https://accounts.spotify.com/authorize";
        options.TokenEndpoint = "https://accounts.spotify.com/api/token";
        options.UserInformationEndpoint = "https://api.spotify.com/v1/me";
        options.UsePkce = true;
        options.Scope.Add("user-read-private");
        options.Scope.Add("user-read-email");
        options.Scope.Add("user-library-read");
        options.Scope.Add("user-read-playback-state");
        options.Scope.Add("user-read-currently-playing");
        options.Scope.Add("user-modify-playback-state");
        options.Scope.Add("streaming");
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "display_name");
        options.Events.OnCreatingTicket = async context =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.UserInformationEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
            using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            using var user = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(),
                cancellationToken: context.HttpContext.RequestAborted);
            context.RunClaimActions(user.RootElement);
            var store = context.HttpContext.RequestServices.GetRequiredService<SpotifySessionStore>();
            var id = await store.CreateAsync(context.AccessToken!, context.RefreshToken,
                context.ExpiresIn ?? TimeSpan.FromHours(1), context.HttpContext.RequestAborted);
            context.Identity!.AddClaim(new Claim(SpotifySessionStore.SessionClaim, id));
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("/spotify?error=login");
            return Task.CompletedTask;
        };
    });
}
builder.Services.AddHttpClient<ApiHealthCheckService>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("Diagnostics", client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient("LoadTest", client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient("Alerts", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<SystemMetricsService>();
builder.Services.AddSingleton<ComputerInformationService>();
builder.Services.AddSingleton<DiskStorageService>();
builder.Services.AddSingleton<TcpPortScannerService>();
builder.Services.AddSingleton<FolderScanService>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<AlertDeliveryService>();
builder.Services.AddSingleton<NetworkDiagnosticsService>();
builder.Services.AddSingleton<DiagnosticToolsService>();
builder.Services.AddSingleton<DeploymentInfoService>();
builder.Services.AddSingleton<MaintenanceState>();
builder.Services.AddSingleton<AdminAccessService>();
builder.Services.AddSingleton<AdministrationService>();
builder.Services.AddHostedService<MetricsCollector>();
builder.Services.AddHostedService<WatchlistCollector>();
builder.Services.AddSingleton(new DiagnosticsSettings(
    builder.Configuration.GetValue<bool?>("Diagnostics:Enabled") ?? builder.Environment.IsDevelopment(),
    builder.Configuration["Diagnostics:FolderRoot"]));
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseMiddleware<TelemetryMiddleware>();
app.UseMiddleware<MaintenanceMiddleware>();
app.UseMiddleware<DiagnosticsGuardMiddleware>();
// TLS may terminate at the host proxy; opt in only when Kestrel has an HTTPS endpoint.
if (builder.Configuration.GetValue<bool>("Hosting:RedirectToHttps")) app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/healthz");
app.MapGet("/healthz/details", () => Results.Ok(new
{
    status = "Healthy",
    service = "DevPulse",
    timestamp = DateTimeOffset.UtcNow
}));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/spotify/login", () => spotify.IsConfigured
    ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/spotify" }, ["Spotify"])
    : Results.Redirect("/spotify?error=configuration"));
app.MapGet("/spotify/browser-token", async (HttpContext context, SpotifyPlayerService player) =>
{
    context.Response.Headers.CacheControl = "no-store, private";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Vary = "Cookie";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    var token = await player.GetBrowserTokenAsync(context.User, context.RequestAborted);
    return token.Success
        ? Results.Json(new { accessToken = token.AccessToken, expiresIn = token.ExpiresInSeconds })
        : Results.Json(new { error = token.Error }, statusCode: token.Status);
}).RequireRateLimiting("spotify-token");
app.MapPost("/spotify/logout", async (HttpContext context, IAntiforgery antiforgery, SpotifySessionStore store) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    await store.RemoveAsync(context.User, context.RequestAborted);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/spotify");
});
app.MapPost("/admin/login", async (HttpContext context, IAntiforgery antiforgery,
    AdminAccessService access, TelemetryStore telemetry) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var username = form["username"].ToString();
    if (!access.Validate(username, form["password"]))
    {
        telemetry.Add(new AuditEvent(DateTimeOffset.UtcNow, username, "Admin login", "DevPulse", false));
        return Results.LocalRedirect("/admin?error=invalid");
    }
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, access.Username), new Claim(ClaimTypes.Role, "Administrator")], Program.AdminScheme);
    await context.SignInAsync(Program.AdminScheme, new ClaimsPrincipal(identity));
    telemetry.Add(new AuditEvent(DateTimeOffset.UtcNow, access.Username, "Admin login", "DevPulse", true));
    return Results.LocalRedirect("/admin");
}).RequireRateLimiting("admin-login");
app.MapPost("/admin/logout", async (HttpContext context, IAntiforgery antiforgery, TelemetryStore telemetry) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var actor = (await context.AuthenticateAsync(Program.AdminScheme)).Principal?.Identity?.Name ?? "unknown";
    await context.SignOutAsync(Program.AdminScheme);
    telemetry.Add(new AuditEvent(DateTimeOffset.UtcNow, actor, "Admin logout", "DevPulse", true));
    return Results.LocalRedirect("/admin");
});
app.MapPost("/admin/maintenance", async (HttpContext context, IAntiforgery antiforgery,
    MaintenanceState maintenance, TelemetryStore telemetry) =>
{
    var adminResult = await Program.RequireAdmin(context); if (adminResult is not null) return adminResult;
    try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var enabled = bool.TryParse(form["enabled"], out var value) && value;
    DateTimeOffset? until = DateTimeOffset.TryParse(form["until"], out var parsed) ? parsed : null;
    maintenance.Set(enabled, form["message"], until);
    var actor = (await context.AuthenticateAsync(Program.AdminScheme)).Principal?.Identity?.Name ?? "unknown";
    telemetry.Add(new AuditEvent(DateTimeOffset.UtcNow, actor, enabled ? "Enable maintenance" : "Disable maintenance", "Website", true));
    return Results.LocalRedirect("/admin");
}).RequireRateLimiting("admin-actions");
app.MapPost("/admin/process/terminate", async (HttpContext context, IAntiforgery antiforgery,
    AdministrationService administration, TelemetryStore telemetry) =>
{
    var denied = await Program.RequireAdmin(context); if (denied is not null) return denied;
    try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var processId = int.TryParse(form["processId"], out var id) ? id : 0;
    var success = administration.Terminate(processId);
    var actor = (await context.AuthenticateAsync(Program.AdminScheme)).Principal?.Identity?.Name ?? "unknown";
    telemetry.Add(new AuditEvent(DateTimeOffset.UtcNow, actor, "Terminate process", processId.ToString(), success));
    return Results.LocalRedirect("/admin");
}).RequireRateLimiting("admin-actions");
app.MapGet("/admin/export", async (HttpContext context, AdministrationService administration, TelemetryStore telemetry) =>
{
    var denied = await Program.RequireAdmin(context); if (denied is not null) return denied;
    var actor = (await context.AuthenticateAsync(Program.AdminScheme)).Principal?.Identity?.Name ?? "unknown";
    telemetry.Add(new AuditEvent(DateTimeOffset.UtcNow, actor, "Export diagnostics", "ZIP", true));
    return Results.File(administration.ExportDiagnostics(), "application/zip", $"devpulse-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
});
app.Run();
public partial class Program
{
    public const string AdminScheme = "DevPulse.Admin";
    public static async Task<IResult?> RequireAdmin(HttpContext context) =>
        (await context.AuthenticateAsync(AdminScheme)).Succeeded ? null : Results.Unauthorized();
}
