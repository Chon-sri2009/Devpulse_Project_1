using System.Diagnostics;

namespace MiniProject_Everything_1.Services;

public sealed class ApiHealthCheckService(HttpClient httpClient)
{
    public async Task<ApiHealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com");
            request.Headers.UserAgent.ParseAdd("DevPulse/1.0");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new(response.IsSuccessStatusCode, (int)response.StatusCode, response.StatusCode.ToString(),
                watch.ElapsedMilliseconds, response.IsSuccessStatusCode ? null : $"GitHub returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, 0, "Timeout", watch.ElapsedMilliseconds, "GitHub did not respond within the timeout. Try again."); }
        catch (HttpRequestException)
        { return new(false, 0, "Unavailable", watch.ElapsedMilliseconds, "Could not reach GitHub. Check the server's network connection."); }
    }
}
public record ApiHealthResult(bool IsReachable, int StatusCode, string Status, long LatencyMs, string? ErrorMessage);
