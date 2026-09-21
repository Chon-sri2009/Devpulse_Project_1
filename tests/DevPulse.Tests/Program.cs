using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
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
    ("Created, private, and collaborative playlists are paged and parsed safely", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => JsonResponse("""{"offset":0,"limit":12,"total":2,"items":[{"name":"My Mix","uri":"spotify:playlist:ABC123","owner":{"display_name":"Owner"},"images":[{"url":"https://mosaic.scdn.co/640/cover"}],"tracks":{"total":24},"public":false,"collaborative":true},{"name":"Unsafe","uri":"javascript:alert(1)"}]}"""));
        var result = await fixture.Player.GetPlaylistsAsync(user, 0, 12, default);
        var page = SpotifyPlayback.ParsePlaylists(result.Data!.Value);
        Check(result.Success && fixture.Requests[0].Uri.EndsWith("me/playlists?limit=12&offset=0"));
        Check(page.Items.Count == 1 && page.Items[0].Name == "My Mix" && page.Items[0].Owner == "Owner");
        Check(page.Items[0].Tracks == 24 && page.Items[0].Public == false && page.Items[0].Collaborative);
        Check(page.Items[0].Image == "https://mosaic.scdn.co/640/cover");
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
    ("Playlist playback accepts only Spotify playlist URIs", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        Check((await fixture.Player.PlayPlaylistAsync(user, "https://attacker.example/playlist", "phone", default)).Status == 400);
        Check(fixture.Requests.Count == 0);
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        Check((await fixture.Player.PlayPlaylistAsync(user, "spotify:playlist:ABC123", "phone&other=value", default)).Success);
        Check(fixture.Requests[0].Uri.EndsWith("me/player/play?device_id=phone%26other%3Dvalue"));
        using var body = JsonDocument.Parse(fixture.Requests[0].Body);
        Check(body.RootElement.GetProperty("context_uri").GetString() == "spotify:playlist:ABC123");
    }),
    ("Spotify track search is encoded, bounded, and parsed safely", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => JsonResponse("""{"tracks":{"offset":0,"limit":10,"total":1,"items":[{"name":"Song","uri":"spotify:track:TRACK123","artists":[{"name":"Artist"}],"album":{"name":"Album","images":[{"url":"https://i.scdn.co/image/cover"}]},"external_urls":{"spotify":"https://open.spotify.com/track/TRACK123"},"duration_ms":123000,"explicit":true,"is_playable":true}]}}"""));
        var result = await fixture.Player.SearchTracksAsync(user, "song & artist", 0, default);
        var page = SpotifyPlayback.ParseSearchTracks(result.Data!.Value, "song & artist");
        Check(result.Success && fixture.Requests[0].Uri.Contains("search?q=song%20%26%20artist&type=track&limit=10"));
        Check(page.Items.Count == 1 && page.Items[0].Name == "Song" && page.Items[0].Artists == "Artist");
        Check(page.Items[0].Explicit && page.Items[0].Playable && page.Items[0].DurationMs == 123000);
        Check((await fixture.Player.SearchTracksAsync(user, "", 0, default)).Status == 400 && fixture.Requests.Count == 1);
    }),
    ("Liked songs and queue tracks parse their different response shapes", () =>
    {
        using var likedJson = JsonDocument.Parse("""{"offset":0,"limit":20,"total":1,"items":[{"added_at":"2026-01-01T00:00:00Z","track":{"name":"Liked","uri":"spotify:track:LIKED1","artists":[{"name":"Artist"}],"album":{"name":"Album"},"duration_ms":60000}}]}""");
        using var queueJson = JsonDocument.Parse("""{"queue":[{"name":"Next","uri":"spotify:track:NEXT1","artists":[{"name":"Artist"}],"album":{"name":"Album"},"duration_ms":90000},{"name":"Unsafe","uri":"https://attacker.example"}]}""");
        var liked = SpotifyPlayback.ParseSavedTracks(likedJson.RootElement);
        var queue = SpotifyPlayback.ParseQueue(queueJson.RootElement);
        Check(liked.Items.Count == 1 && liked.Items[0].Name == "Liked" && liked.Total == 1);
        Check(queue.Count == 1 && queue[0].Name == "Next");
        return Task.CompletedTask;
    }),
    ("Exact track playback supports standalone and album context", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        Check((await fixture.Player.PlayTrackAsync(user, "spotify:track:TRACK1", null, "phone", default)).Success);
        Check((await fixture.Player.PlayTrackAsync(user, "spotify:track:TRACK2", "spotify:album:ALBUM1", "phone", default)).Success);
        using var standalone = JsonDocument.Parse(fixture.Requests[0].Body);
        using var contextual = JsonDocument.Parse(fixture.Requests[1].Body);
        Check(standalone.RootElement.GetProperty("uris")[0].GetString() == "spotify:track:TRACK1");
        Check(contextual.RootElement.GetProperty("context_uri").GetString() == "spotify:album:ALBUM1");
        Check(contextual.RootElement.GetProperty("offset").GetProperty("uri").GetString() == "spotify:track:TRACK2");
    }),
    ("Queue, shuffle, and repeat commands use safe Spotify endpoints", async () =>
    {
        using var fixture = new SpotifyFixture(); var user = await fixture.Login();
        for (var i = 0; i < 4; i++) fixture.Replies.Enqueue(_ => new(HttpStatusCode.NoContent));
        Check((await fixture.Player.AddToQueueAsync(user, "spotify:track:TRACK1", "device&x=1", default)).Success);
        Check((await fixture.Player.CommandAsync(user, "shuffle-on", "device", null, default)).Success);
        Check((await fixture.Player.CommandAsync(user, "repeat-track", "device", null, default)).Success);
        Check((await fixture.Player.CommandAsync(user, "repeat-context", "device", null, default)).Success);
        Check(fixture.Requests[0].Method == "POST" && fixture.Requests[0].Uri.Contains("uri=spotify%3Atrack%3ATRACK1"));
        Check(fixture.Requests[0].Uri.Contains("device_id=device%26x%3D1"));
        Check(fixture.Requests[1].Uri.EndsWith("shuffle?state=true&device_id=device"));
        Check(fixture.Requests[2].Uri.EndsWith("repeat?state=track&device_id=device"));
        Check(fixture.Requests[3].Uri.EndsWith("repeat?state=context&device_id=device"));
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
        Check(!body.RootElement.GetProperty("play").GetBoolean());
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
    ("AES-GCM file encryption round-trips bytes and the original name", async () =>
    {
        var original = System.Text.Encoding.UTF8.GetBytes("DevPulse protected file test");
        await using var input = new MemoryStream(original);
        var encrypted = await DiagnosticToolsService.EncryptFileAsync(input, "sample.txt", "correct horse battery staple", default);
        Check(encrypted.FileName == "sample.txt.devpulse" && !encrypted.Contents.SequenceEqual(original));
        await using var package = new MemoryStream(encrypted.Contents);
        var decrypted = await DiagnosticToolsService.DecryptFileAsync(package, "correct horse battery staple", default);
        try { Check(decrypted.FileName == "sample.txt" && decrypted.Contents.SequenceEqual(original)); }
        finally { CryptographicOperations.ZeroMemory(decrypted.Contents); }
    }),
    ("AES-GCM file decryption rejects a wrong password", async () =>
    {
        await using var input = new MemoryStream([1, 2, 3, 4]);
        var encrypted = await DiagnosticToolsService.EncryptFileAsync(input, "sample.bin", "correct horse battery staple", default);
        await using var package = new MemoryStream(encrypted.Contents);
        try { await DiagnosticToolsService.DecryptFileAsync(package, "another secure password", default); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("incorrect")) { return; }
        throw new InvalidOperationException("Expected wrong-password rejection");
    }),
    ("AES-GCM file decryption rejects tampering", async () =>
    {
        await using var input = new MemoryStream([1, 2, 3, 4]);
        var encrypted = await DiagnosticToolsService.EncryptFileAsync(input, "sample.bin", "correct horse battery staple", default);
        encrypted.Contents[^1] ^= 0xff;
        await using var package = new MemoryStream(encrypted.Contents);
        try { await DiagnosticToolsService.DecryptFileAsync(package, "correct horse battery staple", default); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("changed")) { return; }
        throw new InvalidOperationException("Expected tamper rejection");
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
    ("One-off network target accepts domains, IPs, and URLs", () =>
    {
        Check(PublicHttpTarget.ParseHost("example.com") == "example.com");
        Check(PublicHttpTarget.ParseHost("https://example.com/health") == "example.com");
        Check(PublicHttpTarget.ParseHost("8.8.8.8") == "8.8.8.8");
        return Task.CompletedTask;
    }),
    ("One-off network checks block unapproved private targets", async () =>
    {
        var network = new NetworkDiagnosticsService(new(), new TestHttpFactory(_ => new(HttpStatusCode.OK)));
        foreach (var check in new Func<Task>[]
        {
            async () => { await network.PingTargetAsync("127.0.0.1", default); },
            async () => { await network.ResolveTargetAsync("10.0.0.1", default); },
            async () => { await network.InspectTlsTargetAsync("192.168.1.1", default); },
            async () => { await network.CheckDatabaseTargetAsync("[::1]", 5432, "TCP", default); }
        })
        {
            try { await check(); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("Expected private target rejection");
        }
    }),
    ("Owner-configured private database target can be checked directly", async () =>
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var options = new OperationsSettings { Databases = [new() { Name = "Local test", Host = "127.0.0.1", Port = port, Kind = "TCP" }] };
        var network = new NetworkDiagnosticsService(options, new TestHttpFactory(_ => new(HttpStatusCode.OK)));
        var result = await network.CheckDatabaseTargetAsync("127.0.0.1", port, "TCP", default);
        Check(result.Healthy && result.Kind == "TCP");
    }),
    ("Public JSON URLs accept a domain without a scheme", async () =>
    {
        string? requested = null;
        var tools = new DiagnosticToolsService(new(), new TestHttpFactory(request =>
        {
            requested = request.RequestUri!.AbsoluteUri;
            return JsonResponse("{\"status\":\"ok\"}");
        }));
        var json = await tools.FetchJsonAsync("93.184.216.34/data", default);
        Check(requested == "https://93.184.216.34/data" && json.Contains("ok"));
    }),
    ("Public JSON URLs block local and private targets", async () =>
    {
        foreach (var value in new[] { "http://127.0.0.1/data", "http://10.0.0.1/data", "http://[::1]/data", "http://[fc00::1]/data" })
        {
            var uri = PublicHttpTarget.Parse(value);
            try { await PublicHttpTarget.EnsurePublicAsync(uri, default); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException($"Expected private target rejection for {value}");
        }
    }),
    ("Public address classification rejects non-public ranges", () =>
    {
        Check(PublicHttpTarget.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
        Check(PublicHttpTarget.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")));
        Check(!PublicHttpTarget.IsPublicAddress(IPAddress.Parse("169.254.1.1")));
        Check(!PublicHttpTarget.IsPublicAddress(IPAddress.Parse("172.16.0.1")));
        Check(!PublicHttpTarget.IsPublicAddress(IPAddress.Parse("192.168.1.1")));
        Check(!PublicHttpTarget.IsPublicAddress(IPAddress.Parse("fe80::1")));
        return Task.CompletedTask;
    }),
    ("Owner-approved JSON URLs can intentionally target a private service", async () =>
    {
        var options = new OperationsSettings { ApprovedUrls = ["http://127.0.0.1/data"] };
        var tools = new DiagnosticToolsService(options, new TestHttpFactory(_ => JsonResponse("{\"source\":\"approved\"}")));
        Check((await tools.FetchJsonAsync(options.ApprovedUrls[0], default)).Contains("approved"));
    }),
    ("Website audit crawls assets and validates JSON, headers, and links", async () =>
    {
        var factory = new TestHttpFactory(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/" => new(HttpStatusCode.OK) { Content = new StringContent("""<!doctype html><html lang="en"><head><title>Example audit website</title><meta name="description" content="Audit fixture"><meta name="viewport" content="width=device-width"><script src="/app.js"></script></head><body><h1>Example</h1><img src="/missing.png" alt="Missing fixture"></body></html>""", System.Text.Encoding.UTF8, "text/html") },
                "/app.js" => new(HttpStatusCode.OK) { Content = new StringContent("fetch('/data.json')", System.Text.Encoding.UTF8, "application/javascript") },
                "/data.json" => JsonResponse("[{\"id\":1,\"name\":\"One\",\"category\":\"mcu\",\"description\":\"A\",\"processor\":\"P\"},{\"id\":1}]") ,
                "/missing.png" => new(HttpStatusCode.NotFound),
                "/robots.txt" => new(HttpStatusCode.OK) { Content = new StringContent("User-agent: *") },
                "/sitemap.xml" => new(HttpStatusCode.OK) { Content = new StringContent("<urlset></urlset>", System.Text.Encoding.UTF8, "application/xml") },
                _ => new(HttpStatusCode.NotFound)
            };
            if (path == "/") response.Headers.TryAddWithoutValidation("Strict-Transport-Security", "max-age=31536000");
            return response;
        });
        var audit = new WebsiteAuditService(factory, new(), new() { MaxResources = 30 });
        var result = await audit.AuditAsync("https://93.184.216.34/", default);
        Check(result.Resources.Any(x => x.Kind == "JSON") && result.BrokenResources == 1);
        Check(result.Findings.Any(x => x.Area == "JSON" && x.Message.Contains("Duplicate")));
        Check(result.Findings.Any(x => x.Area == "Security" && x.Message.Contains("Content-Security-Policy")));
    }),
    ("Website uptime monitor reports a successful public response", async () =>
    {
        var audit = new WebsiteAuditService(new TestHttpFactory(_ => new(HttpStatusCode.NoContent)), new(), new());
        var result = await audit.CheckUptimeAsync("https://93.184.216.34/health", default);
        Check(result.Healthy && result.Status == 204 && result.Message == "Available");
    }),
    ("Website audit blocks an unapproved private origin", async () =>
    {
        var count = 0;
        var audit = new WebsiteAuditService(new TestHttpFactory(_ => { count++; return new(HttpStatusCode.OK); }), new(), new());
        try { await audit.AuditAsync("http://127.0.0.1/", default); }
        catch (InvalidOperationException) { Check(count == 0); return; }
        throw new InvalidOperationException("Expected private website audit rejection");
    }),
    ("IPv4 calculator returns subnet range, masks, and capacity", () =>
    {
        var result = NetworkCalculatorService.CalculateIpv4("192.168.10.25", 24);
        Check(result.Network == "192.168.10.0" && result.Broadcast == "192.168.10.255");
        Check(result.FirstUsable == "192.168.10.1" && result.LastUsable == "192.168.10.254");
        Check(result.SubnetMask == "255.255.255.0" && result.WildcardMask == "0.0.0.255" && result.UsableHosts == 254);
        return Task.CompletedTask;
    }),
    ("IPv4 point-to-point and host routes use modern usable counts", () =>
    {
        var pointToPoint = NetworkCalculatorService.CalculateIpv4("10.0.0.4", 31);
        var host = NetworkCalculatorService.CalculateIpv4("10.0.0.9", 32);
        Check(pointToPoint.FirstUsable == "10.0.0.4" && pointToPoint.LastUsable == "10.0.0.5" && pointToPoint.UsableHosts == 2);
        Check(host.Network == "10.0.0.9" && host.UsableHosts == 1);
        return Task.CompletedTask;
    }),
    ("Subnet mask conversion validates contiguous bits", () =>
    {
        var result = NetworkCalculatorService.ConvertMask("255.255.252.0");
        Check(result.Prefix == 22 && result.WildcardMask == "0.0.3.255" && result.TotalAddresses == 1024);
        try { NetworkCalculatorService.ConvertMask("255.0.255.0"); }
        catch (ArgumentException) { return Task.CompletedTask; }
        throw new InvalidOperationException("Expected a non-contiguous mask to be rejected");
    }),
    ("IPv6 calculator expands and masks a prefix", () =>
    {
        var result = NetworkCalculatorService.CalculateIpv6("2001:db8:10:20::1234", 64);
        Check(result.Network == "2001:db8:10:20::" && result.LastAddress == "2001:db8:10:20:ffff:ffff:ffff:ffff");
        Check(result.ExpandedAddress == "2001:0db8:0010:0020:0000:0000:0000:1234" && result.TotalAddresses == "18446744073709551616");
        return Task.CompletedTask;
    }),
    ("Route summarizer finds the smallest covering CIDR", () =>
    {
        var result = NetworkCalculatorService.SummarizeIpv4(["10.20.0.0/24", "10.20.1.0/24"]);
        Check(result.Cidr == "10.20.0.0/23" && result.Broadcast == "10.20.1.255" && result.AdditionalAddresses == 0);
        return Task.CompletedTask;
    }),
    ("IPv4 range calculator counts inclusively and recognizes exact blocks", () =>
    {
        var exact = NetworkCalculatorService.CalculateIpv4Range("192.168.1.0", "192.168.1.255");
        var partial = NetworkCalculatorService.CalculateIpv4Range("192.168.1.50", "192.168.1.199");
        Check(exact.Count == 256 && exact.ExactCidr == "192.168.1.0/24");
        Check(partial.Count == 150 && partial.ExactCidr is null);
        return Task.CompletedTask;
    }),
    ("VLSM planner allocates descending non-overlapping subnets", () =>
    {
        var result = NetworkCalculatorService.PlanVlsm("10.10.0.0/24", [new("Web", 50), new("DB", 20), new("Management", 10)]);
        Check(result.Count == 3 && result[0].Cidr == "10.10.0.0/26" && result[0].Capacity == 62);
        Check(result[1].Cidr == "10.10.0.64/27" && result[2].Cidr == "10.10.0.96/28");
        return Task.CompletedTask;
    }),
    ("VLSM planner rejects requirements that exceed the parent", () =>
    {
        try { NetworkCalculatorService.PlanVlsm("10.0.0.0/30", [new("Too large", 10)]); }
        catch (InvalidOperationException) { return Task.CompletedTask; }
        throw new InvalidOperationException("Expected oversized VLSM plan to be rejected");
    }),
    ("Transfer calculator accounts for overhead and latency", () =>
    {
        var result = NetworkCalculatorService.CalculateTransfer(1, "GB", 100, "Mbps", 0, 20);
        Check(Math.Abs(result.Seconds - 80) < 0.001 && Math.Abs(result.BandwidthDelayProductBytes - 250000) < 0.001);
        return Task.CompletedTask;
    }),
    ("MTU calculator returns TCP MSS and UDP payload", () =>
    {
        var ipv4 = NetworkCalculatorService.CalculateMtu(1500, 4, 0);
        var ipv6 = NetworkCalculatorService.CalculateMtu(1500, 6, 20);
        Check(ipv4.TcpMss == 1460 && ipv4.UdpPayload == 1472 && ipv4.EthernetFrameBytes == 1518);
        Check(ipv6.TcpMss == 1420 && ipv6.UdpPayload == 1452);
        return Task.CompletedTask;
    }),
    ("Load tester accepts an arbitrary public URL with bounded requests", async () =>
    {
        var count = 0; string? requested = null;
        var options = new OperationsSettings { LoadTestMaxRequests = 3, LoadTestMaxConcurrency = 2 };
        var tools = new DiagnosticToolsService(options, new TestHttpFactory(request =>
        {
            Interlocked.Increment(ref count); requested = request.RequestUri!.AbsoluteUri;
            return new(HttpStatusCode.NoContent);
        }));
        var result = await tools.LoadTestAsync("93.184.216.34/health", 20, 20, default);
        Check(result.Requested == 3 && result.Succeeded == 3 && count == 3 && requested == "https://93.184.216.34/health");
    }),
    ("Load tester blocks an unapproved private target", async () =>
    {
        var count = 0;
        var tools = new DiagnosticToolsService(new(), new TestHttpFactory(_ => { count++; return new(HttpStatusCode.OK); }));
        try { await tools.LoadTestAsync("http://127.0.0.1/private", 1, 1, default); }
        catch (InvalidOperationException) { Check(count == 0); return; }
        throw new InvalidOperationException("Expected private load-test target rejection");
    }),
    ("Load tester enforces configured request and concurrency bounds", async () =>
    {
        var count = 0; var options = new OperationsSettings { ApprovedUrls = ["https://example.test/data"], LoadTestMaxRequests = 3, LoadTestMaxConcurrency = 2 };
        var tools = new DiagnosticToolsService(options, new TestHttpFactory(_ => { Interlocked.Increment(ref count); return new(HttpStatusCode.OK); }));
        var result = await tools.LoadTestAsync(options.ApprovedUrls[0], 100, 100, default);
        Check(result.Requested == 3 && result.Succeeded == 3 && count == 3);
    }),
    ("API runner sends a bounded request and evaluates assertions", async () =>
    {
        HttpRequestMessage? sent = null;
        var options = new OperationsSettings { ApprovedUrls = ["https://api.example.test/items"] };
        var runner = new ApiCollectionService(options, new TestHttpFactory(request =>
        {
            sent = request;
            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"result\":\"created\"}", System.Text.Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("X-Request-Id", "test-1");
            return response;
        }));
        var request = new ApiRequestDefinition
        {
            Name = "Create item", Method = "POST", Url = options.ApprovedUrls[0],
            Headers = "Accept: application/json\nX-Test: yes", Body = "{\"name\":\"item\"}",
            ExpectedStatus = 201, ExpectedContains = "created"
        };
        var result = await runner.ExecuteAsync(request, "Bearer secret-token", default);
        Check(result.Passed && result.StatusCode == 201 && result.Body.Contains("created"));
        Check(sent?.Method == HttpMethod.Post && sent.Headers.Authorization?.Scheme == "Bearer");
        Check(sent!.Headers.TryGetValues("X-Test", out var custom) && custom.Single() == "yes");
        Check(!result.Headers.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }),
    ("API runner blocks an unapproved private target", async () =>
    {
        var count = 0;
        var runner = new ApiCollectionService(new(), new TestHttpFactory(_ =>
        { Interlocked.Increment(ref count); return new(HttpStatusCode.OK); }));
        try
        {
            await runner.ExecuteAsync(new() { Name = "Private", Url = "http://127.0.0.1/admin" }, null, default);
        }
        catch (InvalidOperationException) { Check(count == 0); return; }
        throw new InvalidOperationException("Expected private API target rejection");
    }),
    ("API runner allows unsafe methods only for exact owner-approved URLs", async () =>
    {
        var count = 0;
        var runner = new ApiCollectionService(new(), new TestHttpFactory(_ =>
        { Interlocked.Increment(ref count); return new(HttpStatusCode.NoContent); }));
        try
        {
            await runner.ExecuteAsync(new()
            {
                Name = "Unapproved write", Method = "DELETE", Url = "https://93.184.216.34/items/1",
                ExpectedStatus = 204
            }, null, default);
        }
        catch (InvalidOperationException) { Check(count == 0); return; }
        throw new InvalidOperationException("Expected unapproved write-method rejection");
    }),
    ("DNS email inspector recognizes MX, SPF, DMARC, and DKIM", async () =>
    {
        var inspector = new DnsEmailInspectorService(new TestHttpFactory(request =>
        {
            var query = request.RequestUri!.Query;
            var answer = query.Contains("_dmarc.example.com", StringComparison.Ordinal)
                ? "[{\"name\":\"_dmarc.example.com.\",\"type\":16,\"TTL\":300,\"data\":\"\\\"v=DMARC1; p=reject\\\"\"}]"
                : query.Contains("default._domainkey.example.com", StringComparison.Ordinal)
                    ? "[{\"name\":\"default._domainkey.example.com.\",\"type\":16,\"TTL\":300,\"data\":\"\\\"v=DKIM1; p=abc\\\"\"}]"
                    : query.Contains("type=MX", StringComparison.Ordinal)
                        ? "[{\"name\":\"example.com.\",\"type\":15,\"TTL\":300,\"data\":\"10 mail.example.com.\"}]"
                        : query.Contains("type=TXT", StringComparison.Ordinal)
                            ? "[{\"name\":\"example.com.\",\"type\":16,\"TTL\":300,\"data\":\"\\\"v=spf1 -all\\\"\"}]"
                            : query.Contains("type=NS", StringComparison.Ordinal)
                                ? "[{\"name\":\"example.com.\",\"type\":2,\"TTL\":300,\"data\":\"ns1.example.com.\"},{\"name\":\"example.com.\",\"type\":2,\"TTL\":300,\"data\":\"ns2.example.com.\"}]"
                                : query.Contains("type=A&", StringComparison.Ordinal)
                                    ? "[{\"name\":\"example.com.\",\"type\":1,\"TTL\":300,\"data\":\"93.184.216.34\"}]"
                                    : "[]";
            return JsonResponse($"{{\"Status\":0,\"Answer\":{answer}}}");
        }));
        var result = await inspector.InspectAsync("admin@example.com", "default", default);
        Check(result.Domain == "example.com" && result.Records.Any(record => record.Type == "MX"));
        Check(result.Findings.Any(finding => finding.Title == "SPF found" && finding.Level == "Good"));
        Check(result.Findings.Any(finding => finding.Title == "DMARC enforcement enabled"));
        Check(result.Findings.Any(finding => finding.Title == "DKIM key found"));
    }),
    ("DNS inspector validates and normalizes public domains", () =>
    {
        Check(DnsEmailInspectorService.NormalizeDomain("https://WWW.Example.COM/path") == "www.example.com");
        try { DnsEmailInspectorService.NormalizeDomain("127.0.0.1"); }
        catch (InvalidOperationException) { return Task.CompletedTask; }
        throw new InvalidOperationException("Expected IP address rejection");
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
