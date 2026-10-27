namespace GoodieBag.Config;

public sealed class CustomThemeSettings
{
    public bool Enabled { get; set; }

    public string? Name { get; set; }

    public ThemeColors Colors { get; set; } = new();

    public List<ThemeColorProfile> Profiles { get; set; } = new();

    public string? EditingProfile { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool InUse => Enabled && !Colors.IsDefault;

    public ThemeColorProfile? Profile(string? name) =>
        name is null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ThemeColors
{
    public string Background { get; set; } = "#000000";
    public string Edges { get; set; } = "#4A6A78";
    public string Text { get; set; } = "#E6F6FA";
    public string Accent { get; set; } = "#00B4D8";
    public string Highlight { get; set; } = "#90E0EF";
    public string Success { get; set; } = "#2DD4A0";
    public string Warning { get; set; } = "#FFC145";
    public string Error { get; set; } = "#FF5A5F";

    public static readonly ThemeColors Default = new();

    public ThemeColors Copy() => (ThemeColors)MemberwiseClone();

    private IEnumerable<string> All => new[] { Background, Edges, Text, Accent, Highlight, Success, Warning, Error };

    public bool SameAs(ThemeColors other) => All.Zip(other.All).All(p => string.Equals(p.First?.Trim(), p.Second?.Trim(), StringComparison.OrdinalIgnoreCase));

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault => SameAs(Default);
}

public sealed class ThemeColorProfile
{
    public string Name { get; set; } = "";
    public ThemeColors Colors { get; set; } = new();
}
