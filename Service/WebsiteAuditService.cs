using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniProject_Everything_1.Services;

public sealed record WebsiteResourceResult(string Url, string Kind, int Status, bool Ok, long Bytes,
    long DurationMs, string ContentType, string? Issue);
public sealed record WebsiteAuditFinding(string Area, string Severity, string Message, string? Url = null);
public sealed record WebsitePerformanceResult(long DnsMs, long ResponseMs, long Bytes, string CacheControl,
    string ContentEncoding, string Protocol);
public sealed record WebsiteAuditReport(string Target, DateTimeOffset CheckedAt, int Score,
    WebsitePerformanceResult Performance, IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<WebsiteResourceResult> Resources, IReadOnlyList<WebsiteAuditFinding> Findings,
    int JsonFiles, int BrokenResources);
public sealed record WebsiteMonitorSnapshot(string Url, bool Healthy, int Status, long DurationMs,
    DateTimeOffset CheckedAt, string Message);

public sealed class WebsiteAuditService(IHttpClientFactory clients, OperationsSettings operations,
    WebsiteAuditSettings settings)
{
    private const int MaxTextBytes = 2_000_000;
    private static readonly Regex AssetRegex = new("(?:href|src)\\s*=\\s*[\"']([^\"'#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FetchRegex = new("fetch\\(\\s*[\"']([^\"']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CssUrlRegex = new("url\\(\\s*[\"']?([^\"')]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public string DefaultUrl => settings.DefaultUrl ?? settings.MonitoredUrls.FirstOrDefault() ?? "";

    public async Task<WebsiteAuditReport> AuditAsync(string value, CancellationToken ct)
    {
        var target = PublicHttpTarget.Parse(value);
        var trustedOrigin = IsTrustedOrigin(target);
        if (!trustedOrigin) await PublicHttpTarget.EnsurePublicAsync(target, ct);

        var maxResources = Math.Clamp(settings.MaxResources, 10, 150);
        var queue = new Queue<(Uri Uri, int Depth, string Kind)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resources = new List<WebsiteResourceResult>();
        var findings = new List<WebsiteAuditFinding>();
        var documents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rootHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rootPerformance = new WebsitePerformanceResult(0, 0, 0, "", "", "");
        string rootHtml = "";

        queue.Enqueue((WithoutFragment(target), 0, "Page"));
        queue.Enqueue((new Uri(target, "/robots.txt"), 1, "Robots"));
        queue.Enqueue((new Uri(target, "/sitemap.xml"), 1, "Sitemap"));

        while (queue.Count > 0 && resources.Count < maxResources)
        {
            ct.ThrowIfCancellationRequested();
            var entry = queue.Dequeue();
            var key = entry.Uri.AbsoluteUri;
            if (!visited.Add(key)) continue;
            var sameOrigin = SameOrigin(target, entry.Uri);
            var trusted = trustedOrigin && sameOrigin;
            try
            {
                var fetched = await FetchAsync(entry.Uri, trusted, ct);
                var kind = Classify(entry.Uri, fetched.ContentType, entry.Kind);
                var ok = fetched.Status is >= 200 and < 400;
                var issue = ok ? null : fetched.Status == 0 ? "Request failed" : $"HTTP {fetched.Status}";
                resources.Add(new(key, kind, fetched.Status, ok, fetched.Bytes, fetched.DurationMs, fetched.ContentType, issue));
                if (!ok && kind is not ("Robots" or "Sitemap"))
                    findings.Add(new("Links", "Error", $"{kind} returned {(fetched.Status == 0 ? "a connection error" : $"HTTP {fetched.Status}")}.", key));

                if (entry.Depth == 0)
                {
                    rootHtml = fetched.Body;
                    foreach (var header in fetched.Headers) rootHeaders[header.Key] = header.Value;
                    rootPerformance = new(fetched.DnsMs, fetched.DurationMs, fetched.Bytes,
                        Header(fetched.Headers, "Cache-Control"), Header(fetched.Headers, "Content-Encoding"), fetched.Protocol);
                }

                if (kind == "JSON" && ok)
                {
                    documents[key] = fetched.Body;
                    ValidateJson(entry.Uri, fetched.Body, findings);
                }

                if (!ok || string.IsNullOrEmpty(fetched.Body)) continue;
                IEnumerable<string> discovered = kind switch
                {
                    "Page" => AssetRegex.Matches(fetched.Body).Select(x => x.Groups[1].Value),
                    "JavaScript" => FetchRegex.Matches(fetched.Body).Select(x => x.Groups[1].Value),
                    "Stylesheet" => CssUrlRegex.Matches(fetched.Body).Select(x => x.Groups[1].Value),
                    _ => []
                };
                foreach (var raw in discovered)
                {
                    if (!TryResolve(entry.Uri, raw, out var uri) || visited.Contains(uri.AbsoluteUri)) continue;
                    var childSameOrigin = SameOrigin(target, uri);
                    if (!childSameOrigin && entry.Depth > 0) continue;
                    if (queue.Count + resources.Count >= maxResources * 2) break;
                    queue.Enqueue((uri, childSameOrigin ? entry.Depth + 1 : 99, GuessKind(uri)));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && ex is not OperationCanceledException)
            {
                resources.Add(new(key, entry.Kind, 0, false, 0, 0, "", "Request failed"));
                findings.Add(new("Links", "Error", "The resource could not be checked.", key));
            }
        }

        AuditHeaders(target, rootHeaders, findings);
        AuditSeo(target, rootHtml, resources, findings);
        AuditStaticAccessibility(rootHtml, findings);
        AuditCaching(rootHeaders, findings);
        if (resources.Count >= maxResources)
            findings.Add(new("Crawler", "Warning", $"The crawl reached its {maxResources}-resource safety limit."));
        var broken = resources.Count(x => !x.Ok && x.Kind is not ("Robots" or "Sitemap"));
        var score = Math.Clamp(100 - findings.Sum(x => x.Severity == "Error" ? 8 : x.Severity == "Warning" ? 3 : 0), 0, 100);
        return new(target.AbsoluteUri, DateTimeOffset.UtcNow, score, rootPerformance, rootHeaders,
            resources, findings, documents.Count, broken);
    }

    public async Task<WebsiteMonitorSnapshot> CheckUptimeAsync(string value, CancellationToken ct)
    {
        var uri = PublicHttpTarget.Parse(value);
        var trusted = IsTrustedOrigin(uri);
        if (!trusted) await PublicHttpTarget.EnsurePublicAsync(uri, ct);
        try
        {
            var result = await FetchAsync(uri, trusted, ct, readBody: false);
            var healthy = result.Status is >= 200 and < 400;
            return new(uri.AbsoluteUri, healthy, result.Status, result.DurationMs, DateTimeOffset.UtcNow,
                healthy ? "Available" : $"HTTP {result.Status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        { return new(uri.AbsoluteUri, false, 0, 0, DateTimeOffset.UtcNow, "Unavailable"); }
    }

    private async Task<FetchedResource> FetchAsync(Uri uri, bool trusted, CancellationToken ct, bool readBody = true)
    {
        if (!trusted) await PublicHttpTarget.EnsurePublicAsync(uri, ct);
        var dns = Stopwatch.StartNew();
        if (!IPAddress.TryParse(uri.DnsSafeHost, out _)) await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        dns.Stop();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("DevPulse-WebsiteAudit/1.0");
        var watch = Stopwatch.StartNew();
        using var response = await clients.CreateClient(trusted ? "WebsiteAudit" : "PublicWebsiteAudit")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var headers = response.Headers.Concat(response.Content.Headers)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => string.Join(", ", x.SelectMany(v => v.Value)), StringComparer.OrdinalIgnoreCase);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var shouldRead = readBody && IsText(uri, contentType);
        var body = shouldRead ? await ReadTextAsync(response.Content, MaxTextBytes, ct) : "";
        watch.Stop();
        var bytes = response.Content.Headers.ContentLength ?? (shouldRead ? Encoding.UTF8.GetByteCount(body) : 0);
        return new((int)response.StatusCode, body, bytes, watch.ElapsedMilliseconds, dns.ElapsedMilliseconds,
            contentType, response.Version.ToString(), headers);
    }

    private bool IsTrustedOrigin(Uri uri) => operations.ApprovedHosts.Contains(uri.DnsSafeHost, StringComparer.OrdinalIgnoreCase)
        || operations.ApprovedUrls.Any(value => Uri.TryCreate(value, UriKind.Absolute, out var approved)
            && approved.Scheme == uri.Scheme && approved.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)
            && approved.Port == uri.Port);

    private static async Task<string> ReadTextAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) return "";
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920]; var total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (read > limit - total) return "";
            await output.WriteAsync(buffer.AsMemory(0, read), ct); total += read;
        }
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(content.Headers.ContentType?.CharSet?.Trim('"') ?? "utf-8"); }
        catch { encoding = Encoding.UTF8; }
        return encoding.GetString(output.ToArray());
    }

    private static void AuditHeaders(Uri target, IReadOnlyDictionary<string, string> headers, List<WebsiteAuditFinding> findings)
    {
        void Missing(string name, string detail) { if (!headers.ContainsKey(name)) findings.Add(new("Security", "Warning", detail, target.AbsoluteUri)); }
        if (target.Scheme == "https") Missing("Strict-Transport-Security", "HSTS is missing.");
        Missing("Content-Security-Policy", "Content-Security-Policy is missing.");
        Missing("X-Content-Type-Options", "X-Content-Type-Options is missing.");
        Missing("Referrer-Policy", "Referrer-Policy is missing.");
        Missing("Permissions-Policy", "Permissions-Policy is missing.");
        if (!headers.ContainsKey("X-Frame-Options") && (!headers.TryGetValue("Content-Security-Policy", out var csp) || !csp.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("Security", "Warning", "Frame embedding protection is missing.", target.AbsoluteUri));
        if (headers.TryGetValue("Access-Control-Allow-Origin", out var cors) && cors.Trim() == "*")
            findings.Add(new("Security", "Info", "The response permits cross-origin reads from any origin.", target.AbsoluteUri));
    }

    private static void AuditCaching(IReadOnlyDictionary<string, string> headers, List<WebsiteAuditFinding> findings)
    {
        if (!headers.TryGetValue("Cache-Control", out var cache)) findings.Add(new("Caching", "Warning", "Cache-Control is missing."));
        else if (!cache.Contains("max-age", StringComparison.OrdinalIgnoreCase) && !cache.Contains("no-store", StringComparison.OrdinalIgnoreCase))
            findings.Add(new("Caching", "Info", "Cache-Control does not define max-age or no-store."));
        if (!headers.ContainsKey("ETag") && !headers.ContainsKey("Last-Modified"))
            findings.Add(new("Caching", "Info", "No ETag or Last-Modified validator was returned."));
    }

    private static void AuditSeo(Uri target, string html, IReadOnlyList<WebsiteResourceResult> resources, List<WebsiteAuditFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(html)) { findings.Add(new("SEO", "Error", "The entry page could not be parsed as HTML.")); return; }
        var title = WebUtility.HtmlDecode(TitleRegex.Match(html).Groups[1].Value).Trim();
        if (string.IsNullOrWhiteSpace(title)) findings.Add(new("SEO", "Error", "The page has no title."));
        else if (title.Length is < 10 or > 70) findings.Add(new("SEO", "Warning", $"The title length is {title.Length}; aim for 10–70 characters."));
        if (!Regex.IsMatch(html, "<meta[^>]+name=[\"']description[\"']", RegexOptions.IgnoreCase))
            findings.Add(new("SEO", "Warning", "A meta description is missing."));
        if (!Regex.IsMatch(html, "<html[^>]+lang=[\"'][^\"']+[\"']", RegexOptions.IgnoreCase))
            findings.Add(new("SEO", "Warning", "The HTML language attribute is missing."));
        if (!Regex.IsMatch(html, "<meta[^>]+name=[\"']viewport[\"']", RegexOptions.IgnoreCase))
            findings.Add(new("SEO", "Warning", "The viewport meta tag is missing."));
        var h1Count = Regex.Matches(html, "<h1(?:\\s|>)", RegexOptions.IgnoreCase).Count;
        if (h1Count != 1) findings.Add(new("SEO", "Warning", $"Expected one H1 element but found {h1Count}."));
        if (!Regex.IsMatch(html, "<link[^>]+rel=[\"']canonical[\"']", RegexOptions.IgnoreCase))
            findings.Add(new("SEO", "Info", "A canonical URL is not declared."));
        if (resources.Any(x => x.Kind == "Robots" && !x.Ok)) findings.Add(new("SEO", "Info", "robots.txt was not found.", new Uri(target, "/robots.txt").AbsoluteUri));
        if (resources.Any(x => x.Kind == "Sitemap" && !x.Ok)) findings.Add(new("SEO", "Info", "sitemap.xml was not found.", new Uri(target, "/sitemap.xml").AbsoluteUri));
    }

    private static void AuditStaticAccessibility(string html, List<WebsiteAuditFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        var images = Regex.Matches(html, "<img\\b[^>]*>", RegexOptions.IgnoreCase);
        var missingAlt = images.Count(x => !Regex.IsMatch(x.Value, "\\balt\\s*=", RegexOptions.IgnoreCase));
        if (missingAlt > 0) findings.Add(new("Accessibility", "Warning", $"{missingAlt} image element(s) have no alt attribute."));
        var unnamedButtons = Regex.Matches(html, "<button\\b([^>]*)>\\s*(?:<[^>]+>\\s*)*</button>", RegexOptions.IgnoreCase)
            .Count(x => !Regex.IsMatch(x.Groups[1].Value, "aria-label|title", RegexOptions.IgnoreCase));
        if (unnamedButtons > 0) findings.Add(new("Accessibility", "Warning", $"{unnamedButtons} button(s) may not have an accessible name."));
    }

    private static void ValidateJson(Uri uri, string text, List<WebsiteAuditFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(text)) { findings.Add(new("JSON", "Warning", "JSON content exceeded the inspection limit.", uri.AbsoluteUri)); return; }
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var file = Path.GetFileName(uri.AbsolutePath).ToLowerInvariant();
            if (file is "data.json" or "videodata.json" or "photodata.json" && root.ValueKind != JsonValueKind.Array)
                findings.Add(new("JSON", "Error", $"{file} should contain a top-level array.", uri.AbsoluteUri));
            if (file == "projectdata.json" && root.ValueKind != JsonValueKind.Object)
                findings.Add(new("JSON", "Error", "projectdata.json should contain a top-level object.", uri.AbsoluteUri));
            if (root.ValueKind == JsonValueKind.Array)
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var index = 0;
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) { index++; continue; }
                    if (item.TryGetProperty("id", out var id) && !ids.Add(id.ToString()))
                        findings.Add(new("JSON", "Error", $"Duplicate id '{id}' at item {index}.", uri.AbsoluteUri));
                    if (file == "data.json") Require(item, ["id", "name", "category", "description", "processor"], file, index, uri, findings);
                    if (file is "videodata.json" or "photodata.json") Require(item, ["device_name"], file, index, uri, findings);
                    index++;
                }
            }
        }
        catch (JsonException ex) { findings.Add(new("JSON", "Error", $"Invalid JSON near byte {ex.BytePositionInLine}.", uri.AbsoluteUri)); }
    }

    private static void Require(JsonElement item, string[] names, string file, int index, Uri uri, List<WebsiteAuditFinding> findings)
    {
        foreach (var name in names)
            if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                findings.Add(new("JSON", "Warning", $"{file} item {index} is missing '{name}'.", uri.AbsoluteUri));
    }

    private static bool TryResolve(Uri source, string raw, out Uri uri)
    {
        uri = null!; raw = WebUtility.HtmlDecode(raw.Trim());
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('#') || raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(source, raw, out var resolved) || resolved.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(resolved.UserInfo)) return false;
        uri = WithoutFragment(resolved); return true;
    }
    private static Uri WithoutFragment(Uri uri) { var builder = new UriBuilder(uri) { Fragment = "" }; return builder.Uri; }
    private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
    private static bool IsText(Uri uri, string contentType) => contentType.Contains("html") || contentType.Contains("json")
        || contentType.Contains("javascript") || contentType.Contains("css") || contentType.StartsWith("text/")
        || new[] { ".html", ".htm", ".json", ".js", ".css", ".xml", ".txt" }.Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase);
    private static string GuessKind(Uri uri) => Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
    { ".json" => "JSON", ".js" => "JavaScript", ".css" => "Stylesheet", ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" => "Image", ".pdf" => "Document", _ => "Link" };
    private static string Classify(Uri uri, string contentType, string fallback)
    {
        if (fallback is "Robots" or "Sitemap") return fallback;
        return contentType switch
        { var x when x.Contains("html") => "Page", var x when x.Contains("json") => "JSON", var x when x.Contains("javascript") => "JavaScript", var x when x.Contains("css") => "Stylesheet", var x when x.StartsWith("image/") => "Image", _ => fallback == "Link" ? GuessKind(uri) : fallback };
    }
    private static string Header(IReadOnlyDictionary<string, string> headers, string name) => headers.TryGetValue(name, out var value) ? value : "";

    private sealed record FetchedResource(int Status, string Body, long Bytes, long DurationMs, long DnsMs,
        string ContentType, string Protocol, IReadOnlyDictionary<string, string> Headers);
}

public sealed class WebsiteMonitorState
{
    private readonly object sync = new();
    private readonly Dictionary<string, WebsiteMonitorSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<WebsiteMonitorSnapshot> Snapshots { get { lock (sync) return snapshots.Values.OrderBy(x => x.Url).ToArray(); } }
    public void Set(WebsiteMonitorSnapshot snapshot) { lock (sync) snapshots[snapshot.Url] = snapshot; }
}

public sealed class WebsiteMonitorService(WebsiteAuditService audits, WebsiteAuditSettings settings,
    WebsiteMonitorState state, TelemetryStore telemetry, AlertDeliveryService delivery,
    ILogger<WebsiteMonitorService> logger) : BackgroundService
{
    private readonly Dictionary<string, bool> previous = new(StringComparer.OrdinalIgnoreCase);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.MonitoredUrls.Length == 0 || settings.MonitorIntervalSeconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(settings.MonitorIntervalSeconds, 60, 86400)));
        do
        {
            foreach (var url in settings.MonitoredUrls.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var snapshot = await audits.CheckUptimeAsync(url, stoppingToken); state.Set(snapshot);
                    telemetry.Add(new ServiceCheckPoint(snapshot.CheckedAt, snapshot.Url, "Website", snapshot.Healthy, snapshot.DurationMs, snapshot.Message));
                    if (previous.TryGetValue(snapshot.Url, out var before) && before != snapshot.Healthy)
                    {
                        var incident = new IncidentEvent(snapshot.CheckedAt, snapshot.Healthy ? "Info" : "Warning", "Website monitor",
                            snapshot.Healthy ? $"{snapshot.Url} recovered." : $"{snapshot.Url} became unavailable.", snapshot.Healthy);
                        telemetry.Add(incident); if (!snapshot.Healthy) await delivery.DeliverAsync(incident, stoppingToken);
                    }
                    previous[snapshot.Url] = snapshot.Healthy;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogWarning("Website monitor skipped {Url}: {Type}", url, ex.GetType().Name); }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
