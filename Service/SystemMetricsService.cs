using System.Diagnostics;

namespace MiniProject_Everything_1.Services;

public sealed class SystemMetricsService
{
    public SystemMetricsResult GetCurrentProcessMetrics()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        return new SystemMetricsResult(
            process.Id,
            Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
            process.Threads.Count);
    }


    public LiveSystemMetricsResult GetLiveMetrics()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        return new LiveSystemMetricsResult(
            Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
            Math.Round(process.PagedMemorySize64 / 1024.0 / 1024.0, 2),
            process.Threads.Count,
            DateTime.Now);
    }
}
public record SystemMetricsResult(
    int ProcessId,
    double MemoryMb,
    int ActiveThreads);



public record LiveSystemMetricsResult(
    double WorkingMemoryMb,
    double PagedMemoryMb,
    int ActiveThreads,
    DateTime CheckedAt);
