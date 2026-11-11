using static Core.SimRacing.Hud.Editors.FieldSettings;
using static Core.SimRacing.Hud.Editors.HudEditorDef;

namespace Core.SimRacing.Hud.Editors;

internal static class FuelEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Sections",
        Groups = new HudGroup[]
        {
            new("look", "Title and edges", HudTab.Layout, true, O("title"), O("statusEdge")),
            new("arrange", "Columns", HudTab.Layout, true, O("columns") with { Label = "Side by side" }),
            new("rates", "Rates", HudTab.Layout, true, O("avgLaps") with { ShowIf = o => Has("parts", "grid")(o) && Has("gridRows", "avgN")(o) }),
            new("parts", "Sections", HudTab.Content, true, new PickerSetting("parts", NoCategories)),
            new("shows", "In the sections", HudTab.Content, true,
                new PickerSetting("head", NoCategories) { ShowIf = Has("parts", "header") },
                new PickerSetting("gridRows", NoCategories) { ShowIf = Has("parts", "grid") },
                new PickerSetting("gridCols", NoCategories) { ShowIf = Has("parts", "grid") },
                new PickerSetting("top", new HudCategory[]
                {
                    new("Tank", "level", "laps", "time"),
                    new("Rates", "perlap", "last", "max"),
                    new("To the flag", "togo", "tofinish", "surplus", "atflag"),
                    new("Stops", "add", "stops", "window", "pitexit"),
                }) { ShowIf = Has("parts", "figures") },
                new PickerSetting("cols", NoCategories) { ShowIf = Has("parts", "table") },
                new PickerSetting("rows", NoCategories) { ShowIf = Has("parts", "table") },
                O("exitLook") with { ShowIf = Has("parts", "undercut") }),
        },
        Regions = new Dictionary<string, string> { ["title"] = "title" },
    };
}
