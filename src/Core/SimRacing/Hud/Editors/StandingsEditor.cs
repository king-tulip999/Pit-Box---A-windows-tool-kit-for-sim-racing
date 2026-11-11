using static Core.SimRacing.Hud.Editors.FieldSettings;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class StandingsEditor
{
    private static readonly MergedSetting TitleRow = new("titleRow", "Title row", new[] { "title", "progress" }, new MergedChoice[]
    {
        new("Off", V(("title", false))),
        new("On", V(("title", true), ("progress", false)), "Session name and time or laps left"),
        new("On + progress", V(("title", true), ("progress", true)), "With a session progress bar under it"),
    });

    private static readonly MergedSetting Grouping = new("grouping", "Group by class", new[] { "group", "classOrder" }, new MergedChoice[]
    {
        new("Off", V(("group", false)), "One list, overall positions"),
        new("By leader", V(("group", true), ("classOrder", "position")), "Multiclass: one block per class, in their leaders' order"),
        new("Fastest first", V(("group", true), ("classOrder", "fastest")), "Multiclass: one block per class, the fastest class first"),
    });

    private static bool Grouped(HudOptions o) => o.Bool("group");

    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("layout", "Table", HudTab.Layout, true, TitleRow, O("showHeader"), O("stripes"), Grouping, O("divider")),
            new("rows", "Rows", HudTab.Layout, true, MyRow, O("top"), O("around"), O("otherTop") with { ShowIf = Grouped, InlineHelp = true }, O("otherRows") with { ShowIf = Grouped, InlineHelp = true }, O("nameFormat"), O("noDigits"), O("proAm"),
                O("badgeFormat") with { ShowIf = Has("columns", "badge") }),
            new("timing", "Timing", HudTab.Layout, false, O("gapTo"), O("timeFormat"), O("decimals"), O("avgLaps") with { ShowIf = Has("columns", "avglap") }),
            new("pit", "Pit", HudTab.Layout, false, O("pitShow") with { ShowIf = Has("columns", "pit") }, O("pitTime") with { ShowIf = Has("columns", "pit") }),
            new("colors", "Colors and flags", HudTab.Layout, false, FlagBorder, O("marks")),
            new("columns", "Columns", HudTab.Content, true, new PickerSetting("columns", ColumnCategories)),
            new("classhead", "Class headings", HudTab.Content, true, O("classInfo") with { ShowIf = Grouped, Label = "Shows" }),
            new("infobars", "Info bars", HudTab.Content, true, InfoBars),
            new("cars", "Which cars", HudTab.Content, true, new ClassFilterSetting(), O("retired")),
        },
        Regions = new Dictionary<string, string>
        {
            ["title"] = "titleRow",
            ["headings"] = "showHeader",
            ["myrow"] = "myRow",
            ["classhead"] = "classInfo",
            ["divider"] = "divider",
            ["rows"] = "top",
        },
    };
}
