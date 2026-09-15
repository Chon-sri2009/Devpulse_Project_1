using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MiniProject_Everything_1.Services;

public sealed record SpotifySettings(string ClientId, string ClientSecret)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
public sealed record AppStorage(string Path);
public sealed record DiagnosticsSettings(bool Enabled, string? FolderRoot);
public sealed record SpotifySession(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, DateTimeOffset SessionEndsAt);

// Only an opaque session ID goes into the cookie. Tokens are encrypted on the server.
public sealed class SpotifySessionStore(AppStorage storage, IDataProtectionProvider protection)
{
    public const string SessionClaim = "devpulse:spotify-session";
    private readonly IDataProtector protector = protection.CreateProtector("DevPulse.Spotify.v1");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory = System.IO.Path.Combine(storage.Path, "sessions");

    public async Task<string> CreateAsync(string accessToken, string? refreshToken, TimeSpan lifetime, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.EnumerateFiles(directory, "*.token"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file);
            await SaveAsync(id, new(accessToken, refreshToken, DateTimeOffset.UtcNow + lifetime,
                DateTimeOffset.UtcNow.AddDays(7)), ct);
            return id;
        }
        finally { gate.Release(); }
    }
    public async Task<bool> ExistsAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await UseAsync(user, (session, _) => Task.FromResult((session, session is not null)), ct);

    // Serializes refresh and logout, including across two tabs using the same cookie.
    public async Task<T> UseAsync<T>(ClaimsPrincipal user,
        Func<SpotifySession?, CancellationToken, Task<(SpotifySession? Session, T Result)>> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var id = user.FindFirstValue(SessionClaim);
            if (!Guid.TryParseExact(id, "N", out _)) return (await action(null, ct)).Result;
            var session = await ReadAsync(id, ct);
            var updated = await action(session, ct);
            if (updated.Session is null) File.Delete(FilePath(id));
            else if (updated.Session != session) await SaveAsync(id, updated.Session, ct);
            return updated.Result;
        }
        finally { gate.Release(); }
    }
    public async Task RemoveAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await UseAsync(user, (_, _) => Task.FromResult<(SpotifySession?, bool)>((null, true)), ct);
    private string FilePath(string id) => System.IO.Path.Combine(directory, id + ".token");
    private async Task<SpotifySession?> ReadAsync(string id, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(FilePath(id))) return null;
            var session = JsonSerializer.Deserialize<SpotifySession>(protector.Unprotect(await File.ReadAllTextAsync(FilePath(id), ct)));
            return session?.SessionEndsAt > DateTimeOffset.UtcNow ? session : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        { return null; }
    }
    private async Task SaveAsync(string id, SpotifySession session, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath(id) + ".tmp";
        await File.WriteAllTextAsync(temp, protector.Protect(JsonSerializer.Serialize(session)), ct);
        File.Move(temp, FilePath(id), overwrite: true);
    }
}
