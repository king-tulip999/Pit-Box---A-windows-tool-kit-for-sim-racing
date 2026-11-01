using System.IO;
using System.Text.RegularExpressions;
using PitBox.SimRacing.IracingFiles;

namespace PitBox.Services;

internal enum StorageKind { Replays, Paints, Telemetry, Traces, Backups, PitBox }

internal sealed record StoragePart(StorageKind Kind, string Folder, long Bytes, int Files);

internal sealed record StorageReport(IReadOnlyList<StoragePart> Parts, IReadOnlyList<FileInfo> Replays, IReadOnlyList<FileInfo> Paints, IReadOnlyCollection<long> Members);

internal static partial class StorageCleanup
{
    public static readonly int[] AgeChoices = { 30, 90, 180, 365 };

    [GeneratedRegex(@"_(\d+)(?:_ss)?\.(?:tga|mip|png|jpg)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnerNumber();

    public static StorageReport Scan(IReadOnlyCollection<long> knownMembers, string pitBoxFolder, string? tracesPath)
    {
        var parts = new List<StoragePart>();
        var replays = new List<FileInfo>();
        var paints = new List<FileInfo>();
        var members = new HashSet<long>(knownMembers.Where(m => m > 0));
        if (IracingPaths.FindDocumentsFolder() is { } documents)
        {
            replays = Files(Path.Combine(documents, "replay"), "*.rpy", false);
            parts.Add(Part(StorageKind.Replays, Path.Combine(documents, "replay"), replays));
            paints = Files(Path.Combine(documents, "paint"), "*", true);
            parts.Add(Part(StorageKind.Paints, Path.Combine(documents, "paint"), paints));
            var telemetry = TelemetryFolder.Scan(documents);
            parts.Add(Part(StorageKind.Telemetry, telemetry.Folder, telemetry.Files));
            foreach (var ibt in telemetry.Files.Take(3))
                if (SimRacing.LiveryPictures.Session(ibt.FullName)?.Player?.UserId is > 0 and var id) members.Add(id);
        }
        if (tracesPath is not null && File.Exists(tracesPath)) parts.Add(new StoragePart(StorageKind.Traces, Path.GetDirectoryName(tracesPath)!, new FileInfo(tracesPath).Length, 1));
        parts.Add(Part(StorageKind.Backups, IracingPaths.BackupFolder, Files(IracingPaths.BackupFolder, "*", true)));
        var own = Files(pitBoxFolder, "*", true).Where(f => !f.FullName.StartsWith(IracingPaths.BackupFolder, StringComparison.OrdinalIgnoreCase)
            && (tracesPath is null || !string.Equals(f.FullName, tracesPath, StringComparison.OrdinalIgnoreCase))).ToList();
        parts.Add(Part(StorageKind.PitBox, pitBoxFolder, own));
        return new StorageReport(parts, replays, paints, members);
    }

    private static StoragePart Part(StorageKind kind, string folder, IReadOnlyList<FileInfo> files) => new(kind, folder, files.Sum(f => f.Length), files.Count);

    private static List<FileInfo> Files(string folder, string pattern, bool deep)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles(pattern, new EnumerationOptions { RecurseSubdirectories = deep, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList()
                : new List<FileInfo>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<FileInfo>(); }
    }

    public static List<FileInfo> OldReplays(IReadOnlyList<FileInfo> replays, int days, DateTime now)
    {
        var newest = replays.MaxBy(f => f.LastWriteTimeUtc);
        return replays.Where(f => f != newest && (now - f.LastWriteTimeUtc).TotalDays > days).ToList();
    }

    public static bool Kept(string fileName, IReadOnlyCollection<long> members)
    {
        if (fileName.Contains("_team_", StringComparison.OrdinalIgnoreCase)) return true;
        var m = OwnerNumber().Match(fileName);
        return !m.Success || !long.TryParse(m.Groups[1].Value, out long id) || members.Contains(id);
    }

    public static List<FileInfo> OldPaints(IReadOnlyList<FileInfo> paints, int days, DateTime now, IReadOnlyCollection<long> members) =>
        members.Count == 0 ? new List<FileInfo>()
            : paints.Where(f => (now - f.LastWriteTimeUtc).TotalDays > days && !Kept(f.Name, members)).ToList();

    public static string Size(long bytes) => bytes >= 1e9 ? HealthCheck.Gb(bytes)
        : bytes >= 1e6 ? (bytes / 1e6).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MB"
        : (bytes / 1e3).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " KB";
}
