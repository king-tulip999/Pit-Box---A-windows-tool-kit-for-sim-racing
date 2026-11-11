using static Core.SimRacing.Hud.Editors.FieldSettings;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class RelativeEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("layout", "Table", HudTab.Layout, true, O("ahead"), O("behind"), O("showHeader"), O("stripes")),
            new("rows", "Rows", HudTab.Layout, true, MyRow, O("lapColors"), O("close"), O("nameFormat"), O("noDigits"), O("proAm"),
                O("badgeFormat") with { ShowIf = Has("columns", "badge") }),
            new("timing", "Timing", HudTab.Layout, false, O("timeFormat"), O("decimals"), O("avgLaps") with { ShowIf = Has("columns", "avglap") }),
            new("pit", "Pit", HudTab.Layout, false, O("pitShow") with { ShowIf = Has("columns", "pit") }, O("pitTime") with { ShowIf = Has("columns", "pit") }),
            new("colors", "Colors and flags", HudTab.Layout, false, FlagBorder, O("flagBanner"), O("marks")),
            new("columns", "Columns", HudTab.Content, true, new PickerSetting("columns", ColumnCategories)),
            new("infobars", "Info bars", HudTab.Content, true, InfoBars),
            new("cars", "Which cars", HudTab.Content, true, new ClassFilterSetting(), O("hidePits")),
        },
        Regions = new Dictionary<string, string>
        {
            ["headings"] = "showHeader",
            ["myrow"] = "myRow",
            ["flag"] = "flagBanner",
            ["rows"] = "ahead",
        },
    };
}
