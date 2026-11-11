using System.Diagnostics;

namespace Core.Services;

internal sealed record OldAppState(bool Running, bool StartsWithWindows)
{
    public bool Present => Running || StartsWithWindows;
}

internal static class OldAppHandover
{
    public const string OldName = "Pit-Box";
    public const string ProcessName = "PitBox";
    public const string TaskName = "Pit-Box";

    public static OldAppState Check() => new(IsRunning(), StartupManager.TaskExists(TaskName));

    private static bool IsRunning()
    {
        var found = Process.GetProcessesByName(ProcessName);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    public static bool CanRemoveTask(AppKind kind, OldAppState state) => state.StartsWithWindows && kind == AppKind.Tecnica;

    public static string Message(AppKind kind, OldAppState state)
    {
        var lines = new List<string>();
        if (state.Running)
            lines.Add(kind == AppKind.Tecnica
                ? $"{OldName} is running and still drives the fans, display profiles and keybinds, so the two apps would fight over them. Quit it from its tray icon and pick “Give fans back and quit”."
                : $"{OldName} is running, so its iRacing Overlay and keybinds would show and fire twice. Quit it from its tray icon.");
        if (state.StartsWithWindows)
            lines.Add(kind == AppKind.Tecnica
                ? "It also starts with Windows. Remove its startup entry so only the new apps start."
                : $"It also starts with Windows. Tecnica can remove that startup entry, or turn off Start with Windows in {OldName}'s settings.");
        lines.Add("Your settings were already copied. Once Tecnica and Velocita are set up, PitBox.exe can be deleted.");
        return string.Join("\n\n", lines);
    }
}
