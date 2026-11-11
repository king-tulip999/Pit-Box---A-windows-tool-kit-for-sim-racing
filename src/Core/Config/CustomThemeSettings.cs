namespace Core.Config;

public sealed class CustomThemeSettings
{
    public bool Enabled { get; set; }

    public string? Name { get; set; }

    public ThemeColors Colors { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool InUse => Enabled && !Colors.IsDefault;

    public void Use(string? preset)
    {
        if (Core.Theme.CustomPalette.Preset(preset) is { } colors)
        {
            Colors = colors.Copy();
            Name = Core.Theme.CustomPalette.NameOf(colors);
            Enabled = true;
        }
        else
        {
            Colors = new();
            Name = null;
            Enabled = false;
        }
    }

    public void Normalize()
    {
        string? preset = Core.Theme.CustomPalette.NameOf(Colors);
        Use(Enabled ? preset : null);
    }
}

public sealed class ThemeColors
{
    public string Background { get; set; } = "#000000";
    public string Edges { get; set; } = "#736DAC";
    public string Text { get; set; } = "#F2F0FF";
    public string Accent { get; set; } = "#8B4DF0";
    public string Highlight { get; set; } = "#49C8F5";
    public string Success { get; set; } = "#3DFF8A";
    public string Warning { get; set; } = "#FF9F1C";
    public string Error { get; set; } = "#FF4545";

    public static readonly ThemeColors Default = new();

    public ThemeColors Copy() => (ThemeColors)MemberwiseClone();

    private IEnumerable<string> All => new[] { Background, Edges, Text, Accent, Highlight, Success, Warning, Error };

    public bool SameAs(ThemeColors other) => All.Zip(other.All).All(p => string.Equals(p.First?.Trim(), p.Second?.Trim(), StringComparison.OrdinalIgnoreCase));

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault => SameAs(Default);
}
