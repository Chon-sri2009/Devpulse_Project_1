using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using MiniProject_Everything_1.Components;
using MiniProject_Everything_1.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var spotifyClientId = builder.Configuration["Spotify:ClientId"]
    ?? throw new InvalidOperationException(
        "Spotify:ClientId is not configured.");

var spotifyClientSecret = builder.Configuration["Spotify:ClientSecret"]
    ?? throw new InvalidOperationException(
        "Spotify:ClientSecret is not configured.");

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme =
            CookieAuthenticationDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme = "Spotify";
    })
    .AddCookie()
    .AddOAuth("Spotify", options =>
    {
        options.ClientId = spotifyClientId;
        options.ClientSecret = spotifyClientSecret;
        options.CallbackPath = "/signin-spotify";

        options.AuthorizationEndpoint =
            "https://accounts.spotify.com/authorize";

        options.TokenEndpoint =
            "https://accounts.spotify.com/api/token";

        options.UserInformationEndpoint =
            "https://api.spotify.com/v1/me";

        options.SaveTokens = true;

        options.Scope.Add("user-read-private");
        options.Scope.Add("user-read-playback-state");
        options.Scope.Add("user-read-currently-playing");
        options.Scope.Add("user-modify-playback-state");

        options.ClaimActions.MapJsonKey(
            ClaimTypes.NameIdentifier, "id");

        options.ClaimActions.MapJsonKey(
            ClaimTypes.Name, "display_name");

        options.Events.OnCreatingTicket = async context =>
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                options.UserInformationEndpoint);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    context.AccessToken);

            using var response = await context.Backchannel.SendAsync(
                request,
                context.HttpContext.RequestAborted);

            response.EnsureSuccessStatusCode();

            using var user = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            context.RunClaimActions(user.RootElement);
        };
    });

builder.Services.AddHttpClient<ApiHealthCheckService>();
builder.Services.AddSingleton<SystemMetricsService>();
builder.Services.AddSingleton<ComputerInformationService>();
builder.Services.AddSingleton<DiskStorageService>();
builder.Services.AddSingleton<TcpPortScannerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/spotify/login", () =>
    Results.Challenge(
        new AuthenticationProperties
        {
            RedirectUri = "/spotify"
        },
        ["Spotify"]));

app.MapGet("/spotify/logout", () =>
    Results.SignOut(
        new AuthenticationProperties
        {
            RedirectUri = "/spotify"
        },
        [CookieAuthenticationDefaults.AuthenticationScheme]));

app.Run();