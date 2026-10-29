using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class ResultEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Shows",
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("seconds"), O("position"), O("colors"), O("nameFormat"), O("awardChips")),
            new("show", "Shows", HudTab.Content, true, new PickerSetting("show", new HudCategory[]
            {
                new("Top", "checker", "gain", "class", "car", "ribbon"),
                new("Driver", "name", "carname", "track"),
                new("Chips", "rating", "incidents", "awards"),
                new("Stats", "best", "average", "consistency", "laps", "time", "strip"),
            })),
        },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("show", "gain,name,rating,best,consistency"), ("awardChips", 0))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("show", Widgets.ResultWidget.AllParts), ("awardChips", 6), ("seconds", 0))),
        },
    };
}
