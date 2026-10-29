namespace PitBox.SimRacing.IracingFiles;

internal enum SettingShape { Level, Switch, Slider, Choice, Row }

internal static class SettingShapes
{
    public static readonly string[] Levels = { "Off", "Low", "Med", "High" };

    public const int MaxChoices = 4, MaxChoiceLabel = 14, MaxSliderSteps = 500;

    public static int? LevelOf(string label) => label.Trim().ToLowerInvariant() switch
    {
        "off" => 0,
        "low" => 1,
        "med" or "medium" => 2,
        "high" => 3,
        _ => null,
    };

    public static int?[] LevelMap(SettingDef def)
    {
        var map = new int?[Levels.Length];
        if (def.Options is not { } options) return map;
        for (int i = 0; i < options.Count; i++)
            if (LevelOf(options[i].Label) is { } level)
                map[level] = i;
        return map;
    }

    public static SettingShape Of(SettingDef def)
    {
        if (def.ReadOnly) return SettingShape.Row;
        switch (def.Type)
        {
            case "bool":
                return SettingShape.Switch;
            case "enum" when def.Options is { Count: > 0 } options:
                if (IsLevels(options)) return SettingShape.Level;
                if (options.Count <= MaxChoices && options.All(o => o.Label.Length is > 0 and <= MaxChoiceLabel)) return SettingShape.Choice;
                return SettingShape.Row;
            case "num" or "range":
                return Slides(def) ? SettingShape.Slider : SettingShape.Row;
            default:
                return SettingShape.Row;
        }
    }

    private static bool IsLevels(List<OptionDef> options)
    {
        if (options.Count < 3 || options.Count > Levels.Length) return false;
        var seen = new HashSet<int>();
        foreach (var o in options)
            if (LevelOf(o.Label) is not { } level || !seen.Add(level)) return false;
        return seen.Contains(1) && seen.Contains(3);
    }

    private static bool Slides(SettingDef def)
    {
        if (def.Min is not { } min || def.Max is not { } max || !(max > min) || def.DisplayScale is not null) return false;
        double step = def.Step is > 0 and var s ? s : 1;
        double steps = (max - min) / step;
        return steps >= 2 && steps <= MaxSliderSteps && Math.Abs(steps - Math.Round(steps)) < 1e-6;
    }
}
