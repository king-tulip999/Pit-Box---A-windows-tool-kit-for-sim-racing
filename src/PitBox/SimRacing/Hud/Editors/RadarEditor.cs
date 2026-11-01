using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class RadarEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("range"), O("distances")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("distances", false))),
            new(HudPreset.Standard, V()),
        },
    };
}
