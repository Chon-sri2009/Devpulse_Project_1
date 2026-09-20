using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed record SpotifyDevice(string Id, string Name, bool Active, bool Restricted, bool SupportsVolume, int? Volume);
public sealed record SpotifyAlbum(string Uri, string Name, string Artists, string? Image, DateTimeOffset? AddedAt)
{
    public static bool IsSafeUri(string? value)
    {
        const string prefix = "spotify:album:";
        return value is { Length: > 14 } && value.StartsWith(prefix, StringComparison.Ordinal)
            && value[prefix.Length..].All(char.IsAsciiLetterOrDigit);
    }
}
public sealed record SpotifyAlbumPage(IReadOnlyList<SpotifyAlbum> Items, int Offset, int Limit, int Total)
{
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Offset + Items.Count < Total;
}
public sealed record SpotifyPlaylist(string Uri, string Name, string Owner, string? Image, int Tracks, bool? Public, bool Collaborative)
{
    public static bool IsSafeUri(string? value)
    {
        const string prefix = "spotify:playlist:";
        return value is { Length: > 17 } && value.StartsWith(prefix, StringComparison.Ordinal)
            && value[prefix.Length..].All(char.IsAsciiLetterOrDigit);
    }
}
public sealed record SpotifyPlaylistPage(IReadOnlyList<SpotifyPlaylist> Items, int Offset, int Limit, int Total)
{
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Offset + Items.Count < Total;
}
public sealed record SpotifyPlayback(string Title, string Creator, string? Image, string? Link,
    bool Playing, int ProgressMs, int DurationMs, SpotifyDevice? Device, IReadOnlySet<string> Disallowed)
{
    public static SpotifyPlayback Parse(JsonElement json)
    {
        var item = Property(json, "item");
        var album = Property(item, "album");
        var artists = Property(item, "artists");
        var creator = artists.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artists.EnumerateArray().Select(x => Text(x, "name")))
            : Text(Property(item, "show"), "name");
        var images = Property(album, "images");
        if (images.ValueKind != JsonValueKind.Array) images = Property(item, "images");
        var image = images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
            ? Text(images[0], "url") : null;
        var actions = Property(json, "actions");
        var disallows = Property(actions, "disallows");
        if (disallows.ValueKind == JsonValueKind.Object) actions = disallows;
        var blocked = actions.ValueKind == JsonValueKind.Object
            ? actions.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.True).Select(x => x.Name).ToHashSet()
            : [];
        var device = Property(json, "device");
        return new(Text(item, "name", "Nothing playing"), creator,
            SafeUrl(image, "i.scdn.co"), SafeUrl(Text(Property(item, "external_urls"), "spotify"), "open.spotify.com"),
            Flag(json, "is_playing"), Number(json, "progress_ms") ?? 0, Number(item, "duration_ms") ?? 0,
            device.ValueKind == JsonValueKind.Object ? ParseDevice(device) : null, blocked);
    }
    public static IReadOnlyList<SpotifyDevice> ParseDevices(JsonElement json)
    {
        var devices = Property(json, "devices");
        return devices.ValueKind == JsonValueKind.Array ? devices.EnumerateArray().Select(ParseDevice).ToList() : [];
    }
    public static SpotifyAlbumPage ParseSavedAlbums(JsonElement json)
    {
        var items = Property(json, "items");
        var albums = new List<SpotifyAlbum>();
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var album = Property(item, "album");
                var uri = Text(album, "uri");
                if (!SpotifyAlbum.IsSafeUri(uri)) continue;
                var artists = Property(album, "artists");
                var creator = artists.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", artists.EnumerateArray().Select(x => Text(x, "name")).Where(x => x.Length > 0))
                    : "";
                var images = Property(album, "images");
                var image = images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
                    ? SafeUrl(Text(images[0], "url"), "i.scdn.co") : null;
                DateTimeOffset? addedAt = DateTimeOffset.TryParse(Text(item, "added_at"), out var parsed) ? parsed : null;
                albums.Add(new(uri, Text(album, "name", "Untitled album"), creator, image, addedAt));
            }
        }
        return new(albums, Number(json, "offset") ?? 0, Number(json, "limit") ?? albums.Count,
            Number(json, "total") ?? albums.Count);
    }
    public static SpotifyPlaylistPage ParsePlaylists(JsonElement json)
    {
        var items = Property(json, "items");
        var playlists = new List<SpotifyPlaylist>();
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var playlist in items.EnumerateArray())
            {
                var uri = Text(playlist, "uri");
                if (!SpotifyPlaylist.IsSafeUri(uri)) continue;
                var images = Property(playlist, "images");
                var image = images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
                    ? SafeSpotifyImage(Text(images[0], "url")) : null;
                var trackContainer = Property(playlist, "tracks");
                if (trackContainer.ValueKind != JsonValueKind.Object) trackContainer = Property(playlist, "items");
                var publicValue = Property(playlist, "public");
                bool? isPublic = publicValue.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
                playlists.Add(new(uri, Text(playlist, "name", "Untitled playlist"),
                    Text(Property(playlist, "owner"), "display_name", "Spotify user"), image,
                    Number(trackContainer, "total") ?? 0, isPublic, Flag(playlist, "collaborative")));
            }
        }
        return new(playlists, Number(json, "offset") ?? 0, Number(json, "limit") ?? playlists.Count,
            Number(json, "total") ?? playlists.Count);
    }
    private static SpotifyDevice ParseDevice(JsonElement d) => new(Text(d, "id"), Text(d, "name", "Spotify device"),
        Flag(d, "is_active"), Flag(d, "is_restricted"), Flag(d, "supports_volume"), Number(d, "volume_percent"));
    private static JsonElement Property(JsonElement j, string key) =>
        j.ValueKind == JsonValueKind.Object && j.TryGetProperty(key, out var v) ? v : default;
    private static string Text(JsonElement j, string key, string fallback = "") =>
        Property(j, key) is var value && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static int? Number(JsonElement j, string key) =>
        Property(j, key) is var value && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;
    private static bool Flag(JsonElement j, string key) => Property(j, key).ValueKind == JsonValueKind.True;
    private static string? SafeUrl(string? value, string host) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == host ? value : null;
    private static string? SafeSpotifyImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && (uri.Host == "i.scdn.co" || uri.Host.EndsWith(".scdn.co", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".spotifycdn.com", StringComparison.OrdinalIgnoreCase)) ? value : null;
}
