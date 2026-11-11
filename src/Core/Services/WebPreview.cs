using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Core.Config;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Web;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.Services;

internal static class WebPreview
{
    public const string Arg = "--web-preview";
    public const int Port = 8643;

    public static void Run(App app, string? secondsText)
    {
        double seconds = double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 300;
        var settings = new RaceHudSettings();
        settings.Normalize(HudPresets.DefaultLayouts, HudPresets.DefaultRules);
        var race = new RaceState(() => settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        demo.Fill(frame);
        long seq = 0;
        DemoTelemetry.WarmUp(race, new TelemetryFrame(), ref seq, frame.SessionTime);
        var server = new HudWebServer(HudWebRenderer.Overlays);
        if (server.Start(Port) is { } problem)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "velocita-web-preview.txt"), problem);
            app.Shutdown();
            return;
        }
        var renderer = new HudWebRenderer();
        var palette = HudPalette.Of(settings.Appearance);
        server.ThemeCss = HudWebRenderer.ThemeCss(palette);
        int dirty = 1;
        server.ClientsChanged += () => Volatile.Write(ref dirty, 1);
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) =>
        {
            if (clock.Elapsed.TotalSeconds > seconds)
            {
                timer.Stop();
                server.Dispose();
                renderer.Dispose();
                app.Shutdown();
                return;
            }
            demo.Fill(frame);
            frame.Sequence = ++seq;
            race.Update(frame);
            renderer.Render(server.Clients, frame, race, settings, settings.Layout(settings.ActiveLayout), palette, Interlocked.Exchange(ref dirty, 0) == 1);
        };
        timer.Start();
    }
}
