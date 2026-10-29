using System.Diagnostics;
using System.IO;

namespace PitBox.Services;

internal static class LegacyExe
{
    public const string NewName = "PitBox.exe";

    internal static string? NewPath(string? exe) =>
        exe is not null && Path.GetFileName(exe).Equals(Config.LegacyName.Code + ".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(exe) ?? "", NewName)
            : null;

    public static bool RenameAndRestart(IEnumerable<string> args)
    {
        if (Environment.ProcessPath is not { } current || NewPath(current) is not { } target) return false;
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (!External.Allowed("restart")) return false;
        try
        {
            if (File.Exists(target)) File.Delete(target);
            File.Move(current, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError(ex);
            return false;
        }
        try
        {
            SingleInstance.Release();
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            SingleInstance.TryAcquire();
            return false;
        }
    }
}
