using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class NotesEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("titleRow"), O("ahead"), O("onlyNext"), O("general")) },
    };
}
