using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Config;

internal sealed record ImportPreview(AppConfig? Config, string? Error, IReadOnlyList<string> Lines, string? SavedBy);

internal static class ConfigTransfer
{
    public const string Header = "settingsExport";
    public const string OldHeader = "pitBoxExport";

    public static string Export(AppConfig config, string appVersion, DateTime now)
    {
        var copy = ConfigStore.Clone(config);
        copy.Updates.LastCheckUtc = null;
        copy.Updates.NotifiedVersion = null;
        copy.Ui.SetupPending = false;
        copy.Windowsmith = null;
        var body = JsonSerializer.SerializeToNode(copy, ConfigStore.JsonOptions)!.AsObject();
        var file = new JsonObject
        {
            [Header] = new JsonObject { ["app"] = AppIdentity.Name, ["version"] = appVersion, ["exported"] = now.ToString("yyyy-MM-dd HH:mm") },
        };
        foreach (var name in body.Select(p => p.Key).ToList())
        {
            var value = body[name];
            body.Remove(name);
            file[name] = value;
        }
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static readonly HashSet<string> KnownSections = BuildKnownSections();

    private static HashSet<string> BuildKnownSections()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Header, OldHeader, "windowsmith" };
        foreach (var property in JsonSerializer.SerializeToElement(new AppConfig(), ConfigStore.JsonOptions).EnumerateObject())
            names.Add(property.Name);
        return names;
    }

    public static ImportPreview Read(string? json, Version current)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("The file is empty.");
        string? savedBy = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail(NotSettings);
            int known = 0;
            foreach (var property in root.EnumerateObject())
                if (KnownSections.Contains(property.Name)) known++;
            if (known == 0)
                return Fail(NotSettings);
            if ((root.TryGetProperty(Header, out var header) || root.TryGetProperty(OldHeader, out header)) && header.ValueKind == JsonValueKind.Object
                && header.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                savedBy = version.GetString();
        }
        catch (JsonException ex)
        {
            return Fail($"This file isn't a readable settings file ({ex.Message}).");
        }

        AppConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions) ?? new AppConfig();
            ConfigStore.Normalize(config);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or InvalidCastException)
        {
            return Fail($"Parts of this file don't match {AppIdentity.Name}'s settings, so nothing was imported ({ex.Message}).");
        }
        config.Ui.SetupPending = false;

        var lines = Summary(config);
        if (savedBy is not null && TryVersion(savedBy) is { } v && v > current)
            lines.Insert(0, $"Saved by a newer {AppIdentity.Name}: anything this one doesn't know is skipped.");
        return new ImportPreview(config, null, lines, savedBy);
    }

    private static string NotSettings => $"This doesn't look like a {AppIdentity.Name} settings file.";

    private static ImportPreview Fail(string error) => new(null, error, Array.Empty<string>(), null);

    private static Version? TryVersion(string text) => Version.TryParse(text.TrimStart('v', 'V'), out var v) ? v : null;

    public static List<string> Summary(AppConfig c)
    {
        var lines = new List<string>
        {
            $"Theme {ThemeName(c.Ui.Theme)}, animations {(c.Ui.Animations ? "on" : "off")}",
            SimRacing.Race.RaceFormat.Count(c.WindowProfiles.Profiles.Count, "window profile") + $" ({c.WindowProfiles.Profiles.Count(p => p.Auto)} automatic), {SimRacing.Race.RaceFormat.Count(c.WindowProfiles.Groups.Count, "group")}",
            SimRacing.Race.RaceFormat.Count(c.Displays.Profiles.Count, "display profile"),
            $"Fan control {(c.Fans.Enabled ? "on" : "off")}: {SimRacing.Race.RaceFormat.Count(c.Fans.Presets.Count, "preset")}, {SimRacing.Race.RaceFormat.Count(c.Fans.Curves.Count, "curve")}, {SimRacing.Race.RaceFormat.Count(c.Fans.Hardware.Count, "fan")} set up",
            SimRacing.Race.RaceFormat.Count(c.Overlay.Layouts.Count, "overlay layout"),
            SimRacing.Race.RaceFormat.Count(Keybinds(c), "keybind"),
            $"Sensor update rate {PerformanceSettings.Name(c.Performance.Rate)} ({PerformanceSettings.Describe(c.Performance.IntervalMs)}), background usage {c.Performance.Background}",
        };
        lines.Add("Window, startup, page order, page layouts, sim racing tools and update settings");
        return lines;
    }

    private static string ThemeName(AppTheme id) => Theme.Themes.For(id) is { } spec && spec.Id == id ? spec.Name : id.ToString();

    public static int Keybinds(AppConfig c)
    {
        var a = c.Hotkeys;
        return c.WindowProfiles.Profiles.Count(p => Set(p.Hotkey)) + c.WindowProfiles.Groups.Count(g => Set(g.Hotkey)) + c.Displays.Profiles.Count(p => Set(p.Hotkey))
            + c.Fans.Presets.Count(p => Set(p.Hotkey))
            + new[] { c.Overlay.Hotkey, c.Overlay.ResetFpsHotkey, a.ShowWindow,
                a.NextDisplayProfile, a.PreviousDisplayProfile, a.FanControl, a.NextFanPreset, a.PreviousFanPreset, a.NextOverlayLayout }.Count(Set);
    }

    private static bool Set(string? hotkey) => !string.IsNullOrWhiteSpace(hotkey);

}
