using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MiniProject_Everything_1.Services;

public sealed class ApiRequestDefinition
{
    public string Name { get; set; } = "New request";
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public string Headers { get; set; } = "";
    public string Body { get; set; } = "";
    public int? ExpectedStatus { get; set; } = 200;
    public string ExpectedContains { get; set; } = "";

    public ApiRequestDefinition Copy() => new()
    {
        Name = Name,
        Method = Method,
        Url = Url,
        ContentType = ContentType,
        Headers = Headers,
        Body = Body,
        ExpectedStatus = ExpectedStatus,
        ExpectedContains = ExpectedContains
    };
}

public sealed record ApiResponseResult(string Name, string Method, string Url, int StatusCode,
    string Reason, long DurationMs, string ContentType, long BytesRead, string Headers, string Body,
    bool Passed, IReadOnlyList<string> Assertions, bool Truncated);

public sealed class ApiCollectionService(OperationsSettings settings, IHttpClientFactory clients)
{
    public const int MaxRequestsPerRun = 10;
    public const int MaxRequestBodyBytes = 256 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase)
        { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Host", "Content-Length", "Connection", "Transfer-Encoding", "Proxy-Authorization", "Authorization", "Cookie" };
    private static readonly HashSet<string> HiddenResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Set-Cookie", "Proxy-Authenticate", "WWW-Authenticate", "Authentication-Info" };

    public async Task<ApiResponseResult> ExecuteAsync(ApiRequestDefinition definition,
        string? authorizationValue, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var method = definition.Method?.Trim().ToUpperInvariant() ?? "";
        if (!Methods.Contains(method)) throw new InvalidOperationException("Choose a supported HTTP method.");
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Trim().Length > 80)
            throw new InvalidOperationException("Request names must contain 1 to 80 characters.");

        var uri = PublicHttpTarget.Parse(definition.Url);
        var approved = IsApprovedUrl(uri);
        if (!approved && method is not ("GET" or "HEAD" or "OPTIONS"))
            throw new InvalidOperationException("POST, PUT, PATCH, and DELETE require the exact URL in Operations:ApprovedUrls.");
        if (!approved) await PublicHttpTarget.EnsurePublicAsync(uri, ct);
        var bodyBytes = Encoding.UTF8.GetByteCount(definition.Body ?? "");
        if (bodyBytes > MaxRequestBodyBytes)
            throw new InvalidOperationException("Request body exceeds the 256 KB limit.");
        if (definition.ExpectedStatus is < 100 or > 599)
            throw new InvalidOperationException("Expected status must be between 100 and 599, or left empty.");
        if ((definition.ExpectedContains?.Length ?? 0) > 256)
            throw new InvalidOperationException("The response assertion must be 256 characters or fewer.");

        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        request.Headers.UserAgent.ParseAdd("DevPulse-ApiRunner/1.0");
        AddHeaders(request, definition.Headers);
        if (!string.IsNullOrWhiteSpace(authorizationValue))
        {
            var auth = authorizationValue.Trim();
            if (auth.Length > 4096 || auth.Contains('\r') || auth.Contains('\n'))
                throw new InvalidOperationException("The authorization value is invalid.");
            request.Headers.TryAddWithoutValidation("Authorization", auth);
        }
        if (method is "POST" or "PUT" or "PATCH" or "DELETE" && bodyBytes > 0)
        {
            var contentType = ParseContentType(definition.ContentType);
            request.Content = new StringContent(definition.Body ?? "", Encoding.UTF8, contentType);
        }

        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await clients.CreateClient(approved ? "ApiRunnerApproved" : "ApiRunnerPublic")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var (body, bytes, truncated) = await ReadResponseAsync(response, method == "HEAD", timeout.Token);
            var assertions = new List<string>();
            var passed = true;
            if (definition.ExpectedStatus is { } expectedStatus)
            {
                var statusPass = (int)response.StatusCode == expectedStatus;
                passed &= statusPass;
                assertions.Add(statusPass
                    ? $"Status is {expectedStatus}."
                    : $"Expected status {expectedStatus}, received {(int)response.StatusCode}.");
            }
            if (!string.IsNullOrWhiteSpace(definition.ExpectedContains))
            {
                var bodyPass = body.Contains(definition.ExpectedContains, StringComparison.Ordinal);
                passed &= bodyPass;
                assertions.Add(bodyPass
                    ? "Response contains the expected text."
                    : "Response does not contain the expected text.");
            }
            if (assertions.Count == 0) assertions.Add("No assertions configured; transport completed.");

            return new(definition.Name.Trim(), method, uri.AbsoluteUri, (int)response.StatusCode,
                response.ReasonPhrase ?? response.StatusCode.ToString(), watch.ElapsedMilliseconds,
                response.Content.Headers.ContentType?.ToString() ?? "Not provided", bytes,
                FormatHeaders(response), body, passed, assertions, truncated);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("The API request exceeded the 15-second timeout.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("The API endpoint could not be reached.");
        }
    }

    private bool IsApprovedUrl(Uri uri) => settings.ApprovedUrls.Any(value =>
        Uri.TryCreate(value, UriKind.Absolute, out var approved)
        && Uri.Compare(approved, uri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0);

    private static void AddHeaders(HttpRequestMessage request, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > 20) throw new InvalidOperationException("A request can contain at most 20 custom headers.");
        if (Encoding.UTF8.GetByteCount(source) > 8192)
            throw new InvalidOperationException("Custom headers exceed the 8 KB limit.");
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var separator = raw.IndexOf(':');
            if (separator <= 0) throw new InvalidOperationException("Write each custom header as Name: value.");
            var name = raw[..separator].Trim();
            var value = raw[(separator + 1)..].Trim();
            if (ForbiddenHeaders.Contains(name) || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The {name} header is managed or blocked by DevPulse.");
            if (!IsHeaderName(name) || value.Contains('\r') || value.Contains('\n'))
                throw new InvalidOperationException("A custom header name or value is invalid.");
            if (!request.Headers.TryAddWithoutValidation(name, value))
                throw new InvalidOperationException($"The {name} header is managed by DevPulse or is not supported as a custom request header.");
        }
    }

    private static bool IsHeaderName(string value) => value.Length is > 0 and <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string ParseContentType(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "application/json" : value.Trim();
        if (!MediaTypeHeaderValue.TryParse(value, out var mediaType) || string.IsNullOrWhiteSpace(mediaType.MediaType))
            throw new InvalidOperationException("Enter a valid request content type.");
        return mediaType.MediaType;
    }

    private static async Task<(string Body, long Bytes, bool Truncated)> ReadResponseAsync(
        HttpResponseMessage response, bool skipBody, CancellationToken ct)
    {
        if (skipBody || response.Content is null) return ("", 0, false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var remaining = MaxResponseBytes + 1;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
        var truncated = output.Length > MaxResponseBytes;
        var count = (int)Math.Min(output.Length, MaxResponseBytes);
        var body = IsTextContent(response.Content.Headers.ContentType?.MediaType)
            ? Encoding.UTF8.GetString(output.GetBuffer(), 0, count)
            : count == 0 ? "" : $"Binary response preview hidden ({count:N0} bytes read).";
        return (body, count, truncated);
    }

    private static bool IsTextContent(string? mediaType) => string.IsNullOrWhiteSpace(mediaType)
        || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private static string FormatHeaders(HttpResponseMessage response)
    {
        var values = response.Headers.Concat(response.Content.Headers)
            .Where(header => !HiddenResponseHeaders.Contains(header.Key))
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}");
        return string.Join(Environment.NewLine, values);
    }
}
