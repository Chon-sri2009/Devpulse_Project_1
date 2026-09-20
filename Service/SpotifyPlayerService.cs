using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed record SpotifyReply(int Status, JsonElement? Data = null, string? Error = null, int RetryAfterSeconds = 0)
{
    public bool Success => Status is >= 200 and < 300;
    public bool NeedsLogin => Status == 401;
}

public sealed record SpotifyBrowserTokenReply(int Status, string? AccessToken = null,
    int ExpiresInSeconds = 0, string? Error = null)
{
    public bool Success => Status is >= 200 and < 300 && !string.IsNullOrEmpty(AccessToken);
}

public sealed class SpotifyPlayerService(IHttpClientFactory clients, SpotifySettings settings,
    SpotifySessionStore sessions, ILogger<SpotifyPlayerService> logger)
{
    private DateTimeOffset retryAt;
    public Task<SpotifyReply> GetPlaybackAsync(ClaimsPrincipal user, CancellationToken ct) =>
        SendAsync(user, HttpMethod.Get, "me/player?additional_types=track,episode", null, ct);
    public Task<SpotifyReply> GetDevicesAsync(ClaimsPrincipal user, CancellationToken ct) =>
        SendAsync(user, HttpMethod.Get, "me/player/devices", null, ct);
    public Task<SpotifyReply> GetSavedAlbumsAsync(ClaimsPrincipal user, int offset, int limit, CancellationToken ct) =>
        SendAsync(user, HttpMethod.Get,
            $"me/albums?limit={Math.Clamp(limit, 1, 50)}&offset={Math.Max(0, offset)}", null, ct);
    public Task<SpotifyReply> GetPlaylistsAsync(ClaimsPrincipal user, int offset, int limit, CancellationToken ct) =>
        SendAsync(user, HttpMethod.Get,
            $"me/playlists?limit={Math.Clamp(limit, 1, 50)}&offset={Math.Max(0, offset)}", null, ct);

    public Task<SpotifyReply> PlayAlbumAsync(ClaimsPrincipal user, string albumUri, string? deviceId, CancellationToken ct)
    {
        if (!SpotifyAlbum.IsSafeUri(albumUri))
            return Task.FromResult(new SpotifyReply(400, Error: "Choose a valid album from your Spotify library."));
        var suffix = string.IsNullOrWhiteSpace(deviceId) ? "" : "?device_id=" + Uri.EscapeDataString(deviceId);
        return SendAsync(user, HttpMethod.Put, "me/player/play" + suffix, new { context_uri = albumUri }, ct);
    }

    public Task<SpotifyReply> PlayPlaylistAsync(ClaimsPrincipal user, string playlistUri, string? deviceId, CancellationToken ct)
    {
        if (!SpotifyPlaylist.IsSafeUri(playlistUri))
            return Task.FromResult(new SpotifyReply(400, Error: "Choose a valid playlist from your Spotify library."));
        var suffix = string.IsNullOrWhiteSpace(deviceId) ? "" : "?device_id=" + Uri.EscapeDataString(deviceId);
        return SendAsync(user, HttpMethod.Put, "me/player/play" + suffix, new { context_uri = playlistUri }, ct);
    }

    public async Task<SpotifyBrowserTokenReply> GetBrowserTokenAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!settings.IsConfigured) return new(503, Error: "Spotify is not configured.");
        try
        {
            return await sessions.UseAsync(user, async (session, token) =>
            {
                if (session is null) return (null, BrowserLoginRequired());
                if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
                {
                    var refresh = await RefreshAsync(session, token);
                    if (refresh.Error is not null)
                        return (refresh.Session, new SpotifyBrowserTokenReply(refresh.Error.Status,
                            Error: refresh.Error.Error));
                    session = refresh.Session!;
                }

                var expiresIn = Math.Max(1, (int)Math.Floor((session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds));
                return (session, new SpotifyBrowserTokenReply(200, session.AccessToken, expiresIn));
            }, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(504, Error: "Spotify took too long to respond. Try again."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Spotify browser token request failed: {ExceptionType}", ex.GetType().Name);
            return new(503, Error: "Spotify is temporarily unavailable. Try again.");
        }
    }

    public Task<SpotifyReply> CommandAsync(ClaimsPrincipal user, string command, string? deviceId,
        int? value, CancellationToken ct)
    {
        var device = string.IsNullOrWhiteSpace(deviceId) ? "" : "device_id=" + Uri.EscapeDataString(deviceId);
        var suffix = device.Length == 0 ? "" : "?" + device;
        return command switch
        {
            "play" or "pause" => SendAsync(user, HttpMethod.Put, "me/player/" + command + suffix, null, ct),
            "next" or "previous" => SendAsync(user, HttpMethod.Post, "me/player/" + command + suffix, null, ct),
            "volume" when value is >= 0 and <= 100 => SendAsync(user, HttpMethod.Put,
                "me/player/volume?volume_percent=" + value + (device.Length > 0 ? "&" + device : ""), null, ct),
            "seek" when value is >= 0 => SendAsync(user, HttpMethod.Put,
                "me/player/seek?position_ms=" + value + (device.Length > 0 ? "&" + device : ""), null, ct),
            "transfer" or "transfer-play" when !string.IsNullOrWhiteSpace(deviceId) =>
                SendAsync(user, HttpMethod.Put, "me/player",
                    new { device_ids = new[] { deviceId }, play = command == "transfer-play" }, ct),
            _ => Task.FromResult(new SpotifyReply(400, Error: "Choose a valid playback action or volume from 0 to 100."))
        };
    }

    private async Task<SpotifyReply> SendAsync(ClaimsPrincipal user, HttpMethod method, string path,
        object? body, CancellationToken ct)
    {
        if (!settings.IsConfigured) return new(503, Error: "Spotify is not configured.");
        if (retryAt > DateTimeOffset.UtcNow)
            return new(429, Error: "Spotify is busy. Please wait before trying again.",
                RetryAfterSeconds: (int)Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds));
        try
        {
            return await sessions.UseAsync<SpotifyReply>(user, async (session, token) =>
            {
                if (session is null) return (null, LoginRequired());
                if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
                {
                    var refresh = await RefreshAsync(session, token);
                    if (refresh.Error is not null) return (refresh.Session, refresh.Error);
                    session = refresh.Session!;
                }
                var reply = await RequestAsync(method, path, body, session.AccessToken, token);
                if (reply.Status == 401)
                {
                    var refresh = await RefreshAsync(session, token);
                    if (refresh.Error is not null) return (refresh.Session, refresh.Error);
                    session = refresh.Session!;
                    reply = await RequestAsync(method, path, body, session.AccessToken, token);
                    if (reply.Status == 401) session = null;
                }
                return (session, reply);
            }, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(504, Error: "Spotify took too long to respond. Try again."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Spotify request failed: {ExceptionType}", ex.GetType().Name);
            return new(503, Error: "Spotify is temporarily unavailable. Try again.");
        }
    }

    private async Task<(SpotifySession? Session, SpotifyReply? Error)> RefreshAsync(SpotifySession session, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.RefreshToken)) return (null, LoginRequired());
        using var client = clients.CreateClient("Spotify");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.ClientId + ":" + settings.ClientSecret)));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken
        });
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            return (null, LoginRequired());
        if (!response.IsSuccessStatusCode) return (session, ErrorResponse(response));
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var access = json.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrEmpty(access)) return (null, LoginRequired());
        var refresh = json.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        return (session with
        {
            AccessToken = access,
            RefreshToken = string.IsNullOrEmpty(refresh) ? session.RefreshToken : refresh,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32())
        }, null);
    }

    private async Task<SpotifyReply> RequestAsync(HttpMethod method, string path, object? body, string accessToken, CancellationToken ct)
    {
        using var client = clients.CreateClient("Spotify");
        using var request = new HttpRequestMessage(method, "https://api.spotify.com/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return ErrorResponse(response);
        // Player commands and an idle player commonly return 204 (no JSON body).
        if (method != HttpMethod.Get || response.StatusCode == HttpStatusCode.NoContent)
            return new((int)response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return new((int)response.StatusCode, json.RootElement.Clone());
    }

    private SpotifyReply ErrorResponse(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var seconds = status == 429
            ? Math.Max(1, (int)Math.Ceiling(response.Headers.RetryAfter?.Delta?.TotalSeconds
                ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 30))
            : 0;
        if (seconds > 0) retryAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        return new(status, Error: status switch
        {
            401 => "Your Spotify session expired. Reconnect to continue.",
            403 => "Spotify denied this action. Check Premium, app access, and device restrictions.",
            404 => "No active device. Open Spotify, start a track, then refresh devices.",
            429 => $"Spotify rate limit reached. Try again in {seconds} seconds.",
            _ => "Spotify could not complete this action. Try again."
        }, RetryAfterSeconds: seconds);
    }
    private static SpotifyReply LoginRequired() => new(401, Error: "Your Spotify session expired. Reconnect to continue.");
    private static SpotifyBrowserTokenReply BrowserLoginRequired() =>
        new(401, Error: "Your Spotify session expired. Reconnect to continue.");
}
