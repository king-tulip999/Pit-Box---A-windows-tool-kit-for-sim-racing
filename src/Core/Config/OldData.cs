using System.IO;

namespace Core.Config;

internal static class OldData
{
    private static readonly string[] TecnicaFiles = { "config.json", "temp-history.json" };

    private static readonly string[] VelocitaFiles =
    {
        "config.json", "pitloss.json",
        "stats.db", "stats.db-wal", "stats.db-shm",
        "traces.db", "traces.db-wal", "traces.db-shm",
    };

    private static readonly string[] VelocitaFolders = { "grr", "iRacing", "liveries" };

    public static string OldFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.OldDataFolder);

    public static bool CopyOnFirstStart() => Copy(OldFolder, ConfigStore.Folder, AppIdentity.Kind);

    internal static bool Copy(string from, string to, AppKind kind)
    {
        if (!Directory.Exists(from) || File.Exists(Path.Combine(to, "config.json"))) return false;
        try
        {
            Directory.CreateDirectory(to);
            foreach (var name in kind == AppKind.Tecnica ? TecnicaFiles : VelocitaFiles)
                CopyFile(Path.Combine(from, name), Path.Combine(to, name));
            if (kind == AppKind.Velocita)
                foreach (var folder in VelocitaFolders)
                    CopyFolder(Path.Combine(from, folder), Path.Combine(to, folder));
            return File.Exists(Path.Combine(to, "config.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyFile(string from, string to)
    {
        if (!File.Exists(from) || File.Exists(to)) return;
        try { File.Copy(from, to); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void CopyFolder(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            CopyFile(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.GetDirectories(from))
            CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}
