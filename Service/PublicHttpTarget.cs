using System.Net;
using System.Net.Sockets;

namespace MiniProject_Everything_1.Services;

public static class PublicHttpTarget
{
    public static Uri Parse(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Enter a public JSON URL.");
        if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Enter a valid public HTTP or HTTPS URL without embedded credentials.");
        return uri;
    }

    public static async Task EnsurePublicAsync(Uri uri, CancellationToken ct)
    {
        var addresses = await ResolvePublicAddressesAsync(uri.DnsSafeHost, ct);
        if (addresses.Length == 0) throw new InvalidOperationException("The public host could not be resolved.");
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = ConnectPublicAsync
    };

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            return !(first == 0
                || first == 10
                || first == 100 && second is >= 64 and <= 127
                || first == 127
                || first == 169 && second == 254
                || first == 172 && second is >= 16 and <= 31
                || first == 192 && second is 0 or 168
                || first == 198 && second is 18 or 19
                || first >= 224);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
        if ((bytes[0] & 0xfe) == 0xfc) return false; // Unique local fc00::/7.
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return false; // Documentation range.
        return true;
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await ResolvePublicAddressesAsync(context.DnsEndPoint.Host, ct); }
        catch (InvalidOperationException ex) { throw new HttpRequestException(ex.Message, ex); }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                lastError = ex;
                socket.Dispose();
            }
        }
        throw new HttpRequestException("The public host could not be reached.", lastError);
    }

    private static async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            throw new InvalidOperationException("The public host could not be resolved.");
        }

        if (addresses.Length == 0) return [];
        if (addresses.Any(address => !IsPublicAddress(address)))
            throw new InvalidOperationException("Local, private, link-local, and reserved network addresses are not allowed.");
        return addresses;
    }
}
