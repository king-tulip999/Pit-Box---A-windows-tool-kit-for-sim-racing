using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class InputsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("style", "Style", HudTab.Layout, true, O("style")),
            new("shows", "Shows", HudTab.Layout, true, O("show")),
        },
    };
}
