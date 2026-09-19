using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniProject_Everything_1.Services;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("GitHub 200 is healthy", async () =>
    {
        using var client = new HttpClient(new Stub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        Check((await new ApiHealthCheckService(client).CheckAsync()).IsReachable);
    }),
    ("GitHub 503 is not healthy", async () =>
    {
        using var client = new HttpClient(new Stub((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var result = await new ApiHealthCheckService(client).CheckAsync();
        Check(!result.IsReachable && result.StatusCode == 503 && result.ErrorMessage is not null);
    }),
    ("GitHub timeout has useful feedback", async () =>
    {
        using var client = new HttpClient(new Stub((_, _) => throw new TaskCanceledException()));
        Check((await new ApiHealthCheckService(client).CheckAsync()).Status == "Timeout");
    }),
    ("Caller cancellation propagates", async () =>
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        using var client = new HttpClient(new Stub((_, token) => { token.ThrowIfCancellationRequested(); throw new Exception(); }));
        await Cancelled(() => new ApiHealthCheckService(client).CheckAsync(ct.Token));
    }),
    ("Folder totals include nested files", async () =>
    {
        using var temp = new Temp();
        Directory.CreateDirectory(Path.Combine(temp.Path, "large", "nested"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "root.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "large", "nested", "file.bin"), new byte[100]);
        var result = await new FolderScanService(new(true, temp.Path)).ScanAsync(default);
        Check(result.Error is null && result.Bytes == 110 && result.Files == 2 && !result.IsPartial);
        Check(result.Folders[0].Name == "large" && result.Folders[0].Bytes == 100);
    }),
    ("Disabled folder scans do not read files", async () =>
    {
        var result = await new FolderScanService(new(false, "/unavailable")).ScanAsync(default);
        Check(result.Error is not null && result.Files == 0);
    }),
    ("Missing scan root returns error", async () =>
    {
        using var temp = new Temp();
        var result = await new FolderScanService(new(true, Path.Combine(temp.Path, "missing"))).ScanAsync(default);
        Check(result.Error is not null && result.Files == 0);
    }),
    ("Folder scans support cancellation", async () =>
    {
        using var temp = new Temp(); using var ct = new CancellationTokenSource(); ct.Cancel();
        await Cancelled(() => new FolderScanService(new(true, temp.Path)).ScanAsync(ct.Token));
    }),
    ("Live metrics are plausible", () =>
    {
        var result = new SystemMetricsService().GetLiveMetrics();
        Check(result.WorkingMemoryMb > 0 && result.ActiveThreads > 0);
        return Task.CompletedTask;
    }),
    ("Disk percentages are finite and bounded", () =>
    {
        var drives = new DiskStorageService(NullLogger<DiskStorageService>.Instance).GetReadyDrives();
        Check(drives.All(d => double.IsFinite(d.UsedPercentage) && d.UsedPercentage is >= 0 and <= 100));
        return Task.CompletedTask;
    }),
    ("Open TCP port is detected", async () =>
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Check((await TcpPortScannerService.ScanPortAsync(((IPEndPoint)listener.LocalEndpoint).Port, "test")).IsOpen);
    }),
    ("Closed TCP port is detected", async () =>
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        Check(!(await TcpPortScannerService.ScanPortAsync(port, "test")).IsOpen);
    }),
    ("Spotify unconfigured is available as a disabled feature", async () =>
    {
        using var fixture = new SpotifyFixture(new("", ""));
        Check((await fixture.Player.GetPlaybackAsync(new(), default)).Status == 503 && fixture.Requests.Count == 0);
    }),
    ("Spotify anonymous session never calls API", async () =>
    {
        using var fixture = new SpotifyFixture();
        Check((await fixture.Player.GetPlaybackAsync(new(), default)).NeedsLogin && fixture.Requests.Count == 0);
        Check((await fixture.Player.GetBrowserTokenAsync(new(), default)).Status == 401 && fixture.Requests.Count == 0);
    }),
    ("Browser playback receives only the current short-lived access token", async () =>
    {
        using var fixture = new SpotifyFixture();
        var token = await fixture.Player.GetBrowserTokenAsync(await fixture.Login(), default);
        Check(token.Success && token.AccessToken == "access-token" && token.ExpiresInSeconds > 0);
        Check(fixture.Requests.Count == 0);
    }),
    ("Browser playback refreshes an expiring token", async () =>
    {
        using var fixture = new SpotifyFixture();
        var user = await fixture.Login(TimeSpan.FromSeconds(-10));
        fixture.Replies.Enqueue(_ => JsonResponse("""{"access_token":"browser-token","expires_in":3600}"""));
        var token = await fixture.Player.GetBrowserTokenAsync(user, default);
        Check(token.Success && token.AccessToken == "browser-token" && fixture.Requests.Count == 1);
    }),
    ("Saved albums are paged and parsed safely", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => JsonResponse("""{"offset":12,"limit":12,"total":13,"items":[{"added_at":"2026-01-01T00:00:00Z","album":{"name":"Album","uri":"spotify:album:ABC123","artists":[{"name":"Artist"}],"images":[{"url":"https://i.scdn.co/image/cover"}]}},{"album":{"name":"Unsafe","uri":"https://attacker.example/album"}}]}"""));
        var result = await fixture.Player.GetSavedAlbumsAsync(user, 12, 12, default);
        var page = SpotifyPlayback.ParseSavedAlbums(result.Data!.Value);
        Check(result.Success && fixture.Requests[0].Uri.EndsWith("me/albums?limit=12&offset=12"));
        Check(page.Items.Count == 1 && page.Items[0].Name == "Album" && page.Items[0].Artists == "Artist");
        Check(page.HasPrevious && !page.HasNext && page.Items[0].Image == "https://i.scdn.co/image/cover");
    }),
    ("Album playback accepts only Spotify album URIs", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        Check((await fixture.Player.PlayAlbumAsync(user, "https://attacker.example/album", "device", default)).Status == 400);
        Check(fixture.Requests.Count == 0);
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        Check((await fixture.Player.PlayAlbumAsync(user, "spotify:album:ABC123", "device&other=value", default)).Success);
        Check(fixture.Requests[0].Uri.EndsWith("me/player/play?device_id=device%26other%3Dvalue"));
        using var body = JsonDocument.Parse(fixture.Requests[0].Body);
        Check(body.RootElement.GetProperty("context_uri").GetString() == "spotify:album:ABC123");
    }),
    ("Spotify 204 means idle player", async () =>
    {
        using var fixture = new SpotifyFixture();
        var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        var result = await fixture.Player.GetPlaybackAsync(user, default);
        Check(result.Success && result.Data is null);
    }),
    ("Expired token refreshes and preserves refresh token", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login(TimeSpan.FromSeconds(-10));
        fixture.Replies.Enqueue(r => JsonResponse("""{"access_token":"new-token","expires_in":3600}"""));
        fixture.Replies.Enqueue(r => { Check(r.Headers.Authorization?.Parameter == "new-token"); return new(HttpStatusCode.NoContent); });
        Check((await fixture.Player.GetPlaybackAsync(user, default)).Success);
        Check(fixture.Requests[0].Uri == "https://accounts.spotify.com/api/token");
        Check(fixture.Requests[0].Body.Contains("grant_type=refresh_token"));
        var token = await fixture.Store.UseAsync(user, (s, _) => Task.FromResult((s, s?.RefreshToken)), default);
        Check(token == "refresh-token");
    }),
    ("Revoked refresh token requires login and removes session", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login(TimeSpan.FromSeconds(-10));
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.BadRequest));
        Check((await fixture.Player.GetPlaybackAsync(user, default)).NeedsLogin);
        Check(!await fixture.Store.ExistsAsync(user, default));
    }),
    ("401 retries once after refresh", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.Unauthorized));
        fixture.Replies.Enqueue(_ => JsonResponse("""{"access_token":"rotated","refresh_token":"new-refresh","expires_in":3600}"""));
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        Check((await fixture.Player.GetPlaybackAsync(user, default)).Success && fixture.Requests.Count == 3);
        var refresh = await fixture.Store.UseAsync(user, (s, _) => Task.FromResult((s, s?.RefreshToken)), default);
        Check(refresh == "new-refresh");
    }),
    ("429 honors Retry-After without another network call", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); return r; });
        var first = await fixture.Player.GetPlaybackAsync(user, default);
        var second = await fixture.Player.GetDevicesAsync(user, default);
        Check(first.Status == 429 && first.RetryAfterSeconds == 60 && second.Status == 429 && fixture.Requests.Count == 1);
    }),
    ("404 reports missing device", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NotFound));
        var reply = await fixture.Player.CommandAsync(user, "play", "device", null, default);
        Check(reply.Error?.Contains("No active device") == true);
    }),
    ("403 explains account and device restrictions", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.Forbidden));
        Check((await fixture.Player.CommandAsync(user, "play", null, null, default)).Error?.Contains("Premium") == true);
    }),
    ("Playback commands use correct methods and escaped device IDs", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        foreach (var command in new[] { "play", "pause", "next", "previous", "volume", "seek", "transfer", "transfer-play" })
        {
            fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
            Check((await fixture.Player.CommandAsync(user, command, "device&other=value", 42, default)).Success);
        }
        Check(fixture.Requests.Select(r => r.Method).SequenceEqual(new[] { "PUT", "PUT", "POST", "POST", "PUT", "PUT", "PUT", "PUT" }));
        Check(fixture.Requests[4].Uri.Contains("volume_percent=42") && fixture.Requests[0].Uri.Contains("device%26other%3Dvalue"));
        Check(fixture.Requests[5].Uri.Contains("position_ms=42"));
        using var body = JsonDocument.Parse(fixture.Requests[6].Body);
        Check(body.RootElement.GetProperty("device_ids")[0].GetString() == "device&other=value");
        using var playBody = JsonDocument.Parse(fixture.Requests[7].Body);
        Check(playBody.RootElement.GetProperty("play").GetBoolean());
    }),
    ("Invalid volume sends no request", async () =>
    {
        using var fixture = new SpotifyFixture();
        Check((await fixture.Player.CommandAsync(await fixture.Login(), "volume", "device", 101, default)).Status == 400);
        Check(fixture.Requests.Count == 0);
    }),
    ("Logout invalidates other tabs", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        await fixture.Store.RemoveAsync(user, default);
        Check((await fixture.Player.GetPlaybackAsync(user, default)).NeedsLogin && fixture.Requests.Count == 0);
    }),
    ("Session tokens are encrypted and survive store recreation", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        var file = Directory.GetFiles(Path.Combine(fixture.Temp.Path, "sessions"), "*.token").Single();
        Check(!(await File.ReadAllTextAsync(file)).Contains("refresh-token"));
        var recreated = new SpotifySessionStore(new(fixture.Temp.Path), fixture.Protection);
        Check(await recreated.ExistsAsync(user, default));
    }),
    ("Tampered session is rejected", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        var file = Directory.GetFiles(Path.Combine(fixture.Temp.Path, "sessions"), "*.token").Single();
        await File.WriteAllTextAsync(file, "invalid-ciphertext");
        Check(!await fixture.Store.ExistsAsync(user, default));
    }),
    ("Refresh is serialized for concurrent tabs", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login(TimeSpan.FromSeconds(-10));
        fixture.Replies.Enqueue(_ => JsonResponse("""{"access_token":"new-token","expires_in":3600}"""));
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        var results = await Task.WhenAll(fixture.Player.GetPlaybackAsync(user, default), fixture.Player.GetPlaybackAsync(user, default));
        Check(results.All(r => r.Success) && fixture.Requests.Count(r => r.Uri.Contains("api/token")) == 1);
    }),
    ("Null playback item and restricted devices parse safely", () =>
    {
        using var json = JsonDocument.Parse("""{"item":null,"is_playing":false,"device":{"id":null,"name":"Speaker","is_restricted":true},"actions":{"disallows":{"skipping_next":true}}}""");
        var playback = SpotifyPlayback.Parse(json.RootElement);
        Check(playback.Title == "Nothing playing" && playback.Device!.Restricted && playback.Disallowed.Contains("skipping_next"));
        return Task.CompletedTask;
    }),
    ("Episode metadata and artwork parse", () =>
    {
        using var json = JsonDocument.Parse("""{"item":{"name":"Episode","type":"episode","show":{"name":"Show"},"duration_ms":50000,"images":[{"url":"https://i.scdn.co/image/1"}],"external_urls":{"spotify":"https://open.spotify.com/episode/1"}},"is_playing":true,"progress_ms":1000}""");
        var playback = SpotifyPlayback.Parse(json.RootElement);
        Check(playback.Title == "Episode" && playback.Creator == "Show" && playback.Image is not null && playback.ProgressMs == 1000);
        return Task.CompletedTask;
    }),
    ("Untrusted playback URLs are not rendered", () =>
    {
        using var json = JsonDocument.Parse("""{"item":{"album":{"images":[{"url":"https://attacker.example/image"}]},"external_urls":{"spotify":"javascript:alert(1)"}}}""");
        var playback = SpotifyPlayback.Parse(json.RootElement);
        Check(playback.Link is null && playback.Image is null);
        return Task.CompletedTask;
    }),
    ("SHA-256 inspector hashes without retaining a file", async () =>
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abc"));
        Check(await DiagnosticToolsService.Sha256Async(stream, default) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }),
    ("JWT inspector decodes local JSON", () =>
    {
        var result = DiagnosticToolsService.DecodeJwt("eyJhbGciOiJub25lIn0.eyJzdWIiOiIxMjMifQ.");
        Check(result.Error is null && result.Header.Contains("none") && result.Payload.Contains("123"));
        return Task.CompletedTask;
    }),
    ("Administrator password requires a strong configured secret", () =>
    {
        Check(!new AdminAccessService(new() { Password = "short" }).IsConfigured);
        var access = new AdminAccessService(new() { Username = "owner", Password = "a-long-test-password" });
        Check(access.IsConfigured && access.Validate("owner", "a-long-test-password") && !access.Validate("owner", "wrong"));
        return Task.CompletedTask;
    }),
    ("Telemetry history and audit records survive restart", () =>
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var store = new TelemetryStore(storage);
        store.Add(new MetricPoint(DateTimeOffset.UtcNow, 10, 5, 2, 1));
        store.Add(new IncidentEvent(DateTimeOffset.UtcNow, "Warning", "Test", "Incident"));
        store.Add(new AuditEvent(DateTimeOffset.UtcNow, "owner", "Test", "target", true));
        var recreated = new TelemetryStore(storage);
        Check(recreated.Metrics.Count == 1 && recreated.Incidents.Count == 1 && recreated.Audits.Count == 1);
        return Task.CompletedTask;
    }),
    ("Maintenance mode state survives restart", () =>
    {
        using var temp = new Temp(); var storage = new AppStorage(temp.Path); var state = new MaintenanceState(storage);
        state.Set(true, "Testing", DateTimeOffset.UtcNow.AddHours(1));
        Check(new MaintenanceState(storage).Current.Enabled && new MaintenanceState(storage).Current.Message == "Testing");
        return Task.CompletedTask;
    }),
    ("Log tailer rejects files outside the allowlist", () =>
    {
        using var temp = new Temp(); var file = System.IO.Path.Combine(temp.Path, "server.log"); File.WriteAllText(file, "line");
        var tools = new DiagnosticToolsService(new(), new TestHttpFactory(_ => new(HttpStatusCode.OK)));
        Check(tools.ReadLog(file).Error is not null);
        return Task.CompletedTask;
    }),
    ("Network diagnostics reject unapproved hosts", async () =>
    {
        var network = new NetworkDiagnosticsService(new(), new TestHttpFactory(_ => new(HttpStatusCode.OK)));
        try { await network.ResolveAsync("unapproved.example", default); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected allowlist rejection");
    }),
    ("Load tester enforces configured request and concurrency bounds", async () =>
    {
        var count = 0; var options = new OperationsSettings { ApprovedUrls = ["https://example.test/data"], LoadTestMaxRequests = 3, LoadTestMaxConcurrency = 2 };
        var tools = new DiagnosticToolsService(options, new TestHttpFactory(_ => { Interlocked.Increment(ref count); return new(HttpStatusCode.OK); }));
        var result = await tools.LoadTestAsync(options.ApprovedUrls[0], 100, 100, default);
        Check(result.Requested == 3 && result.Succeeded == 3 && count == 3);
    }),
    ("Alert delivery posts to an owner-configured HTTPS webhook", async () =>
    {
        var count = 0;
        var alerts = new AlertSettings { WebhookUrl = "https://alerts.example.test/devpulse" };
        var delivery = new AlertDeliveryService(alerts,
            new TestHttpFactory(request => { Check(request.RequestUri == new Uri(alerts.WebhookUrl)); Interlocked.Increment(ref count); return new(HttpStatusCode.OK); }),
            NullLogger<AlertDeliveryService>.Instance);
        await delivery.DeliverAsync(new(DateTimeOffset.UtcNow, "Warning", "Test", "Threshold exceeded"), default);
        Check(count == 1);
    })
};
int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} checks passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
static async Task Cancelled(Func<Task> action)
{
    try { await action(); } catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Expected cancellation");
}
static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
}
sealed class TestHttpFactory(Func<HttpRequestMessage, HttpResponseMessage> send) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new Stub((request, _) => Task.FromResult(send(request))));
}
sealed class Temp : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devpulse-test-" + Guid.NewGuid().ToString("N"));
    public Temp() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
sealed class SpotifyFixture : IHttpClientFactory, IDisposable
{
    public Temp Temp { get; } = new();
    public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
    public SpotifySessionStore Store { get; }
    public SpotifyPlayerService Player { get; }
    public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Replies { get; } = new();
    public List<(string Method, string Uri, string Body)> Requests { get; } = [];
    public SpotifyFixture(SpotifySettings? settings = null)
    {
        Store = new(new(Temp.Path), Protection);
        Player = new(this, settings ?? new("test-client", "test-secret"), Store, NullLogger<SpotifyPlayerService>.Instance);
    }
    public HttpClient CreateClient(string name) => new(new Stub(async (r, ct) =>
    {
        Requests.Add((r.Method.Method, r.RequestUri!.AbsoluteUri, r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
        return Replies.Dequeue()(r);
    }));
    public async Task<ClaimsPrincipal> Login(TimeSpan? lifetime = null)
    {
        var id = await Store.CreateAsync("access-token", "refresh-token", lifetime ?? TimeSpan.FromHours(1), default);
        return new(new ClaimsIdentity([new Claim(SpotifySessionStore.SessionClaim, id)], "test"));
    }
    public void Dispose() => Temp.Dispose();
}
