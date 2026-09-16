using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace MiniProject_Everything_1.Services;

public sealed record PingResult(string Host, bool Reachable, long LatencyMs, string Status);
public sealed record DnsResult(string Host, IReadOnlyList<string> Addresses, long DurationMs, string? Error);
public sealed record TlsResult(string Host, bool Valid, string Subject, string Issuer, DateTimeOffset? Expires,
    int DaysRemaining, long DurationMs, string? Error);
public sealed record WatchResult(string Name, string Kind, bool Healthy, long LatencyMs, string Status);
public sealed record DatabaseHealthResult(string Name, string Kind, bool Healthy, long LatencyMs, string Status);

public sealed class NetworkDiagnosticsService(OperationsSettings settings, IHttpClientFactory clients,
    TelemetryStore? telemetry = null, AlertDeliveryService? delivery = null)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> previousHealth = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Hosts => settings.ApprovedHosts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<string> Urls => settings.ApprovedUrls.Where(IsSafeHttpUrl).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<int> Ports => settings.ApprovedPorts.Where(p => p is > 0 and <= 65535).Distinct().Order().ToArray();

    public async Task<IReadOnlyList<PingResult>> PingAllAsync(CancellationToken ct)
    {
        var results = await Task.WhenAll(Hosts.Select(host => PingAsync(host, ct)));
        return results;
    }

    public async Task<PingResult> PingAsync(string host, CancellationToken ct)
    {
        EnsureHost(host);
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000).WaitAsync(ct);
            return new(host, reply.Status == IPStatus.Success, reply.RoundtripTime, reply.Status.ToString());
        }
        catch (Exception ex) when (ex is PingException or SocketException)
        { return new(host, false, 0, "Unavailable"); }
    }

    public async Task<DnsResult> ResolveAsync(string host, CancellationToken ct)
    {
        EnsureHost(host);
        var watch = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            return new(host, addresses.Select(x => x.ToString()).ToArray(), watch.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        { return new(host, [], watch.ElapsedMilliseconds, "DNS lookup failed."); }
    }

    public async Task<TlsResult> InspectTlsAsync(string host, CancellationToken ct)
    {
        EnsureHost(host);
        var watch = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, ct);
            using var ssl = new SslStream(tcp.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            { TargetHost = host, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, ct);
            if (ssl.RemoteCertificate is null) return new(host, false, "", "", null, 0, watch.ElapsedMilliseconds, "No certificate was returned.");
            using var certificate = new X509Certificate2(ssl.RemoteCertificate);
            var expires = new DateTimeOffset(certificate.NotAfter);
            return new(host, expires > DateTimeOffset.Now, certificate.GetNameInfo(X509NameType.SimpleName, false),
                certificate.Issuer, expires, (int)Math.Floor((expires - DateTimeOffset.Now).TotalDays), watch.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or OperationCanceledException && !ct.IsCancellationRequested)
        { return new(host, false, "", "", null, 0, watch.ElapsedMilliseconds, "TLS connection failed."); }
    }

    public async Task<IReadOnlyList<WatchResult>> CheckWatchlistAsync(CancellationToken ct)
    {
        var http = Urls.Select(CheckUrlAsync).Select(f => f(ct));
        var tcp = Hosts.SelectMany(h => Ports.Select(p => CheckPortAsync(h, p, ct)));
        var results = await Task.WhenAll(http.Concat(tcp));
        foreach (var result in results)
        {
            telemetry?.Add(new ServiceCheckPoint(DateTimeOffset.UtcNow, result.Name, result.Kind,
                result.Healthy, result.LatencyMs, result.Status));
            IncidentEvent? incident = null;
            if (previousHealth.TryGetValue(result.Name, out var before) && before != result.Healthy)
                incident = new IncidentEvent(DateTimeOffset.UtcNow, result.Healthy ? "Info" : "Warning",
                    "Service watchlist", result.Healthy ? $"{result.Name} recovered." : $"{result.Name} became unavailable.", result.Healthy);
            else if (!previousHealth.ContainsKey(result.Name) && !result.Healthy)
                incident = new IncidentEvent(DateTimeOffset.UtcNow, "Warning", "Service watchlist", $"{result.Name} is unavailable.");
            if (incident is not null)
            {
                telemetry?.Add(incident);
                if (!incident.Recovered && delivery is not null) await delivery.DeliverAsync(incident, ct);
            }
            previousHealth[result.Name] = result.Healthy;
        }
        return results;
    }

    public async Task<IReadOnlyList<DatabaseHealthResult>> CheckDatabasesAsync(CancellationToken ct)
    {
        var tasks = settings.Databases.Where(x => IsApprovedHost(x.Host) && x.Port is > 0 and <= 65535)
            .Select(x => CheckDatabaseAsync(x, ct));
        return await Task.WhenAll(tasks);
    }

    private Func<CancellationToken, Task<WatchResult>> CheckUrlAsync(string url) => async ct =>
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("DevPulse/1.0");
            using var response = await clients.CreateClient("Diagnostics").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return new(url, "HTTP", response.IsSuccessStatusCode, watch.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        { return new(url, "HTTP", false, watch.ElapsedMilliseconds, "Unavailable"); }
    };

    private static async Task<WatchResult> CheckPortAsync(string host, int port, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(800);
        try
        {
            using var tcp = new TcpClient(); await tcp.ConnectAsync(host, port, timeout.Token);
            return new($"{host}:{port}", "TCP", true, watch.ElapsedMilliseconds, "Open");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        { return new($"{host}:{port}", "TCP", false, watch.ElapsedMilliseconds, "Closed"); }
    }

    private static async Task<DatabaseHealthResult> CheckDatabaseAsync(DatabaseTarget target, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(2000);
            await tcp.ConnectAsync(target.Host, target.Port, timeout.Token);
            if (target.Kind.Equals("Redis", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = "*1\r\n$4\r\nPING\r\n"u8.ToArray();
                await tcp.GetStream().WriteAsync(bytes, timeout.Token);
                var response = new byte[16]; var count = await tcp.GetStream().ReadAsync(response, timeout.Token);
                var pong = System.Text.Encoding.ASCII.GetString(response, 0, count).StartsWith("+PONG");
                return new(target.Name, target.Kind, pong, watch.ElapsedMilliseconds, pong ? "PONG" : "Unexpected response");
            }
            return new(target.Name, target.Kind, true, watch.ElapsedMilliseconds, "TCP connection accepted");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        { return new(target.Name, target.Kind, false, watch.ElapsedMilliseconds, "Unavailable"); }
    }

    private void EnsureHost(string host) { if (!IsApprovedHost(host)) throw new InvalidOperationException("Host is not in the owner allowlist."); }
    private bool IsApprovedHost(string host) => Hosts.Contains(host, StringComparer.OrdinalIgnoreCase);
    private static bool IsSafeHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
}

public sealed class WatchlistCollector(NetworkDiagnosticsService network, OperationsSettings settings,
    ILogger<WatchlistCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.WatchIntervalSeconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(settings.WatchIntervalSeconds, 30, 86400)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await network.CheckWatchlistAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning("Scheduled watchlist check failed: {Type}", ex.GetType().Name); }
    }
}
