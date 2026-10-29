using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class WindEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("turn"), O("colors"), O("speedUnit"), O("unit"), O("title")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("unit", true), ("title", true))),
        },
    };
}
