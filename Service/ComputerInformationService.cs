using System.Management;

namespace MiniProject_Everything_1.Services;

public sealed class ComputerInformationService
{
    public ComputerInformationResult GetComputerInformation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ComputerInformationResult(
                "Unknown", "Unknown", "Unknown", 0, 0,
                "This feature currently supports Windows only.");
        }

        try
        {
            string cpuName = QueryWmiString(
                "SELECT Name FROM Win32_Processor", "Name");

            string operatingSystem = QueryWmiString(
                "SELECT Caption FROM Win32_OperatingSystem", "Caption");

            string memoryBytesText = QueryWmiString(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem",
                "TotalPhysicalMemory");

            ulong memoryBytes = ulong.TryParse(memoryBytesText, out var result)
                ? result
                : 0;

            return new ComputerInformationResult(
                Environment.MachineName,
                operatingSystem,
                cpuName,
                Environment.ProcessorCount,
                Math.Round(memoryBytes / 1024.0 / 1024.0 / 1024.0, 2),
                null);
        }
        catch (Exception ex)
        {
            return new ComputerInformationResult(
                "Unknown", "Unknown", "Unknown", 0, 0,
                $"Unable to read computer information: {ex.Message}");
        }
    }

    private static string QueryWmiString(string query, string propertyName)
    {
        using var searcher = new ManagementObjectSearcher(query);

        foreach (ManagementObject item in searcher.Get())
        {
            return item[propertyName]?.ToString() ?? "Unknown";
        }

        return "Unknown";
    }
}

public record ComputerInformationResult(
    string ComputerName,
    string OperatingSystem,
    string CpuName,
    int LogicalProcessorCount,
    double TotalMemoryGb,
    string? ErrorMessage);