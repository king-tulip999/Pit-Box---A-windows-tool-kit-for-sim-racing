using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Core;

internal static class Program
{
    internal const string ExtractVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";
    internal static string RuntimePrefix => AppIdentity.Name + "-runtime";

    private static readonly string[] BackgroundArgs = { "--minimized", "--updated", "--update-failed", "--fan-service" };

    private static string ProtectedRoot => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static void Run(AppKind kind, string[] args)
    {
        AppIdentity.Use(kind);
        if (StartedProtected(args)) return;
        RunApp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static bool StartedProtected(string[] args)
    {
        try
        {
            string? current = Environment.GetEnvironmentVariable(ExtractVariable);
            if (current is not null) Environment.SetEnvironmentVariable(ExtractVariable, null);
            if (!Environment.IsPrivilegedProcess || Environment.ProcessPath is not { } exe || File.Exists(Path.Combine(AppContext.BaseDirectory, AppIdentity.Name + ".dll"))) return false;
            if (current is not null && IsRuntimeFolder(current, ProtectedRoot) && AdminsOnly(new DirectoryInfo(current))) return false;
            if (ProtectedRuntimeFolder() is not { } folder) return false;

            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment[ExtractVariable] = folder;
            using var child = Process.Start(start);
            if (child is null) return false;
            if (args.All(a => BackgroundArgs.Contains(a, StringComparer.OrdinalIgnoreCase))) return true;
            child.WaitForExit();
            Environment.Exit(child.ExitCode);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException
                                       or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? ProtectedRuntimeFolder()
    {
        var shared = new DirectoryInfo(Path.Combine(ProtectedRoot, RuntimePrefix));
        try
        {
            if (shared.Exists && !AdminsOnly(shared))
            {
                shared.Delete(recursive: true);
                shared.Refresh();
            }
            if (!shared.Exists) FileSystemAclExtensions.Create(shared, AdminsOnlySecurity());
            shared.Refresh();
            if (AdminsOnly(shared)) return shared.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        var own = new DirectoryInfo(Path.Combine(ProtectedRoot, RuntimePrefix + "-" + Guid.NewGuid().ToString("N")));
        FileSystemAclExtensions.Create(own, AdminsOnlySecurity());
        own.Refresh();
        return AdminsOnly(own) ? own.FullName : null;
    }

    internal static bool IsRuntimeFolder(string folder, string root)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(folder);
        string name = Path.GetFileName(trimmed);
        return Path.IsPathFullyQualified(trimmed)
            && string.Equals(Path.GetDirectoryName(trimmed), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
            && (name.Equals(RuntimePrefix, StringComparison.OrdinalIgnoreCase) || name.StartsWith(RuntimePrefix + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Trusted(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);

    internal static bool AdminsOnly(DirectoryInfo folder)
    {
        if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        var security = folder.GetAccessControl();
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted(owner)) return false;
        if (!security.AreAccessRulesProtected) return false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !(rule.IdentityReference is SecurityIdentifier sid && Trusted(sid))) return false;
        return true;
    }

    internal static DirectorySecurity AdminsOnlySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    internal static void TidyRuntimeFolders()
    {
        try
        {
            string? unpacked = null;
            using (var self = Process.GetCurrentProcess())
                foreach (ProcessModule module in self.Modules)
                    using (module)
                        if (unpacked is null && string.Equals(module.ModuleName, "wpfgfx_cor3.dll", StringComparison.OrdinalIgnoreCase))
                            unpacked = Path.GetDirectoryName(module.FileName);
            if (unpacked is null || Path.GetDirectoryName(unpacked) is not { } builds || Path.GetDirectoryName(builds) is not { } mine) return;
            if (!IsRuntimeFolder(mine, ProtectedRoot)) return;
            foreach (var build in Directory.GetDirectories(builds))
                if (!string.Equals(build, unpacked, StringComparison.OrdinalIgnoreCase)) Remove(build);
            foreach (var other in Directory.GetDirectories(ProtectedRoot, RuntimePrefix + "-*"))
                if (!string.Equals(other, mine, StringComparison.OrdinalIgnoreCase) && IsRuntimeFolder(other, ProtectedRoot)) Remove(other);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private static void Remove(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
