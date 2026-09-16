using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed record MetricPoint(DateTimeOffset At, double MemoryMb, double PagedMemoryMb, int Threads, double CpuPercent);
public sealed record RequestTrace(DateTimeOffset At, string Method, string Path, int Status, long DurationMs, string TraceId);
public sealed record IncidentEvent(DateTimeOffset At, string Severity, string Source, string Message, bool Recovered = false);
public sealed record AuditEvent(DateTimeOffset At, string Actor, string Action, string Target, bool Success);
public sealed record ServiceCheckPoint(DateTimeOffset At, string Name, string Kind, bool Healthy, long LatencyMs, string Status);

public sealed class TelemetryStore
{
    private readonly object sync = new();
    private readonly Queue<MetricPoint> metrics;
    private readonly Queue<RequestTrace> traces = new();
    private readonly Queue<IncidentEvent> incidents;
    private readonly Queue<AuditEvent> audits;
    private readonly Queue<ServiceCheckPoint> serviceChecks;
    private readonly string metricsJournal, incidentsJournal, auditJournal, serviceJournal;

    public TelemetryStore(AppStorage storage)
    {
        metricsJournal = Path.Combine(storage.Path, "metrics.jsonl");
        incidentsJournal = Path.Combine(storage.Path, "incidents.jsonl");
        auditJournal = Path.Combine(storage.Path, "audit.jsonl");
        serviceJournal = Path.Combine(storage.Path, "service-checks.jsonl");
        metrics = Load<MetricPoint>(metricsJournal, 720);
        incidents = Load<IncidentEvent>(incidentsJournal, 300);
        audits = Load<AuditEvent>(auditJournal, 300);
        serviceChecks = Load<ServiceCheckPoint>(serviceJournal, 1000);
    }

    public IReadOnlyList<MetricPoint> Metrics => Snapshot(metrics);
    public IReadOnlyList<RequestTrace> Traces => Snapshot(traces);
    public IReadOnlyList<IncidentEvent> Incidents => Snapshot(incidents);
    public IReadOnlyList<AuditEvent> Audits => Snapshot(audits);
    public IReadOnlyList<ServiceCheckPoint> ServiceChecks => Snapshot(serviceChecks);

    public void Add(MetricPoint item) { lock (sync) { Enqueue(metrics, item, 720); Journal(metricsJournal, item, 720); } }
    public void Add(RequestTrace item) { lock (sync) Enqueue(traces, item, 300); }
    public void Add(IncidentEvent item) { lock (sync) { Enqueue(incidents, item, 300); Journal(incidentsJournal, item, 300); } }
    public void Add(AuditEvent item) { lock (sync) { Enqueue(audits, item, 300); Journal(auditJournal, item, 300); } }
    public void Add(ServiceCheckPoint item) { lock (sync) { Enqueue(serviceChecks, item, 1000); Journal(serviceJournal, item, 1000); } }

    private IReadOnlyList<T> Snapshot<T>(Queue<T> source) { lock (sync) return source.ToArray(); }
    private static void Enqueue<T>(Queue<T> source, T item, int maximum)
    { source.Enqueue(item); while (source.Count > maximum) source.Dequeue(); }
    private static Queue<T> Load<T>(string path, int maximum)
    {
        var queue = new Queue<T>();
        try
        {
            foreach (var line in File.ReadLines(path).TakeLast(maximum))
                if (JsonSerializer.Deserialize<T>(line) is { } item) queue.Enqueue(item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return queue;
    }
    private static void Journal<T>(string path, T item, int maximum)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(item) + Environment.NewLine);
            if (new FileInfo(path).Length <= 2_000_000) return;
            var compacted = File.ReadLines(path).TakeLast(maximum).ToArray();
            var temporary = path + ".tmp";
            File.WriteAllLines(temporary, compacted);
            File.Move(temporary, path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class TelemetryMiddleware(RequestDelegate next)
{
    private long lastLatencyAlertTicks;
    public async Task InvokeAsync(HttpContext context, TelemetryStore store, AlertSettings alerts, AlertDeliveryService delivery)
    {
        var watch = Stopwatch.StartNew();
        try { await next(context); }
        finally
        {
            if (!context.Request.Path.StartsWithSegments("/_framework")
                && !context.Request.Path.StartsWithSegments("/_blazor"))
            {
                store.Add(new RequestTrace(DateTimeOffset.UtcNow, context.Request.Method,
                    RedactPath(context.Request.Path), context.Response.StatusCode, watch.ElapsedMilliseconds,
                    Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier));
                if (alerts.RequestLatencyMs > 0 && watch.ElapsedMilliseconds >= alerts.RequestLatencyMs)
                {
                    var now = DateTimeOffset.UtcNow.UtcTicks;
                    var previous = Interlocked.Read(ref lastLatencyAlertTicks);
                    if (now - previous > TimeSpan.FromMinutes(1).Ticks
                        && Interlocked.CompareExchange(ref lastLatencyAlertTicks, now, previous) == previous)
                    {
                        var incident = new IncidentEvent(DateTimeOffset.UtcNow, "Warning", "HTTP latency",
                            $"{context.Request.Method} {RedactPath(context.Request.Path)} took {watch.ElapsedMilliseconds} ms.");
                        store.Add(incident);
                        await delivery.DeliverAsync(incident, context.RequestAborted);
                    }
                }
            }
        }
    }
    private static string RedactPath(PathString path) => path.StartsWithSegments("/signin-spotify") ? "/signin-spotify" : path.Value ?? "/";
}

public sealed class MetricsCollector(SystemMetricsService metrics, DiskStorageService disks, TelemetryStore store,
    AlertSettings alerts, AlertDeliveryService delivery, ILogger<MetricsCollector> logger) : BackgroundService
{
    private bool memoryAlert, diskAlert;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sample = metrics.GetLiveMetrics();
                store.Add(new MetricPoint(sample.CheckedAt, sample.WorkingMemoryMb, sample.PagedMemoryMb,
                    sample.ActiveThreads, sample.CpuPercent));
                memoryAlert = await Evaluate("Memory", sample.WorkingMemoryMb >= alerts.MemoryMb, memoryAlert,
                    $"Process memory is {sample.WorkingMemoryMb:F1} MB (threshold {alerts.MemoryMb:F0} MB).", stoppingToken);
                var highest = disks.GetReadyDrives().Select(x => x.UsedPercentage).DefaultIfEmpty().Max();
                diskAlert = await Evaluate("Disk", highest >= alerts.DiskUsedPercent, diskAlert,
                    $"A disk is {highest:F1}% used (threshold {alerts.DiskUsedPercent:F0}%).", stoppingToken);
            }
            catch (Exception ex) { logger.LogDebug("Metric collection skipped: {Type}", ex.GetType().Name); }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task<bool> Evaluate(string source, bool active, bool state, string message, CancellationToken ct)
    {
        if (active == state) return state;
        var incident = new IncidentEvent(DateTimeOffset.UtcNow, active ? "Warning" : "Info", source,
            active ? message : $"{source} returned below its configured threshold.", !active);
        store.Add(incident);
        if (active) await delivery.DeliverAsync(incident, ct);
        return active;
    }
}

public sealed class AlertDeliveryService(AlertSettings alerts, IHttpClientFactory clients, ILogger<AlertDeliveryService> logger)
{
    public async Task DeliverAsync(IncidentEvent incident, CancellationToken ct)
    {
        if (Uri.TryCreate(alerts.WebhookUrl, UriKind.Absolute, out var webhook) && webhook.Scheme == Uri.UriSchemeHttps)
        {
            try
            {
                using var response = await clients.CreateClient("Alerts").PostAsJsonAsync(webhook, incident, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            { logger.LogWarning("Alert webhook delivery failed: {Type}", ex.GetType().Name); }
        }
        if (!alerts.Smtp.IsConfigured) return;
        try
        {
            using var client = new SmtpClient(alerts.Smtp.Host!, alerts.Smtp.Port) { EnableSsl = alerts.Smtp.EnableSsl };
            if (!string.IsNullOrEmpty(alerts.Smtp.Username)) client.Credentials = new NetworkCredential(alerts.Smtp.Username, alerts.Smtp.Password);
            using var mail = new MailMessage(alerts.Smtp.From!, alerts.Smtp.To!, $"DevPulse alert: {incident.Source}", incident.Message);
            await client.SendMailAsync(mail, ct);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        { logger.LogWarning("Alert email delivery failed: {Type}", ex.GetType().Name); }
    }
}

public sealed record DeploymentInfo(string Version, string Environment, string Commit, DateTimeOffset StartedAt,
    TimeSpan Uptime, string Framework, string Machine);
public sealed class DeploymentInfoService(IHostEnvironment environment)
{
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    public DeploymentInfo Get() => new(
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown", environment.EnvironmentName,
        Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "not supplied", started,
        DateTimeOffset.UtcNow - started, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Environment.MachineName);
}
