namespace MiniProject_Everything_1.Services;

public sealed class DiskStorageService
{
    public IReadOnlyList<DriveStorageResult> GetReadyDrives()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive =>
            {
                double totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                double freeGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                double usedGb = totalGb - freeGb;

                return new DriveStorageResult(
                    drive.Name,
                    drive.DriveFormat,
                    Math.Round(totalGb, 2),
                    Math.Round(usedGb, 2),
                    Math.Round(freeGb, 2),
                    Math.Round(usedGb / totalGb * 100, 1));
            })
            .ToList();
    }
}

public record DriveStorageResult(
    string DriveName,
    string Format,
    double TotalGb,
    double UsedGb,
    double FreeGb,
    double UsedPercentage);