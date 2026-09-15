using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed record SpotifyDevice(string Id, string Name, bool Active, bool Restricted, bool SupportsVolume, int? Volume);
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
}
