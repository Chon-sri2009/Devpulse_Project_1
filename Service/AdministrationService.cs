using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed class AdminAccessService(AdminSettings settings)
{
    private readonly byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(settings.Password ?? ""));
    public bool IsConfigured => settings.IsConfigured;
    public string Username => settings.Username;
    public bool Validate(string? username, string? password)
    {
        if (!IsConfigured || !string.Equals(username, settings.Username, StringComparison.Ordinal)) return false;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? ""));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}

public sealed record MaintenanceSnapshot(bool Enabled, string Message, DateTimeOffset? Until, DateTimeOffset ChangedAt);
public sealed class MaintenanceState(AppStorage storage)
{
    private readonly object sync = new();
    private readonly string path = Path.Combine(storage.Path, "maintenance.json");
    private MaintenanceSnapshot current = Load(Path.Combine(storage.Path, "maintenance.json"));
    public MaintenanceSnapshot Current { get { lock (sync) return current; } }
    public void Set(bool enabled, string? message, DateTimeOffset? until)
    {
        lock (sync)
        {
            current = new(enabled, string.IsNullOrWhiteSpace(message) ? "DevPulse is undergoing scheduled maintenance." : message.Trim(), until, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(current));
        }
    }
    private static MaintenanceSnapshot Load(string path)
    {
        try { return JsonSerializer.Deserialize<MaintenanceSnapshot>(File.ReadAllText(path)) ?? new(false, "", null, DateTimeOffset.UtcNow); }
        catch { return new(false, "", null, DateTimeOffset.UtcNow); }
    }
}

public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MaintenanceState state)
    {
        var maintenance = state.Current;
        if (maintenance.Enabled && maintenance.Until is { } until && until <= DateTimeOffset.UtcNow)
        { state.Set(false, maintenance.Message, null); maintenance = state.Current; }
        if (!maintenance.Enabled || context.Request.Path.StartsWithSegments("/admin")
            || context.Request.Path.StartsWithSegments("/healthz") || context.Request.Path.StartsWithSegments("/_framework")
            || Path.HasExtension(context.Request.Path)) { await next(context); return; }
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.RetryAfter = "300";
        await context.Response.WriteAsync($$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Maintenance | DevPulse</title>
            <style>body{font:16px system-ui;background:#f4f7fb;color:#172033;display:grid;place-items:center;min-height:100vh;margin:0}main{max-width:600px;background:white;padding:3rem;border-radius:18px;box-shadow:0 15px 45px #18233c22}h1{margin-top:0}</style></head>
            <body><main><p>DEVPULSE</p><h1>Scheduled maintenance</h1><p>{{System.Net.WebUtility.HtmlEncode(maintenance.Message)}}</p><small>Please try again later.</small></main></body></html>
            """, context.RequestAborted);
    }
}

public sealed class DiagnosticsGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, DiagnosticsSettings settings)
    {
        var restricted = context.Request.Path.StartsWithSegments("/operations")
            || context.Request.Path.StartsWithSegments("/telemetry") || context.Request.Path.StartsWithSegments("/tools")
            || context.Request.Path.StartsWithSegments("/website-audit")
            || context.Request.Path.StartsWithSegments("/api-runner")
            || context.Request.Path.StartsWithSegments("/dns-email");
        if (!settings.Enabled && restricted)
        { context.Response.StatusCode = StatusCodes.Status404NotFound; await context.Response.WriteAsync("Diagnostics are disabled."); return; }
        await next(context);
    }
}

public sealed class AdministrationService(DiagnosticToolsService tools, TelemetryStore telemetry,
    DeploymentInfoService deployment, MaintenanceState maintenance, AdminSettings settings)
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit",
        "services", "lsass", "winlogon", "svchost", "dwm"
    };
    public bool ProcessTerminationEnabled => settings.AllowProcessTermination;
    public IReadOnlyList<ProcessInfo> Processes() => tools.GetProcesses();
    public bool CanTerminate(ProcessInfo process) => settings.AllowProcessTermination
        && process.Id > 4 && process.Id != Environment.ProcessId && !ProtectedProcessNames.Contains(process.Name);
    public bool Terminate(int processId)
    {
        if (!settings.AllowProcessTermination || processId <= 4 || processId == Environment.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (ProtectedProcessNames.Contains(process.ProcessName)) return false;
            process.Kill(false);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return false; }
    }

    public byte[] ExportDiagnostics()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(zip, "deployment.json", deployment.Get());
            Write(zip, "metrics.json", telemetry.Metrics);
            Write(zip, "incidents.json", telemetry.Incidents);
            Write(zip, "request-traces.json", telemetry.Traces);
            Write(zip, "service-checks.json", telemetry.ServiceChecks);
            Write(zip, "audit.json", telemetry.Audits);
            Write(zip, "maintenance.json", maintenance.Current);
        }
        return output.ToArray();
    }
    private static void Write<T>(ZipArchive zip, string name, T value)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
