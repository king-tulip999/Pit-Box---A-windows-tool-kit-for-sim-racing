using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class NotesEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("ahead"), O("onlyNext"), O("general")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("onlyNext", true), ("general", false))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("ahead", 500))),
        },
    };
}
