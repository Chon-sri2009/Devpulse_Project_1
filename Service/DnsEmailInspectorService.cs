using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MiniProject_Everything_1.Services;

public sealed record DnsInspectionRecord(string Type, string Name, int Ttl, string Value);
public sealed record DnsInspectionFinding(string Level, string Title, string Detail);
public sealed record DnsEmailInspection(string Domain, string? DkimSelector,
    IReadOnlyList<DnsInspectionRecord> Records, IReadOnlyList<DnsInspectionFinding> Findings,
    long DurationMs, DateTimeOffset CheckedAt);

public sealed class DnsEmailInspectorService(IHttpClientFactory clients)
{
    private const string Resolver = "https://dns.google/resolve";
    private static readonly Dictionary<int, string> RecordTypes = new()
    {
        [1] = "A",
        [2] = "NS",
        [5] = "CNAME",
        [6] = "SOA",
        [15] = "MX",
        [16] = "TXT",
        [28] = "AAAA",
        [257] = "CAA"
    };

    public async Task<DnsEmailInspection> InspectAsync(string input, string? dkimSelector, CancellationToken ct)
    {
        var domain = NormalizeDomain(input);
        dkimSelector = NormalizeSelector(dkimSelector);
        var watch = Stopwatch.StartNew();
        var requests = new List<(string Name, string Type)>
        {
            (domain, "A"), (domain, "AAAA"), (domain, "MX"), (domain, "TXT"),
            (domain, "CAA"), (domain, "NS"), ($"_dmarc.{domain}", "TXT")
        };
        if (dkimSelector is not null) requests.Add(($"{dkimSelector}._domainkey.{domain}", "TXT"));
        var answers = await Task.WhenAll(requests.Select(request => QueryAsync(request.Name, request.Type, ct)));
        var records = answers.SelectMany(answer => answer).ToArray();
        var findings = Analyze(domain, dkimSelector, records);
        return new(domain, dkimSelector, records, findings, watch.ElapsedMilliseconds, DateTimeOffset.UtcNow);
    }

    public static string NormalizeDomain(string? input)
    {
        input = input?.Trim();
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidOperationException("Enter a public domain or email address.");
        var at = input.LastIndexOf('@');
        if (at >= 0) input = input[(at + 1)..];
        if (input.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
                throw new InvalidOperationException("Enter a valid public domain or email address.");
            input = uri.IdnHost;
        }
        input = input.Trim().TrimEnd('.');
        if (IPAddress.TryParse(input, out _)) throw new InvalidOperationException("Enter a domain name, not an IP address.");
        string ascii;
        try { ascii = new IdnMapping().GetAscii(input).ToLowerInvariant(); }
        catch (ArgumentException) { throw new InvalidOperationException("The domain name is invalid."); }
        if (ascii.Length is < 3 or > 253 || !ascii.Contains('.'))
            throw new InvalidOperationException("Enter a complete public domain such as example.com.");
        var labels = ascii.Split('.');
        if (labels.Any(label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-'
            || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
            throw new InvalidOperationException("The domain name contains an invalid label.");
        return ascii;
    }

    private static string? NormalizeSelector(string? selector)
    {
        selector = selector?.Trim();
        if (string.IsNullOrWhiteSpace(selector)) return null;
        if (selector.Length > 63 || selector[0] == '-' || selector[^1] == '-'
            || selector.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidOperationException("The DKIM selector must be one DNS label, such as default or google.");
        return selector.ToLowerInvariant();
    }

    private async Task<IReadOnlyList<DnsInspectionRecord>> QueryAsync(string name, string type, CancellationToken ct)
    {
        var url = $"{Resolver}?name={Uri.EscapeDataString(name)}&type={Uri.EscapeDataString(type)}&cd=false&do=false";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("DevPulse-DnsInspector/1.0");
        using var response = await clients.CreateClient("DnsInspector")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 512 * 1024)
            throw new InvalidOperationException("The DNS resolver returned an unexpectedly large response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 16 }, ct);
        var root = document.RootElement;
        if (!root.TryGetProperty("Status", out var status) || status.GetInt32() is not (0 or 3))
            throw new InvalidOperationException("The public DNS resolver could not complete the lookup.");
        if (!root.TryGetProperty("Answer", out var answer) || answer.ValueKind != JsonValueKind.Array) return [];
        return answer.EnumerateArray().Take(100).Select(item =>
        {
            var code = item.TryGetProperty("type", out var recordType) ? recordType.GetInt32() : 0;
            var recordName = item.TryGetProperty("name", out var recordNameValue)
                ? recordNameValue.GetString()?.TrimEnd('.') ?? name : name;
            var ttl = item.TryGetProperty("TTL", out var ttlValue) ? ttlValue.GetInt32() : 0;
            var data = item.TryGetProperty("data", out var dataValue) ? dataValue.GetString() ?? "" : "";
            return new DnsInspectionRecord(RecordTypes.GetValueOrDefault(code, code.ToString(CultureInfo.InvariantCulture)),
                recordName, ttl, CleanRecordValue(data));
        }).ToArray();
    }

    private static IReadOnlyList<DnsInspectionFinding> Analyze(string domain, string? selector,
        IReadOnlyList<DnsInspectionRecord> records)
    {
        var findings = new List<DnsInspectionFinding>();
        var mail = records.Where(record => record.Name.Equals(domain, StringComparison.OrdinalIgnoreCase)
            && record.Type == "MX").ToArray();
        findings.Add(mail.Length > 0
            ? new("Good", "MX records found", $"{mail.Length} mail exchanger record(s) are published.")
            : new("Warning", "No MX record", "No mail exchanger was found. This domain may not receive email."));

        var rootText = records.Where(record => record.Name.Equals(domain, StringComparison.OrdinalIgnoreCase)
            && record.Type == "TXT").ToArray();
        var spf = rootText.Where(record => record.Value.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)).ToArray();
        findings.Add(spf.Length switch
        {
            0 => new("Warning", "SPF not found", "Publish one SPF TXT record to identify authorized sending services."),
            1 => new("Good", "SPF found", "One SPF policy is published."),
            _ => new("Bad", "Multiple SPF records", "Multiple SPF policies are invalid; combine them into one record.")
        });

        var dmarcName = $"_dmarc.{domain}";
        var dmarc = records.FirstOrDefault(record => record.Type == "TXT"
            && record.Value.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        if (dmarc is null)
            findings.Add(new("Warning", "DMARC not found", "Publish a DMARC TXT policy at _dmarc."));
        else if (ReadTag(dmarc.Value, "p") is "reject" or "quarantine")
            findings.Add(new("Good", "DMARC enforcement enabled", "The policy requests quarantine or rejection."));
        else
            findings.Add(new("Info", "DMARC monitoring policy", "DMARC exists but does not request quarantine or rejection."));

        if (selector is not null)
        {
            var dkim = records.Any(record => record.Type == "TXT"
                && (record.Value.Contains("v=DKIM1", StringComparison.OrdinalIgnoreCase)
                    || record.Name.Contains("._domainkey.", StringComparison.OrdinalIgnoreCase)
                        && record.Value.Contains("p=", StringComparison.OrdinalIgnoreCase))
                && !record.Name.Equals(domain, StringComparison.OrdinalIgnoreCase)
                && !record.Name.Equals(dmarcName, StringComparison.OrdinalIgnoreCase));
            findings.Add(dkim
                ? new("Good", "DKIM key found", $"A DKIM record exists for selector {selector}.")
                : new("Warning", "DKIM key not found", $"No DKIM TXT key was found for selector {selector}."));
        }
        else findings.Add(new("Info", "DKIM selector needed", "Enter the selector supplied by your email provider to inspect DKIM."));

        var nameServers = records.Count(record => record.Name.Equals(domain, StringComparison.OrdinalIgnoreCase)
            && record.Type == "NS");
        findings.Add(nameServers >= 2
            ? new("Good", "Authoritative DNS available", $"{nameServers} name servers are published.")
            : new("Warning", "Limited name-server redundancy", $"Only {nameServers} name server record(s) were found."));
        if (!records.Any(record => record.Name.Equals(domain, StringComparison.OrdinalIgnoreCase) && record.Type == "CAA"))
            findings.Add(new("Info", "CAA not published", "CAA is optional; publishing it can restrict which certificate authorities may issue certificates."));
        return findings;
    }

    private static string CleanRecordValue(string value)
    {
        value = value.Trim().TrimEnd('.');
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Replace("\" \"", "", StringComparison.Ordinal);
        return value;
    }

    private static string? ReadTag(string value, string name) => value.Split(';', StringSplitOptions.TrimEntries)
        .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
        .Where(parts => parts.Length == 2 && parts[0].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(parts => parts[1].ToLowerInvariant())
        .FirstOrDefault();
}
