using System.Net.Sockets;

namespace MiniProject_Everything_1.Services;

public sealed class TcpPortScannerService
{
    private static readonly IReadOnlyDictionary<int, string> Ports =
        new Dictionary<int, string>
        {
            [80] = "HTTP",
            [443] = "HTTPS",
            [5000] = "ASP.NET HTTP",
            [5001] = "ASP.NET HTTPS",
            [5173] = "Vite development server",
            [8080] = "Alternative HTTP"
        };

    public async Task<IReadOnlyList<TcpPortResult>> ScanLocalhostAsync()
    {
        var scans = Ports.Select(port =>
            ScanPortAsync("127.0.0.1", port.Key, port.Value));

        var results = await Task.WhenAll(scans);

        return results.OrderBy(result => result.Port).ToList();
    }

    private static async Task<TcpPortResult> ScanPortAsync(
        string host,
        int port,
        string service)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(800));

        try
        {
            await client.ConnectAsync(host, port, timeout.Token);

            return new TcpPortResult(port, service, true);
        }
        catch
        {
            return new TcpPortResult(port, service, false);
        }
    }
}

public record TcpPortResult(
    int Port,
    string Service,
    bool IsOpen);
