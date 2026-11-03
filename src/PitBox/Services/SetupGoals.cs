using PitBox.Plugins;

namespace PitBox.Services;

internal sealed record SetupGoal(string Id, string Title, string Caption, IReadOnlyList<string>? Plugins, IReadOnlyList<string> Tools);

internal static class SetupGoals
{
    private static readonly string[] RaceTools = { "setups", "hud", "fov", "iracing", "tradingpaints", "garage61" };

    public static readonly IReadOnlyList<SetupGoal> All = new SetupGoal[]
    {
        new("overlays", "Overlays", "Only the iRacing Overlay, nothing else", new[] { PluginIds.SimRacing }, new[] { "hud" }),
        new("racing", "Sim racing", "Race tools and Session Stats", new[] { PluginIds.SimRacing, PluginIds.SessionStats }, RaceTools),
        new("pc", "PC tuning", "Fans, displays, windows, FPS overlay",
            new[] { PluginIds.FanControl, PluginIds.Displays, PluginIds.WindowProfiles, PluginIds.Overlay }, Array.Empty<string>()),
        new("everything", "Everything", "Every tool that's on by default", null, RaceTools),
    };

    public static (HashSet<string> Plugins, HashSet<string> Tools) Resolve(IEnumerable<SetupGoal> picked, IEnumerable<(string Id, bool ByDefault)> plugins)
    {
        var known = plugins.ToList();
        var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var goal in picked)
        {
            on.UnionWith(goal.Plugins ?? known.Where(p => p.ByDefault).Select(p => p.Id));
            tools.UnionWith(goal.Tools);
        }
        return (on, tools);
    }
}
