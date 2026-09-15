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
using MiniProject_Everything_1.Components;
using MiniProject_Everything_1.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
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
        options.Scope.Add("user-read-playback-state");
        options.Scope.Add("user-read-currently-playing");
        options.Scope.Add("user-modify-playback-state");
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
builder.Services.AddSingleton<SystemMetricsService>();
builder.Services.AddSingleton<ComputerInformationService>();
builder.Services.AddSingleton<DiskStorageService>();
builder.Services.AddSingleton<TcpPortScannerService>();
builder.Services.AddSingleton<FolderScanService>();
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
// TLS may terminate at the host proxy; opt in only when Kestrel has an HTTPS endpoint.
if (builder.Configuration.GetValue<bool>("Hosting:RedirectToHttps")) app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/healthz");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/spotify/login", () => spotify.IsConfigured
    ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/spotify" }, ["Spotify"])
    : Results.Redirect("/spotify?error=configuration"));
app.MapPost("/spotify/logout", async (HttpContext context, IAntiforgery antiforgery, SpotifySessionStore store) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    await store.RemoveAsync(context.User, context.RequestAborted);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/spotify");
});
app.Run();
public partial class Program;
