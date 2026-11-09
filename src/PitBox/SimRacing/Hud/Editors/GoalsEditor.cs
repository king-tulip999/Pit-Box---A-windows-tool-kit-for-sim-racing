using static PitBox.SimRacing.Hud.Editors.HudEditorDef;

namespace PitBox.SimRacing.Hud.Editors;

internal static class GoalsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Goals",
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("titleRow"), O("look"), O("races")),
            new("goals", "Goals", HudTab.Content, true, O("goals"),
                O("cleanLaps") with { ShowIf = o => o.Columns("goals").Contains("clean") },
                O("consistentLaps") with { ShowIf = o => o.Columns("goals").Contains("consistent") },
                O("consistentWithin") with { ShowIf = o => o.Columns("goals").Contains("consistent") },
                O("nearLaps") with { ShowIf = o => o.Columns("goals").Contains("near") },
                O("nearWithin") with { ShowIf = o => o.Columns("goals").Contains("near") },
                O("underLaps") with { ShowIf = o => o.Columns("goals").Contains("under") },
                O("underTime") with { ShowIf = o => o.Columns("goals").Contains("under") },
                O("totalLaps") with { ShowIf = o => o.Columns("goals").Contains("total") },
                O("minutes") with { ShowIf = o => o.Columns("goals").Contains("minutes") }),
        },
    };
}
