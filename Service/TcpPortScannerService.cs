using System.Net.Sockets;
namespace MiniProject_Everything_1.Services;

public sealed class TcpPortScannerService(OperationsSettings settings)
{
    private static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [80] = "HTTP",
        [443] = "HTTPS",
        [5000] = "ASP.NET HTTP",
        [5001] = "ASP.NET HTTPS",
        [5173] = "Vite",
        [5214] = "DevPulse HTTP",
        [7133] = "DevPulse HTTPS",
        [8080] = "Alternative HTTP",
        [10000] = "Container HTTP"
    };
    public async Task<IReadOnlyList<TcpPortResult>> ScanLocalhostAsync(CancellationToken cancellationToken = default)
    {
        return await ScanAsync("127.0.0.1", cancellationToken);
    }
    public async Task<IReadOnlyList<TcpPortResult>> ScanAsync(string host, CancellationToken cancellationToken = default)
    {
        if (!settings.ApprovedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host is not in the owner allowlist.");
        var ports = settings.ApprovedPorts.Where(p => p is > 0 and <= 65535).Distinct();
        var results = await Task.WhenAll(ports.Select(port => ScanPortAsync(host, port,
            Labels.GetValueOrDefault(port, "Configured service"), cancellationToken)));
        return results.OrderBy(result => result.Port).ToList();
    }
    public static Task<TcpPortResult> ScanPortAsync(int port, string service, CancellationToken cancellationToken = default) =>
        ScanPortAsync("127.0.0.1", port, service, cancellationToken);
    public static async Task<TcpPortResult> ScanPortAsync(string host, int port, string service, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return new(port, service, true, "Open");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(port, service, false, "Timed out"); }
        catch (SocketException ex)
        { return new(port, service, false, ex.SocketErrorCode == SocketError.ConnectionRefused ? "Closed" : "Unavailable"); }
    }
}
public record TcpPortResult(int Port, string Service, bool IsOpen, string Status);
