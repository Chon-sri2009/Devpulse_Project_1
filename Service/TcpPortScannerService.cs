using System.Net.Sockets;
namespace MiniProject_Everything_1.Services;

public sealed class TcpPortScannerService
{
    private static readonly IReadOnlyDictionary<int, string> Ports = new Dictionary<int, string>
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
        var results = await Task.WhenAll(Ports.Select(port => ScanPortAsync(port.Key, port.Value, cancellationToken)));
        return results.OrderBy(result => result.Port).ToList();
    }
    public static async Task<TcpPortResult> ScanPortAsync(int port, string service, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return new(port, service, true, "Open");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(port, service, false, "Timed out"); }
        catch (SocketException ex)
        { return new(port, service, false, ex.SocketErrorCode == SocketError.ConnectionRefused ? "Closed" : "Unavailable"); }
    }
}
public record TcpPortResult(int Port, string Service, bool IsOpen, string Status);
