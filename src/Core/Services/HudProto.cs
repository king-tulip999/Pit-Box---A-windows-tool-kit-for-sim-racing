using System.Drawing;
using System.IO;
using System.Text.Json;
using Core.Config;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Editors;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.Services;

internal static class HudProto
{
    public const string Arg = "--hud-proto";
    private const float Tall = 2400;

    public static void Run(App app, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Export(folder);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch (IOException) { }
        }
        app.Shutdown();
    }

    private static void Export(string folder)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        for (double t = -1500; t <= 0; t += 0.25)
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
        var palette = HudPalette.Of(settings.Appearance);
        var widgets = new List<object>();
        foreach (var def in HudCatalog.All)
        {
            var config = new HudWidgetConfig { Id = "proto-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
            var size = def.Natural is { } natural ? natural(new HudOptions(def, config)) : Fit(def, config, race, settings, palette);
            config.W = size.Width;
            config.H = size.Height;
            using var painter = new HudPainter { Items = new(), Regions = new(), Overflow = new() };
            using var surface = new Core.Overlay.DibSurface();
            var g = surface.Ensure(new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
            painter.Simulated = true;
            painter.MarkSimulated = false;
            painter.IsPreview = true;
            painter.Begin(g, 1, size, palette, settings.Appearance);
            def.Create().Draw(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def });
            painter.FinishPanels();
            painter.End(surface.Dc, surface.Size);
            surface.Bitmap!.Save(Path.Combine(folder, def.Kind + ".png"));
            var editor = def.EditorDef;
            var keys = painter.Items!.Select(i => i.Key).Concat(painter.Regions!.Select(r => r.Id)).Distinct(StringComparer.OrdinalIgnoreCase);
            widgets.Add(new
            {
                kind = def.Kind,
                name = def.Name,
                w = size.Width,
                h = size.Height,
                items = painter.Items!.Select(i => new { key = i.Key, id = i.Id, x = i.Box.X, y = i.Box.Y, w = i.Box.Width, h = i.Box.Height }),
                regions = painter.Regions!.Select(r => new { id = r.Id, x = r.Box.X, y = r.Box.Y, w = r.Box.Width, h = r.Box.Height }),
                regionSetting = keys.ToDictionary(k => k, k => editor.SettingForRegion(k)),
                groups = editor.Groups.Select(gr => new { id = gr.Id, title = gr.Title, tab = gr.Tab.ToString(), settings = gr.Settings.Select(Describe) }),
                options = def.Options.Select(o => new
                {
                    key = o.Key, label = o.Label, kind = o.Kind.ToString(), def = o.Default, help = o.Help, min = o.Min, max = o.Max, unit = o.Unit,
                    choices = o.Choices.Select(c => new { id = c.Id, label = c.Label, help = c.Help }),
                }),
                perItem = PerItem(def, editor),
                values = def.Options.ToDictionary(o => o.Key, o => new HudOptions(def, config).Value(o.Key)),
            });
        }
        File.WriteAllText(Path.Combine(folder, "widgets.json"), JsonSerializer.Serialize(widgets, new JsonSerializerOptions { WriteIndented = false }));
    }

    private static object Describe(HudSetting s) => s switch
    {
        PickerSetting p => (object)new { type = "picker", id = s.Id, label = s.Label, keys = s.Keys, categories = p.Categories.Select(c => new { title = c.Title, ids = c.Ids }) },
        LanesSetting l => new { type = "lanes", id = s.Id, label = l.Label ?? l.Title, keys = s.Keys, lanes = l.Lanes.Select(x => new { key = x.Key, lane = x.Lane }), categories = l.Categories.Select(c => new { title = c.Title, ids = c.Ids }) },
        TogglesSetting t => new { type = "toggles", id = s.Id, label = t.Label ?? t.Title, keys = s.Keys },
        MergedSetting m => new { type = "merged", id = s.Id, label = m.Label ?? m.Title, keys = s.Keys, choices = m.Choices.Select(c => new { label = c.Label, tip = c.Tip, values = c.Values }) },
        _ => new { type = s is ClassFilterSetting ? "classes" : "option", id = s.Id, label = s.Label, keys = s.Keys },
    };

    private static Dictionary<string, Dictionary<string, string[]>> PerItem(HudWidgetDef def, HudEditorDef editor)
    {
        var result = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in def.Options.Where(o => o.Kind == HudOptionKind.Columns))
        {
            var byItem = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in o.Choices)
            {
                var without = new HudWidgetConfig { Kind = def.Kind };
                var with = new HudWidgetConfig { Kind = def.Kind };
                var start = new HudOptions(def, without).Columns(o.Key).Where(id => id != choice.Id).ToList();
                without.Options[o.Key] = string.Join(",", start);
                with.Options[o.Key] = string.Join(",", start.Append(choice.Id));
                var a = new HudOptions(def, without);
                var b = new HudOptions(def, with);
                var only = editor.Settings.Where(s => !s.Visible(a) && s.Visible(b)).Select(s => s.Id).ToArray();
                if (only.Length > 0) byItem[choice.Id] = only;
            }
            if (byItem.Count > 0) result[o.Key] = byItem;
        }
        return result;
    }

    private static SizeF Fit(HudWidgetDef def, HudWidgetConfig config, RaceState race, RaceHudSettings settings, HudPalette palette)
    {
        using var fit = new HudPainter { Overflow = new() };
        using var surface = new Core.Overlay.DibSurface();
        var widget = def.Create();
        bool Clean(float w, float h)
        {
            var g = surface.Ensure(new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h)));
            fit.Simulated = true;
            fit.IsPreview = true;
            fit.Begin(g, 1, new SizeF(w, h), palette, settings.Appearance);
            widget.Draw(fit, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def });
            fit.FinishPanels();
            return fit.Overflow!.Count == 0 && !fit.Spilled && fit.MinWidth <= w + 0.5f;
        }
        if (def.FixedSize) return new SizeF((float)def.W, (float)def.H);
        Clean((float)def.W, Tall);
        if (fit.Needed is { } need)
            return new SizeF(Math.Clamp(Math.Max(need.Width, fit.MinWidth), (float)def.MinW, (float)HudWidgetConfig.MaxSize), Math.Clamp(need.Height, (float)def.MinH, Tall));
        float width = Math.Max((float)def.W, fit.MinWidth);
        Clean(width, Tall);
        width = Math.Max(width, fit.MinWidth);
        float lo = (float)def.MinH, hi = Tall;
        while (hi - lo > 1)
        {
            float mid = MathF.Round((lo + hi) / 2);
            if (mid <= lo || mid >= hi) break;
            if (Clean(width, mid)) hi = mid; else lo = mid;
        }
        return new SizeF(width, MathF.Ceiling(hi));
    }
}
