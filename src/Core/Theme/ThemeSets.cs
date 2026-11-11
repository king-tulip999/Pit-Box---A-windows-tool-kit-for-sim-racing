using Core.Config;

namespace Core.Theme;

internal sealed class ThemeSet
{
    public required AppTheme Id { get; init; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string PickTip { get; init; }
    public required string[] Families { get; init; }
    public required (string Name, string Family, ThemeColors Colors)[] Presets { get; init; }
    public required Func<UiSettings, string?> Get { get; init; }
    public required Action<UiSettings, string?> Set { get; init; }

    public string First => Presets[0].Name;

    public string? Known(string? name) =>
        name is null ? null : Presets.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Name;

    public ThemeColors? Preset(string? name) =>
        Known(name) is { } known ? Presets.First(p => p.Name == known).Colors : null;

    public string Current(UiSettings ui) => Known(Get(ui)) ?? First;
}

internal static class ThemeSets
{
    public static readonly ThemeSet Teams = new()
    {
        Id = AppTheme.Teams, Name = "Teams", Title = "Team", PickTip = "pick a team",
        Families = TeamPalette.Series, Presets = TeamPalette.Presets,
        Get = ui => ui.Team, Set = (ui, name) => ui.Team = name,
    };

    public static readonly ThemeSet[] All = { Teams };

    public static ThemeSet? For(AppTheme id) => All.FirstOrDefault(s => s.Id == id);

}
