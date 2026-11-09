using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class LapsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("titleRow"), O("count"), O("fuel"), O("stats"), O("window") with { ShowIf = o => o.Bool("stats") }) },
    };
}
