using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class MapEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("zoom"), O("rotate"), O("turns"), O("positions"), O("pit")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Standard, V()),
        },
    };
}
