using Core.Config;

namespace Core.Theme;

internal static class TeamPalette
{
    public static readonly string[] Series = { "F1", "WEC", "IMSA", "GT World Challenge", "NLS", "IndyCar", "NASCAR", "Rally", "Classics" };

    public static readonly (string Name, string Family, ThemeColors Colors)[] Presets =
    {
        ("Ferrari", "F1", Make("#070101", "#6E4044", "#FFF0EE", "#DC002B", "#F2F2F2", "#4ADE80", "#FF9A1F", "#FF4DA6")),
        ("McLaren", "F1", Make("#060302", "#6B5444", "#FFF3EA", "#FF8000", "#9CA3A8", "#4ADE80", "#FFE14D", "#FF4D6A")),
        ("Mercedes", "F1", Make("#010605", "#4A6662", "#EEFBF9", "#00D7B6", "#C6CDD2", "#7CFF6B", "#FFC93C", "#FF5C5C")),
        ("Red Bull", "F1", Make("#030712", "#3F4C78", "#EDF0FF", "#DB0A40", "#FFC906", "#4ADE80", "#FF8A3D", "#FF5CA8")),
        ("Racing Bulls", "F1", Make("#03040A", "#4E5878", "#F1F3FF", "#6692FF", "#FF3355", "#4ADE80", "#FFC93C", "#FF7A45")),
        ("Aston Martin", "F1", Make("#010604", "#3E6656", "#ECF7F1", "#1C7F5E", "#CEDC00", "#5CE0A0", "#FFB547", "#FF5C5C")),
        ("Alpine", "F1", Make("#02040A", "#45597A", "#EEF4FF", "#0079A7", "#FF87BC", "#4ADE80", "#FFC93C", "#FF5C5C")),
        ("Williams", "F1", Make("#01030A", "#3E5480", "#EBF2FF", "#1868DB", "#64C4FF", "#4ADE80", "#FFC93C", "#FF5C5C")),
        ("Haas", "F1", Make("#050505", "#5E5E62", "#F4F4F6", "#DC0029", "#E8E8E8", "#4ADE80", "#FFC93C", "#FF4DA6")),
        ("Audi", "F1", Make("#050505", "#5C5C60", "#F2F2F2", "#D4311A", "#B8BEC4", "#4ADE80", "#FFC93C", "#FF4DA6")),
        ("Cadillac", "F1", Make("#030303", "#5A5A5A", "#F5F5F5", "#F0F0F0", "#8A8D91", "#4ADE80", "#FFC93C", "#FF5C5C")),

        ("Ferrari AF Corse", "WEC", Make("#060102", "#6A3E44", "#FFF2EE", "#C8102E", "#F5D90A", "#4ADE80", "#FF9A1F", "#FF4DA6")),
        ("Toyota Racing", "WEC", Make("#050505", "#5E5A5A", "#F5F3F3", "#DA0A1C", "#EDEDED", "#4ADE80", "#FFC93C", "#FF4DA6")),
        ("Cadillac Hertz Team Jota", "WEC", Make("#050402", "#6A6248", "#FFF9E8", "#FFD100", "#E6E6E6", "#4ADE80", "#FF8A3D", "#FF5C5C")),
        ("BMW M Team WRT", "WEC", Make("#01030A", "#45557A", "#EEF2FF", "#2970CC", "#81C4FF", "#4ADE80", "#FFC93C", "#FF4545")),
        ("Alpine Endurance", "WEC", Make("#02040C", "#45577A", "#EEF2FF", "#2E6BE6", "#E6E9F0", "#4ADE80", "#FFC93C", "#FF5C8A")),
        ("Peugeot TotalEnergies", "WEC", Make("#040404", "#5A5C55", "#F3F5EE", "#D0121B", "#EDEDED", "#3CE0A0", "#FF9A1F", "#FF5C5C")),
        ("Aston Martin THOR", "WEC", Make("#010503", "#3E6650", "#EAF6EE", "#008160", "#D7E600", "#6EE7A0", "#FFB547", "#FF5C5C")),
        ("Genesis Magma Racing", "WEC", Make("#060302", "#6B5040", "#FFF1E8", "#C24920", "#9E1B22", "#4ADE80", "#FFC93C", "#FF4D8A")),

        ("Porsche Penske", "IMSA", Make("#050404", "#62584E", "#F7F5F2", "#D5001C", "#E6E6E6", "#4ADE80", "#FF9A1F", "#FF4DA6")),
        ("Cadillac Whelen", "IMSA", Make("#040404", "#5E5A5C", "#F5F3F4", "#D81735", "#E6E6E6", "#4ADE80", "#FFC93C", "#FF5CA8")),
        ("Corvette Racing", "IMSA", Make("#050402", "#6A6450", "#FFF8E6", "#FFC72C", "#C6CDD2", "#4ADE80", "#FF8A3D", "#FF5C5C")),
        ("Ford Multimatic", "IMSA", Make("#01030A", "#44547A", "#EDF2FF", "#2D6CDF", "#E6E9EE", "#4ADE80", "#FFC93C", "#FF5C5C")),

        ("AF Corse", "GT World Challenge", Make("#070101", "#6E4044", "#FFF0EE", "#D40000", "#FFD100", "#4ADE80", "#FF9A1F", "#FF4DA6")),
        ("Emil Frey Racing", "GT World Challenge", Make("#07020A", "#6A4860", "#FFEEF6", "#D80075", "#E30613", "#4ADE80", "#FFC93C", "#FF8A3D")),
        ("Mercedes-AMG Team Verstappen Racing", "GT World Challenge", Make("#030712", "#3F4C78", "#EDF0FF", "#3B63C4", "#DB0A40", "#4ADE80", "#FFC906", "#FF8A3D")),
        ("Mercedes-AMG Team Mann-Filter", "GT World Challenge", Make("#040502", "#5E6A3A", "#FBFFE8", "#FFE600", "#00873E", "#3CE0C8", "#FF9A1F", "#FF5C6E")),
        ("Garage 59", "GT World Challenge", Make("#060302", "#6B5444", "#FFF3EA", "#FF8000", "#9CA3A8", "#4ADE80", "#FFE14D", "#FF4D6A")),
        ("Turner Motorsport", "GT World Challenge", Make("#01030C", "#44507A", "#EEF2FF", "#1A62C4", "#FFC800", "#4ADE80", "#FF8A3D", "#FF5C5C")),

        ("Manthey Racing", "NLS", Make("#040502", "#5E6A3A", "#FBFFE8", "#F2E400", "#00A651", "#3CE0C8", "#FF9A1F", "#FF5C6E")),
        ("Falken Motorsports", "NLS", Make("#01030C", "#44507A", "#EEF2FF", "#1565C0", "#3FA9F5", "#4ADE80", "#FFC93C", "#FF5C5C")),
        ("Dunlop Motorsport", "NLS", Make("#050402", "#6A6248", "#FFF9E6", "#FFD100", "#E6E6E6", "#4ADE80", "#FF8A3D", "#FF5C5C")),
        ("ROWE Racing", "NLS", Make("#050404", "#5E5A5A", "#F7F5F5", "#E30613", "#F2F2F2", "#4ADE80", "#FFC93C", "#FF4DA6")),
        ("Toyota Gazoo Rookie Racing", "NLS", Make("#000000", "#5A5A5A", "#F5F5F5", "#DA0A1C", "#F2F2F2", "#4ADE80", "#FFC93C", "#FF4DA6")),

        ("Team Penske", "IndyCar", Make("#050404", "#5E5A5A", "#F7F5F5", "#CF102D", "#FFD100", "#4ADE80", "#FF8A3D", "#FF5CA8")),
        ("Chip Ganassi Racing", "IndyCar", Make("#060102", "#6A4048", "#FFF0F0", "#C8102E", "#C6CDD2", "#4ADE80", "#FFC93C", "#FF5CA8")),
        ("Andretti Global", "IndyCar", Make("#050404", "#5E5A5A", "#F7F5F5", "#D7182A", "#E8E8E8", "#4ADE80", "#FFC93C", "#FF8A3D")),
        ("Arrow McLaren", "IndyCar", Make("#060302", "#6B5444", "#FFF3EA", "#FF8000", "#2D9CDB", "#4ADE80", "#FFE14D", "#FF4D6A")),
        ("A.J. Foyt Racing", "IndyCar", Make("#060302", "#6B5040", "#FFF1E8", "#B84C1A", "#E8E8E8", "#4ADE80", "#FFC93C", "#FF4545")),

        ("Hendrick Motorsports", "NASCAR", Make("#050505", "#5E5A5A", "#F5F3F3", "#D0102A", "#C6CDD2", "#4ADE80", "#FFC93C", "#FF5CA8")),
        ("Joe Gibbs Racing", "NASCAR", Make("#060103", "#6A4050", "#FFF0F4", "#B8235A", "#A7B1B7", "#3CE0C8", "#FFC93C", "#FF5C5C")),
        ("Legacy Motor Club", "NASCAR", Make("#050402", "#6A6248", "#FFF9E6", "#C2A867", "#E8E8E8", "#4ADE80", "#FFC93C", "#FF8A3D")),
        ("Wood Brothers Racing", "NASCAR", Make("#060102", "#6A4048", "#FFF2F0", "#C8102E", "#E8E8E8", "#4ADE80", "#FFC93C", "#FF5CA8")),

        ("Toyota Gazoo Racing WRT", "Rally", Make("#000000", "#5A5A5A", "#F5F5F5", "#DA0A1C", "#FFFFFF", "#4ADE80", "#FFC93C", "#FF4DA6")),
        ("Hyundai Shell Mobis", "Rally", Make("#010410", "#3E4C78", "#EEF2FF", "#00AAD2", "#E4002B", "#4ADE80", "#FFC93C", "#FF5C8A")),
        ("M-Sport Ford", "Rally", Make("#01030A", "#44547A", "#ECF1FA", "#2F6FD6", "#E8EEF5", "#4ADE80", "#FFC93C", "#FF5C5C")),

        ("Gulf", "Classics", Make("#02060A", "#4A6478", "#EEF7FC", "#8AC6E8", "#F47B20", "#4ADE80", "#FFD23F", "#FF5C6E")),
        ("Martini", "Classics", Make("#01030C", "#44507A", "#F2F4FA", "#49A3DF", "#E31A1C", "#4ADE80", "#FFC93C", "#FF7A45")),
        ("Subaru World Rally Team", "Classics", Make("#020410", "#3E4C80", "#EDF1FF", "#2F5FC4", "#F2C200", "#4ADE80", "#FF8A3D", "#FF5C5C")),
        ("JPS Lotus", "Classics", Make("#000000", "#5E5640", "#F7F1E2", "#C9A44C", "#E6DCC4", "#4ADE80", "#FF8A3D", "#FF5C5C")),
    };

    private static ThemeColors Make(string bg, string edges, string text, string accent, string highlight, string success, string warning, string error) => new()
    {
        Background = bg, Edges = edges, Text = text, Accent = accent, Highlight = highlight, Success = success, Warning = warning, Error = error,
    };
}
