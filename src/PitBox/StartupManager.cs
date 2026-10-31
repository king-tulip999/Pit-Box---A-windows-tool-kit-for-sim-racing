using System.IO;
using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PitBox;

internal sealed record StartupState(bool Enabled, string? Command, bool Exposed = false)
{
    public bool PointsElsewhere => Enabled && !string.IsNullOrWhiteSpace(Command) && Environment.ProcessPath is { } exe &&
                                   !string.Equals(Path.GetFullPath(Command), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
}

internal static class StartupManager
{
    private const string TaskName = "Pit-Box";
    private const string LegacyName = "RigControl";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool _legacyRemoved;

    public static StartupState State()
    {
        if (Schtasks($"/Query /TN \"{TaskName}\" /XML", out string xml) != 0)
            return new StartupState(false, null);
        var match = System.Text.RegularExpressions.Regex.Match(xml, "<Command>(.*?)</Command>");
        string? command = match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim('"') : null;
        return new StartupState(true, command, command is not null && OthersCanChange(command));
    }

    public static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pit-Box");

    public static string MoveToProgramFiles()
    {
        string exe = Environment.ProcessPath!;
        string target = Path.Combine(InstallFolder, Path.GetFileName(exe));
        Directory.CreateDirectory(InstallFolder);
        string part = target + ".part";
        File.Copy(exe, part, overwrite: true);
        File.Move(part, target, overwrite: true);
        SetEnabled(true, target);
        return target;
    }

    private static readonly SecurityIdentifier[] Trusted =
    {
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),
    };

    private const FileSystemRights GenericWrite = (FileSystemRights)0x50000000;
    private const FileSystemRights ChangeFile = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | GenericWrite;
    private const FileSystemRights ChangeFolder = FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | GenericWrite;
    private const FileSystemRights ChangeAncestor = FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership | GenericWrite;

    internal static bool OthersCanChange(string exe)
    {
        try
        {
            var file = new FileInfo(exe);
            if (!file.Exists || Open(file.GetAccessControl(), ChangeFile)) return file.Exists;
            if (file.Directory is not { } folder) return false;
            if (Open(folder.GetAccessControl(), ChangeFolder)) return true;
            for (var up = folder.Parent; up is not null; up = up.Parent)
                if (Open(up.GetAccessControl(), ChangeAncestor)) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool Open(FileSystemSecurity security, FileSystemRights rights)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !Trusted.Contains(owner)) return true;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
            if ((rule.FileSystemRights & rights) == 0 || rule.IdentityReference is not SecurityIdentifier sid) continue;
            if (!Trusted.Contains(sid) && !sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid)) return true;
        }
        return false;
    }

    public static void SetEnabled(bool enabled, string? exe = null)
    {
        RemoveLegacyRunEntry();

        if (!enabled)
        {
            Schtasks($"/Delete /TN \"{TaskName}\" /F", out _);
            return;
        }

        string scratch = App.Scratch(Environment.ProcessPath ?? AppContext.BaseDirectory);
        string xmlPath = Path.Combine(scratch, $"PitBox-task-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlPath, TaskXml(exe ?? Environment.ProcessPath ?? ""), Encoding.Unicode);
            if (Schtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", out string output) != 0)
                throw new InvalidOperationException(output.Trim());
        }
        finally
        {
            File.Delete(xmlPath);
            App.RemoveScratch(scratch);
        }
    }

    private static string TaskXml(string exePath)
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string exe = SecurityElement.Escape(exePath);
        string dir = SecurityElement.Escape(Path.GetDirectoryName(exePath) + Path.DirectorySeparatorChar);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Pit-Box when you sign in.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>--minimized</Arguments><WorkingDirectory>{dir}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
    }

    private static void RemoveLegacyRunEntry()
    {
        if (_legacyRemoved)
            return;
        _legacyRemoved = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
            key?.DeleteValue(LegacyName, throwOnMissingValue: false);
        }
        catch { }

        try { Schtasks($"/Delete /TN \"{LegacyName}\" /F", out _); } catch { }
    }

    private static int Schtasks(string arguments, out string output)
    {
        var info = new ProcessStartInfo(App.SystemExe("schtasks"), arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(); } catch { }
            output = "schtasks didn't respond.";
            return -1;
        }
        output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        return process.ExitCode;
    }
}
