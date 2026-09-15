namespace MiniProject_Everything_1.Services;

public sealed class DiskStorageService(ILogger<DiskStorageService> logger)
{
    public IReadOnlyList<DriveStorageResult> GetReadyDrives()
    {
        var results = new List<DriveStorageResult>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                long total = drive.TotalSize;
                if (total <= 0) continue;
                long free = Math.Clamp(drive.TotalFreeSpace, 0, total);
                double gb = 1024.0 * 1024 * 1024;
                results.Add(new(drive.Name, drive.DriveFormat, Math.Round(total / gb, 2),
                    Math.Round((total - free) / gb, 2), Math.Round(free / gb, 2),
                    Math.Clamp(Math.Round((total - free) * 100.0 / total, 1), 0, 100)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogDebug("Skipped an unavailable drive: {ExceptionType}", ex.GetType().Name); }
        }
        return results;
    }
}
public record DriveStorageResult(string DriveName, string Format, double TotalGb, double UsedGb, double FreeGb, double UsedPercentage);
