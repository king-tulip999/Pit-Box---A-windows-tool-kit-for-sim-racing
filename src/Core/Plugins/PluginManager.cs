using Core.Services;

namespace Core.Plugins;

internal sealed class PluginManager
{
    private readonly AppServices _app;

    public IReadOnlyList<Plugin> All { get; }

    public event Action? Changed;

    public PluginManager(AppServices app)
    {
        _app = app;
        All = (_discovery ?? Task.Run(PluginTypes)).GetAwaiter().GetResult()
            .Select(t => (Plugin)Activator.CreateInstance(t)!)
            .Where(p => p.App == AppIdentity.Kind)
            .OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
    }

    private static Task<List<Type>>? _discovery;

    public static void DiscoverEarly() => _discovery ??= Task.Run(PluginTypes);

    private static List<Type> PluginTypes() => typeof(Plugin).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(Plugin)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
        .ToList();

    public IEnumerable<Plugin> Active => All.Where(p => p.IsActive);

    public Plugin? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public bool IsActive(string id) => Find(id)?.IsActive == true;

    public (Plugin Plugin, PluginPage Page)? PageOwner(string key)
    {
        foreach (var plugin in All)
            foreach (var page in plugin.Pages)
                if (page.Key == key)
                    return (plugin, page);
        return null;
    }

    public void StartAll()
    {
        foreach (var plugin in All)
        {
            TryStart(plugin);
            if (PerfProbe.File is not null) PerfProbe.Mark("plugin_" + plugin.Id);
        }
        Changed?.Invoke();
    }

    public void RaiseChanged()
    {
        _app.Hotkeys.RegisterAll();
        Changed?.Invoke();
    }

    internal static List<Plugin> InNavOrder(IEnumerable<Plugin> plugins, IReadOnlyList<string> navOrder)
    {
        int Rank(Plugin plugin)
        {
            int best = int.MaxValue;
            foreach (var key in Keys(plugin))
                for (int i = 0; i < navOrder.Count && i < best; i++)
                    if (navOrder[i] == key) { best = i; break; }
            return best;
        }
        return plugins.OrderBy(Rank).ToList();
    }

    internal static IEnumerable<string> Keys(Plugin plugin) =>
        plugin.Pages.Count > 0 ? plugin.Pages.Select(p => p.Key) : new[] { PluginKeyPrefix + plugin.Id };

    public const string PluginKeyPrefix = "plugin:";

    public void StopAll()
    {
        foreach (var plugin in All.Reverse())
        {
            try { plugin.Stop(_app); }
            catch (Exception ex) { App.LogError(ex); }
        }
    }

    private void TryStart(Plugin plugin)
    {
        try
        {
            plugin.Start(_app);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            try { plugin.Stop(_app); } catch { }
            _app.Notify($"{plugin.Name} couldn't start", ex.Message, NoticeKind.Error, important: true);
        }
    }
}
