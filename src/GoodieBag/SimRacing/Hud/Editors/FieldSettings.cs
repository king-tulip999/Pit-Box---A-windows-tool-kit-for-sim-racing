using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class FieldSettings
{
    public static readonly HudCategory[] ColumnCategories =
    {
        new("Position", "pos", "cpos", "change"),
        new("Driver", "num", "name", "team", "badge", "irchange"),
        new("Gaps", "gap", "int", "gapme", "laps", "trend"),
        new("Lap times", "last", "best", "avglap", "delta"),
        new("Race", "lapsdone", "inc"),
        new("Pit", "pit", "out", "stint"),
    };

    public static readonly HudCategory[] StripCategories =
    {
        new("Session", "session", "remain", "lapsleft", "lap", "clock", "trackname", "cars"),
        new("Position", "pos", "cpos", "classpos", "leader", "ahead", "behind"),
        new("Lap times", "last", "best", "delta", "fastest", "classFastest"),
        new("Car and fuel", "fuel", "fuellevel", "bias", "inc"),
        new("Field", "sof", "csof"),
        new("Weather", "air", "track", "wind", "humidity", "wetness", "rain"),
    };

    public static Func<HudOptions, bool> Has(string key, string id) => o => o.Columns(key).Contains(id);

    public static readonly LanesSetting InfoBars = new("infobars", "Info bars", new[] { ("header", "Top"), ("footer", "Bottom") }, StripCategories);

    public static readonly MergedSetting MyRow = new("myRow", "My row", new[] { "highlightMe", "posBlock" }, new MergedChoice[]
    {
        new("Off", V(("highlightMe", false)), "Your row looks like the others"),
        new("Tint", V(("highlightMe", true), ("posBlock", false)), "Your row tinted in the accent color"),
        new("Tint + block", V(("highlightMe", true), ("posBlock", true)), "Your row tinted in the accent color, your position on an accent block"),
    });

    public static readonly MergedSetting FlagBorder = new("flagBorder", "Flag border", new[] { "flagEdge", "flagWidth" }, new MergedChoice[]
    {
        new("Off", V(("flagEdge", false))),
        new("2 px", V(("flagEdge", true), ("flagWidth", 2))),
        new("4 px", V(("flagEdge", true), ("flagWidth", 4))),
        new("6 px", V(("flagEdge", true), ("flagWidth", 6))),
    })
    {
        Extra = o => o.Bool("flagEdge") ? new MergedChoice($"{o.Int("flagWidth")} px", V(("flagEdge", true), ("flagWidth", o.Int("flagWidth")))) : null,
        Help = "A border in the flag's color while a yellow, red, blue, white or checkered flag is out",
    };

    public static readonly HudCategory[] NoCategories = Array.Empty<HudCategory>();
}
