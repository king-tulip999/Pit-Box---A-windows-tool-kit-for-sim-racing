namespace PitBox.Theme;

internal static class ThemeMarks
{
    private static readonly Dictionary<string, string> Short = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mercedes-AMG Team Verstappen Racing"] = "Verstappen Racing", ["Mercedes-AMG Team Mann-Filter"] = "Mann-Filter",
        ["Toyota Gazoo Rookie Racing"] = "Rookie Racing", ["Toyota Gazoo Racing WRT"] = "Toyota Gazoo Racing",
        ["Subaru World Rally Team"] = "Subaru WRT",
    };

    public static string ShortName(string name) => Short.GetValueOrDefault(name, name);
}
