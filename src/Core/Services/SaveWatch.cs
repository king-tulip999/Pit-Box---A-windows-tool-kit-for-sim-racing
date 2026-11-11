using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Config;

namespace Core.Services;

internal static class SaveWatch
{
    public static bool On { get; private set; }

    private static string? _saved;

    public static void Start(AppServices app)
    {
        On = true;
        _saved = Json(app.Config);
    }

    public static void Rebase(AppServices app)
    {
        On = true;
        app.FlushPendingSave();
        _saved = Json(app.Config);
    }

    public static void Saved(AppConfig config)
    {
        if (On) _saved = Json(config);
    }

    public static string? Unsaved(AppServices app)
    {
        if (!On || _saved is null) return null;
        app.FlushPendingSave();
        string now = Json(app.Config);
        if (now == _saved) return null;
        string? path = FirstDifference(JsonNode.Parse(_saved), JsonNode.Parse(now), "");
        _saved = now;
        return path ?? "(order only)";
    }

    private static string Json(AppConfig config)
    {
        config.StoreSections();
        return JsonSerializer.Serialize(config, ConfigStore.JsonOptions);
    }

    private static string? FirstDifference(JsonNode? a, JsonNode? b, string path)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
            {
                oa.TryGetPropertyValue(key, out var va);
                ob.TryGetPropertyValue(key, out var vb);
                if (FirstDifference(va, vb, path + "." + key) is { } found) return found;
            }
            return null;
        }
        if (a is JsonArray aa && b is JsonArray ab)
        {
            if (aa.Count != ab.Count) return $"{path} ({aa.Count} → {ab.Count} items)";
            for (int i = 0; i < aa.Count; i++)
                if (FirstDifference(aa[i], ab[i], $"{path}[{i}]") is { } found) return found;
            return null;
        }
        string sa = a?.ToJsonString() ?? "null", sb = b?.ToJsonString() ?? "null";
        if (sa == sb) return null;
        return $"{path}: {Short(sa)} → {Short(sb)}";
    }

    private static string Short(string text) => text.Length <= 60 ? text : text[..57] + "…";
}
