using System.Diagnostics;

namespace MiniProject_Everything_1.Services;

public sealed class ApiHealthCheckService(HttpClient httpClient)
{
    public async Task<ApiHealthResult> CheckAsync()
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.github.com");

            request.Headers.UserAgent.ParseAdd("DevPulse/1.0");

            using var response = await httpClient.SendAsync(request);
            watch.Stop();

            return new ApiHealthResult(
                true,
                (int)response.StatusCode,
                response.StatusCode.ToString(),
                watch.ElapsedMilliseconds,
                null);
        }
        catch (Exception ex)
        {
            watch.Stop();

            return new ApiHealthResult(
                false, 0, "Unavailable", watch.ElapsedMilliseconds, ex.Message);
        }
    }
}

public record ApiHealthResult(
    bool IsReachable,
    int StatusCode,
    string Status,
    long LatencyMs,
    string? ErrorMessage);