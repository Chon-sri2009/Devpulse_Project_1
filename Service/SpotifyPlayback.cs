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
public sealed record SpotifyTrack(string Uri, string Name, string Artists, string Album, string? Image, string? Link,
    int DurationMs, bool Explicit, bool Playable)
{
    public static bool IsSafeUri(string? value)
    {
        const string prefix = "spotify:track:";
        return value is { Length: > 14 } && value.StartsWith(prefix, StringComparison.Ordinal)
            && value[prefix.Length..].All(char.IsAsciiLetterOrDigit);
    }
}
public sealed record SpotifyTrackPage(IReadOnlyList<SpotifyTrack> Items, int Offset, int Limit, int Total,
    string Title, string? ContextUri = null)
{
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Offset + Items.Count < Total;
}
public sealed record SpotifyPlayback(string Title, string Creator, string? Image, string? Link,
    bool Playing, int ProgressMs, int DurationMs, SpotifyDevice? Device, IReadOnlySet<string> Disallowed,
    bool Shuffle, string Repeat)
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
            device.ValueKind == JsonValueKind.Object ? ParseDevice(device) : null, blocked,
            Flag(json, "shuffle_state"), Text(json, "repeat_state", "off"));
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
    public static SpotifyTrackPage ParseSearchTracks(JsonElement json, string query) =>
        ParseTrackContainer(Property(json, "tracks"), "Search: " + query, null, false, null, null);
    public static SpotifyTrackPage ParseSavedTracks(JsonElement json) =>
        ParseTrackContainer(json, "Liked songs", null, true, null, null);
    public static SpotifyTrackPage ParseAlbumTracks(JsonElement json, SpotifyAlbum album) =>
        ParseTrackContainer(json, album.Name, album.Uri, false, album.Name, album.Image);
    public static SpotifyTrackPage ParsePlaylistTracks(JsonElement json, SpotifyPlaylist playlist) =>
        ParseTrackContainer(json, playlist.Name, playlist.Uri, true, playlist.Name, playlist.Image);
    public static IReadOnlyList<SpotifyTrack> ParseQueue(JsonElement json)
    {
        var queue = Property(json, "queue");
        return queue.ValueKind == JsonValueKind.Array
            ? queue.EnumerateArray().Select(x => ParseTrack(x, null, null)).Where(x => x is not null).Cast<SpotifyTrack>().Take(20).ToList()
            : [];
    }
    private static SpotifyTrackPage ParseTrackContainer(JsonElement container, string title, string? contextUri,
        bool wrapped, string? fallbackAlbum, string? fallbackImage)
    {
        var items = Property(container, "items");
        var tracks = new List<SpotifyTrack>();
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var track = wrapped ? Property(item, "track") : item;
                var parsed = ParseTrack(track, fallbackAlbum, fallbackImage);
                if (parsed is not null) tracks.Add(parsed);
            }
        }
        return new(tracks, Number(container, "offset") ?? 0, Number(container, "limit") ?? tracks.Count,
            Number(container, "total") ?? tracks.Count, title, contextUri);
    }
    private static SpotifyTrack? ParseTrack(JsonElement track, string? fallbackAlbum, string? fallbackImage)
    {
        var uri = Text(track, "uri");
        if (!SpotifyTrack.IsSafeUri(uri)) return null;
        var artists = Property(track, "artists");
        var artistNames = artists.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artists.EnumerateArray().Select(x => Text(x, "name")).Where(x => x.Length > 0)) : "";
        var album = Property(track, "album");
        var images = Property(album, "images");
        var image = images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
            ? SafeSpotifyImage(Text(images[0], "url")) : SafeSpotifyImage(fallbackImage);
        var playableValue = Property(track, "is_playable");
        return new(uri, Text(track, "name", "Untitled track"), artistNames,
            Text(album, "name", fallbackAlbum ?? ""), image,
            SafeUrl(Text(Property(track, "external_urls"), "spotify"), "open.spotify.com"),
            Number(track, "duration_ms") ?? 0, Flag(track, "explicit"), playableValue.ValueKind != JsonValueKind.False);
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
