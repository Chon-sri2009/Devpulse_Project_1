using System.Diagnostics;

namespace MiniProject_Everything_1.Services;

public sealed class SystemMetricsService
{
    private readonly object sync = new();
    private TimeSpan previousCpu;
    private DateTimeOffset previousAt;
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

        var now = DateTimeOffset.UtcNow;
        double cpu;
        lock (sync)
        {
            var elapsed = (now - previousAt).TotalMilliseconds;
            cpu = previousAt == default || elapsed <= 0 ? 0 :
                Math.Clamp((process.TotalProcessorTime - previousCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
            previousCpu = process.TotalProcessorTime;
            previousAt = now;
        }
        return new LiveSystemMetricsResult(
            Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
            Math.Round(process.PagedMemorySize64 / 1024.0 / 1024.0, 2),
            process.Threads.Count,
            now,
            Math.Round(cpu, 1));
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
    DateTimeOffset CheckedAt,
    double CpuPercent);
