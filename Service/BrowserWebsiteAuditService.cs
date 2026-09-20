using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniProject_Everything_1.Services;

public sealed class BrowserAuditReport
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, double> Scores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BrowserAccessibilityIssue> AccessibilityViolations { get; set; } = [];
    public List<string> Journey { get; set; } = [];
    public List<string> ConsoleErrors { get; set; } = [];
    public List<string> FailedRequests { get; set; } = [];
    public BrowserVisualResult Visual { get; set; } = new();
    public long DurationMs { get; set; }
}
public sealed class BrowserAccessibilityIssue
{
    public string Id { get; set; } = "";
    public string Impact { get; set; } = "";
    public string Description { get; set; } = "";
    public int Nodes { get; set; }
}
public sealed class BrowserVisualResult
{
    public bool BaselineCreated { get; set; }
    public bool BaselineUpdated { get; set; }
    public double DifferencePercent { get; set; }
    public string? DesktopCurrent { get; set; }
    public string? DesktopBaseline { get; set; }
    public string? DesktopDiff { get; set; }
    public string? MobileCurrent { get; set; }
}

public sealed class BrowserWebsiteAuditService(WebsiteAuditSettings settings, IHostEnvironment environment, AppStorage storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string artifactDirectory = Path.Combine(storage.Path, "website-audit");
    private static readonly Regex SafeArtifact = new("^[a-z0-9-]+\\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public bool Enabled => settings.BrowserEnabled;

    public async Task<BrowserAuditReport> RunAsync(string value, bool updateBaseline, CancellationToken ct)
    {
        if (!settings.BrowserEnabled) return new() { Error = "Browser audits are disabled by configuration." };
        var uri = PublicHttpTarget.Parse(value);
        if (!IsConfiguredTarget(uri))
            return new() { Error = "Browser audits are limited to owner-configured website origins. Add this URL under WebsiteAudit:MonitoredUrls first." };
        await PublicHttpTarget.EnsurePublicAsync(uri, ct);
        if (!await gate.WaitAsync(0, ct)) return new() { Error = "Another browser audit is already running." };
        try
        {
            var script = Path.Combine(environment.ContentRootPath, "scripts", "website-browser-audit.mjs");
            if (!File.Exists(script)) return new() { Error = "The browser audit worker is not installed." };
            Directory.CreateDirectory(artifactDirectory);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant()[..16];
            var start = new ProcessStartInfo
            {
                FileName = settings.NodePath ?? Environment.GetEnvironmentVariable("WebsiteAudit__NodePath") ?? "node",
                WorkingDirectory = environment.ContentRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("--url"); start.ArgumentList.Add(uri.AbsoluteUri);
            start.ArgumentList.Add("--artifact-dir"); start.ArgumentList.Add(artifactDirectory);
            start.ArgumentList.Add("--key"); start.ArgumentList.Add(key);
            if (updateBaseline) start.ArgumentList.Add("--update-baseline");
            var chromium = settings.ChromiumPath ?? Environment.GetEnvironmentVariable("WebsiteAudit__ChromiumPath");
            if (!string.IsNullOrWhiteSpace(chromium)) { start.ArgumentList.Add("--chromium"); start.ArgumentList.Add(chromium); }

            using var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start()) return new() { Error = "The browser audit worker could not start." };
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { return new() { Error = "Node.js is not available on this deployment." }; }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.BrowserTimeoutSeconds, 30, 300)));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (ct.IsCancellationRequested) throw;
                return new() { Error = "The browser audit exceeded its time limit." };
            }
            var stdout = await stdoutTask; var stderr = await stderrTask;
            var json = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(json))
                return new() { Error = SafeWorkerError(stderr, process.ExitCode) };
            try
            {
                return JsonSerializer.Deserialize<BrowserAuditReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new() { Error = "The browser audit returned no result." };
            }
            catch (JsonException) { return new() { Error = SafeWorkerError(stderr, process.ExitCode) }; }
        }
        finally { gate.Release(); }
    }

    public string? GetArtifactPath(string name)
    {
        if (!SafeArtifact.IsMatch(name)) return null;
        var path = Path.GetFullPath(Path.Combine(artifactDirectory, name));
        var root = Path.GetFullPath(artifactDirectory) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    private static string SafeWorkerError(string stderr, int exitCode)
    {
        var detail = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(detail)) return $"Browser audit worker exited with code {exitCode}.";
        detail = detail.Length > 240 ? detail[..240] : detail;
        return $"Browser audit unavailable: {detail}";
    }

    private bool IsConfiguredTarget(Uri target)
    {
        var configured = settings.MonitoredUrls.Append(settings.DefaultUrl ?? "");
        return configured.Any(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == target.Scheme && uri.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == target.Port);
    }
}
