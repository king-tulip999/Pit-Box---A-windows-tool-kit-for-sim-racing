using System.Drawing;
using System.IO;
using PitBox.Config;
using PitBox.SimRacing.Hud;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.Services;

internal static class HudSheet
{
    public const string Arg = "--hud-sheet";
    public const string FramesArg = "--hud-frames";

    public static bool Running { get; private set; }

    public static void Run(App app, string folder, bool frames = false)
    {
        Running = true;
        try
        {
            Directory.CreateDirectory(folder);
            if (frames) RenderFrames(folder);
            else
            {
                Render(folder);
                Time(folder);
            }
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch { }
        }
        app.Shutdown();
    }

    private static void Render(string folder)
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
        Thread.Sleep(300);
        demo.Fill(frame, 0.25);
        frame.Sequence = ++seq;
        race.Update(frame);
        using var painter = new HudPainter();
        foreach (var name in new[] { "Default" })
        {
            var palette = HudPalette.Of(settings.Appearance);
            string dir = Path.Combine(folder, name);
            Directory.CreateDirectory(dir);
            foreach (var def in HudCatalog.All)
            {
                Save(def, def.W, def.H, Path.Combine(dir, def.Kind + ".png"));
                Save(def, def.MinW, def.MinH, Path.Combine(dir, def.Kind + "-min.png"));
            }
            var relative = HudCatalog.Find("relative")!;
            Save(relative, 460, relative.H, Path.Combine(dir, "relative-strips.png"), 1, ("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"));
            var flags = race.Frame.Flags;
            foreach (var (file, where, flag) in new[] { ("relative-flag-top", "top", RaceFlags.Yellow), ("relative-flag-checkered", "top", RaceFlags.Checkered), ("relative-flag-blue", "top", RaceFlags.Blue), ("relative-flag-meatball", "top", RaceFlags.Repair) })
            {
                race.Frame.Flags = flag;
                var (fl, ft, fr, fb) = HudFlags.Room(where);
                Save(relative, relative.W + fl + fr, relative.H + ft + fb, Path.Combine(dir, file + ".png"), 1, ("flagBanner", where), ("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"));
            }
            race.Frame.Flags = flags;

            var inputs = HudCatalog.Find("inputs")!;
            foreach (var (file, style, show) in new[] { ("inputs-lights", "lights", ""), ("inputs-trimmed", "trace", "throttle,brake,speed"), ("inputs-lights-trimmed", "lights", "throttle,brake,rpm") })
            {
                var config = new HudWidgetConfig { Options = { ["style"] = style } };
                if (show.Length > 0) config.Options["show"] = show;
                var size = inputs.Natural!(new HudOptions(inputs, config));
                Save(inputs, size.Width, size.Height, Path.Combine(dir, file + ".png"), 1, config.Options.Select(kv => (kv.Key, kv.Value)).ToArray());
            }

            foreach (var (kind, file, options) in new[]
            {
                ("goals", "goals-counters", new[] { ("look", "counters") }),
                ("goals", "goals-six", new[] { ("goals", "clean,consistent,near,under,total,minutes") }),
                ("goals", "goals-counters-six", new[] { ("look", "counters"), ("goals", "clean,consistent,near,under,total,minutes") }),
                ("result", "result-banner", new[] { ("look", "banner") }),
                ("result", "result-stamp", new[] { ("look", "stamp") }),
                ("result", "result-story", new[] { ("look", "story") }),
            })
            {
                var def = HudCatalog.Find(kind)!;
                var config = new HudWidgetConfig();
                foreach (var (key, value) in options) config.Options[key] = value;
                var size = def.Natural!(new HudOptions(def, config));
                Save(def, size.Width, size.Height, Path.Combine(dir, file + ".png"), 1, options);
            }

            var fuel = HudCatalog.Find("fuel")!;
            Save(fuel, fuel.W, 150, Path.Combine(dir, "fuel-pitexit.png"), 1, ("parts", "header,figures"), ("head", "togo,pitexit,stops,add"), ("top", "pitexit,level,laps"));
            Save(fuel, fuel.W, 150, Path.Combine(dir, "fuel-save.png"), 1, ("parts", "header,save,undercut"));
            var everyFuel = new[]
            {
                ("parts", "header,gauge,plan,save,figures,undercut,table,grid"), ("head", "togo,nextpit,stops,pitby,add,pitexit,level"),
                ("gridRows", "curr,avg,max,min,last"), ("top", "level,perlap,togo,laps,add,stops,window,pitexit,atflag,surplus,tofinish,max,last,time"),
            };
            foreach (var (look, columns) in new[] { ("full", "1"), ("full", "2"), ("full", "3"), ("compact", "3") })
            {
                var options = everyFuel.Append(("columns", columns)).Append(("exitLook", look)).ToArray();
                var size = Fitted(fuel, options);
                Save(fuel, size.Width, size.Height, Path.Combine(dir, $"fuel-every-{look}-{columns}col.png"), 1, options);
            }

            SizeF Fitted(HudWidgetDef def, (string Key, string Value)[] options)
            {
                var config = new HudWidgetConfig { Id = "fit-" + def.Kind, Kind = def.Kind };
                foreach (var (key, value) in options) config.Options[key] = value;
                using var fit = new HudPainter { Overflow = new() };
                using var surface = new PitBox.Overlay.DibSurface();
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
                const float Tall = 2400;
                Clean((float)def.W, Tall);
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

            foreach (var kind in new[] { "relative", "standings", "fuel" })
                Save(HudCatalog.Find(kind)!, HudCatalog.Find(kind)!.W, HudCatalog.Find(kind)!.H, Path.Combine(dir, kind + "@150.png"), 1.5f);

            foreach (var kind in new[] { "radar", "goals" })
                Save(HudCatalog.Find(kind)!, HudCatalog.Find(kind)!.W, HudCatalog.Find(kind)!.H, Path.Combine(dir, kind + "@250.png"), 2.5f);

            void Save(HudWidgetDef def, double w, double h, string path, float scale = 1, params (string Key, string Value)[] options)
            {
                var config = new HudWidgetConfig { Id = "sheet-" + def.Kind, Kind = def.Kind, W = w, H = h };
                foreach (var (key, value) in options) config.Options[key] = value;
                using var surface = new PitBox.Overlay.DibSurface();
                var g = surface.Ensure(new Size((int)Math.Round(w * scale), (int)Math.Round(h * scale)));
                {
                    painter.Simulated = true;
                    painter.MarkSimulated = false;
                    painter.IsPreview = true;
                    painter.Begin(g, scale, new SizeF((float)w, (float)h), palette, settings.Appearance);
                    var view = new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def };
                    def.Create().Draw(painter, view);
                    painter.FinishPanels();
                    painter.End(surface.Dc, surface.Size);
                }
                surface.Bitmap!.Save(path);
            }
        }
    }

    private const int TimedFrames = 600, WarmFrames = 60;

    private static void Time(string folder)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        double t = -1500;
        for (; t <= 0; t += 0.25) Step();
        var palette = HudPalette.Of(settings.Appearance);
        var lines = new List<string>();
        var clock = new System.Diagnostics.Stopwatch();
        double totalDraw = 0, totalUpdate = 0;
        long totalBytes = 0;
        using var painter = new HudPainter();
        using var surface = new PitBox.Overlay.DibSurface();
        foreach (var def in HudCatalog.All)
        {
            var config = new HudWidgetConfig { Id = "time-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
            var options = new HudOptions(def, config);
            var widget = def.Create();
            var size = new Size((int)Math.Round(def.W), (int)Math.Round(def.H));
            double draw = 0, update = 0;
            long bytes = 0;
            for (int i = 0; i < WarmFrames + TimedFrames; i++)
            {
                bool timed = i >= WarmFrames;
                long before = GC.GetAllocatedBytesForCurrentThread();
                clock.Restart();
                t += 1.0 / FrameRate;
                Step();
                double updated = clock.Elapsed.TotalMilliseconds;
                var g = surface.Ensure(size);
                painter.Simulated = true;
                painter.MarkSimulated = false;
                painter.Begin(g, 1, new SizeF((float)def.W, (float)def.H), palette, settings.Appearance);
                widget.Draw(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                double drawn = clock.Elapsed.TotalMilliseconds - updated;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (!timed) continue;
                draw += drawn;
                update += updated;
                bytes += allocated;
            }
            totalDraw += draw;
            totalUpdate += update;
            totalBytes += bytes;
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_{0}_draw_ms {1:0.000}", def.Kind, draw / TimedFrames));
            lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_{0}_alloc_kb {1:0.0}", def.Kind, bytes / 1024.0 / TimedFrames));
        }
        int count = HudCatalog.All.Count();
        lines.Insert(0, string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_race_update_ms {0:0.000}", totalUpdate / (TimedFrames * count)));
        lines.Insert(1, string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_draw_mean_ms {0:0.000}", totalDraw / (TimedFrames * count)));
        lines.Insert(2, string.Format(System.Globalization.CultureInfo.InvariantCulture, "hud_alloc_mean_kb {0:0.0}", totalBytes / 1024.0 / (TimedFrames * count)));
        File.WriteAllLines(Path.Combine(folder, "timing.txt"), lines);

        void Step()
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
    }

    private const int FrameRate = 60;
    private const double ClipSeconds = 12, FrameScale = 2.5, ShownFrom = 1.5, ShownFor = 3.5, Probe = 0.25, SearchFrom = 60, SearchTo = 1500;
    private static readonly HashSet<string> EveryFrame = new(StringComparer.OrdinalIgnoreCase) { "inputs", "radar", "map", "timing", "wind" };
    private static readonly Dictionary<string, double> Speeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["relative"] = 4, ["standings"] = 12, ["timing"] = 2, ["laps"] = 40, ["fuel"] = 20, ["session"] = 10, ["alerts"] = 3,
        ["traffic"] = 4, ["map"] = 4, ["tyres"] = 4, ["weather"] = 30, ["wind"] = 6, ["goals"] = 40, ["notes"] = 8, ["events"] = 30, ["block"] = 40,
    };

    private static void RenderFrames(string folder)
    {
        foreach (var def in HudCatalog.All)
        {
            double speed = Speeds.GetValueOrDefault(def.Kind, 1);
            double start = BusiestStart(def, speed) - ShownFrom * speed;
            var settings = new RaceHudSettings();
            var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
            var demo = new DemoTelemetry();
            var frame = new TelemetryFrame();
            long seq = 0;
            for (double t = start - 1500; t < start; t += 0.25) Step(t);
            var palette = HudPalette.Of(settings.Appearance);
            var config = new HudWidgetConfig { Id = "frames-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
            var options = new HudOptions(def, config);
            var widget = def.Create();
            using var painter = new HudPainter();
            using var surface = new PitBox.Overlay.DibSurface();
            var size = new Size((int)Math.Round(def.W * FrameScale), (int)Math.Round(def.H * FrameScale));
            string dir = Path.Combine(folder, def.Kind);
            Directory.CreateDirectory(dir);
            int every = EveryFrame.Contains(def.Kind) ? 1 : speed > 1 ? 2 : FrameRate / 15;
            int substeps = (int)Math.Ceiling(speed / FrameRate / 0.25);
            var saved = new List<string>();
            for (int i = 0; i <= ClipSeconds * FrameRate; i++)
            {
                for (int s = substeps; s >= 1; s--) Step(start + (i - (s - 1) / (double)substeps) * speed / FrameRate);
                var g = surface.Ensure(size);
                painter.Simulated = true;
                painter.MarkSimulated = false;
                painter.Begin(g, (float)FrameScale, new SizeF((float)def.W, (float)def.H), palette, settings.Appearance);
                widget.Draw(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def });
                painter.FinishPanels();
                painter.End(surface.Dc, surface.Size);
                if (i % every != 0) continue;
                string file = $"{saved.Count:0000}.png";
                surface.Bitmap!.Save(Path.Combine(dir, file));
                saved.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{{\"t\":{0:0.000},\"f\":\"{1}\"}}", i / (double)FrameRate, file));
            }
            File.WriteAllText(Path.Combine(dir, "frames.json"), $"{{\"kind\":\"{def.Kind}\",\"scale\":{FrameScale.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"speed\":{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"frames\":[{string.Join(",", saved)}]}}");

            void Step(double t)
            {
                demo.Fill(frame, t);
                frame.Sequence = ++seq;
                race.Update(frame);
            }
        }
    }

    private static double BusiestStart(HudWidgetDef def, double speed)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        double simTime = SearchFrom - 600;
        for (; simTime < SearchFrom; simTime += 0.25) Step(simTime);
        var palette = HudPalette.Of(settings.Appearance);
        var config = new HudWidgetConfig { Id = "busy-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
        var options = new HudOptions(def, config);
        var widget = def.Create();
        using var painter = new HudPainter();
        using var surface = new PitBox.Overlay.DibSurface();
        var size = new Size((int)Math.Round(def.W), (int)Math.Round(def.H));
        var previous = new int[size.Width * size.Height];
        var current = new int[previous.Length];
        var times = new List<double>();
        var changes = new List<double>();
        double step = Probe * speed;
        for (double at = SearchFrom; at < SearchTo; at += step)
        {
            for (; simTime <= at; simTime += 0.25) Step(simTime);
            var g = surface.Ensure(size);
            painter.Simulated = true;
            painter.MarkSimulated = false;
            painter.Begin(g, 1, new SizeF((float)def.W, (float)def.H), palette, settings.Appearance);
            widget.Draw(painter, new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def });
            painter.FinishPanels();
            painter.End(surface.Dc, surface.Size);
            var bitmap = surface.Bitmap!;
            var data = bitmap.LockBits(new Rectangle(Point.Empty, size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            for (int y = 0; y < size.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, current, y * size.Width, size.Width);
            bitmap.UnlockBits(data);
            int differing = 0;
            for (int i = 0; i < current.Length; i++) if (current[i] != previous[i]) differing++;
            times.Add(at);
            changes.Add(Math.Min(0.15, differing / (double)current.Length));
            (previous, current) = (current, previous);
        }
        int window = (int)Math.Round(ShownFor / Probe);
        double best = -1, bestAt = SearchFrom + ShownFrom * speed;
        for (int i = 1; i + window <= changes.Count; i++)
        {
            if (times[i] - ShownFrom * speed < SearchFrom - 590) continue;
            double score = 0;
            for (int j = i; j < i + window; j++) score += changes[j];
            if (score > best) (best, bestAt) = (score, times[i]);
        }
        return bestAt;

        void Step(double t)
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
    }
}
