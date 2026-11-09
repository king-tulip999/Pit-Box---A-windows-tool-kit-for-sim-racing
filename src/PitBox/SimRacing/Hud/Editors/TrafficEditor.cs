using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class TrafficEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("titleRow"), O("window"), O("max"), O("sameClass")) },
    };
}
