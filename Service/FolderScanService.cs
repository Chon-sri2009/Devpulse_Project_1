using System.Diagnostics;

namespace MiniProject_Everything_1.Services;

public sealed record FolderUsage(string Name, long Bytes, int Files);
public sealed record FolderScanResult(IReadOnlyList<FolderUsage> Folders, long Bytes, int Files,
    int Skipped, bool IsPartial, string? Error);
public sealed class FolderScanService(DiagnosticsSettings settings)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool IsConfigured => settings.Enabled && !string.IsNullOrWhiteSpace(settings.FolderRoot);

    public async Task<FolderScanResult> ScanAsync(CancellationToken ct)
    {
        if (!IsConfigured) return new([], 0, 0, 0, false, "Folder scanning is not configured.");
        if (!await gate.WaitAsync(0, ct)) return new([], 0, 0, 0, false, "A scan is already running. Try again shortly.");
        try { return await Task.Run(() => Scan(ct), ct); }
        finally { gate.Release(); }
    }

    private FolderScanResult Scan(CancellationToken ct)
    {
        var groups = new Dictionary<string, (long Bytes, int Files)>();
        long total = 0;
        int files = 0, skipped = 0, entries = 0;
        bool partial = false;
        var clock = Stopwatch.StartNew();
        try
        {
            var root = new DirectoryInfo(Path.GetFullPath(settings.FolderRoot!));
            if (!root.Exists) return new([], 0, 0, 0, false, "The configured folder is unavailable.");
            // Reject links in the root's ancestors as well as in its descendants.
            for (DirectoryInfo? parent = root; parent is not null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new([], 0, 0, 0, false, "Choose a folder whose path does not contain symbolic links.");
            var pending = new Stack<(DirectoryInfo Directory, string Group, int Depth)>();
            pending.Push((root, "(files in root)", 0));
            while (pending.TryPop(out var current))
            {
                ct.ThrowIfCancellationRequested();
                if (entries >= 100000 || clock.Elapsed > TimeSpan.FromSeconds(15)) { partial = true; break; }
                try
                {
                    foreach (var entry in current.Directory.EnumerateFileSystemInfos())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++entries > 100000 || clock.Elapsed > TimeSpan.FromSeconds(15)) { partial = true; break; }
                        try
                        {
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            if (entry is DirectoryInfo directory)
                            {
                                if (current.Depth >= 64) { skipped++; partial = true; continue; }
                                pending.Push((directory, current.Depth == 0 ? directory.Name : current.Group, current.Depth + 1));
                            }
                            else if (entry is FileInfo file)
                            {
                                long size = file.Length;
                                var group = groups.GetValueOrDefault(current.Group);
                                groups[current.Group] = (group.Bytes + size, group.Files + 1);
                                total += size;
                                files++;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                    }
                    if (partial) break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
            return new(groups.OrderByDescending(x => x.Value.Bytes).Take(20)
                .Select(x => new FolderUsage(x.Key, x.Value.Bytes, x.Value.Files)).ToList(),
                total, files, skipped, partial, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new([], total, files, skipped, true, "The configured folder cannot be scanned. Check its path and read permissions."); }
    }
}
