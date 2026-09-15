using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiniProject_Everything_1.Services;

public sealed class ComputerInformationService
{
    public ComputerInformationResult GetComputerInformation()
    {
        var basic = new ComputerInformationResult(Environment.MachineName, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0, 2), null,
            "Memory available to this process");
        if (!OperatingSystem.IsWindows()) return basic;
        try
        {
            ulong.TryParse(QueryWmiString("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory"), out var memory);
            return basic with
            {
                CpuName = QueryWmiString("SELECT Name FROM Win32_Processor", "Name"),
                OperatingSystem = QueryWmiString("SELECT Caption FROM Win32_OperatingSystem", "Caption"),
                TotalMemoryGb = Math.Round(memory / 1073741824.0, 2),
                MemoryLabel = "Installed RAM"
            };
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        { return basic with { ErrorMessage = "Detailed hardware information is unavailable; showing basic host information." }; }
    }
    [SupportedOSPlatform("windows")]
    private static string QueryWmiString(string query, string propertyName)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
            using (item) return item[propertyName]?.ToString() ?? "Unknown";
        return "Unknown";
    }
}
public record ComputerInformationResult(string ComputerName, string OperatingSystem, string CpuName,
    int LogicalProcessorCount, double TotalMemoryGb, string? ErrorMessage, string MemoryLabel);
