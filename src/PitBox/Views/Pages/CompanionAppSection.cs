using System.Windows;
using System.Windows.Controls;
using PitBox.Controls;
using PitBox.Plugins;
using PitBox.Plugins.BuiltIn;
using PitBox.Services;
using PitBox.SimRacing;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed class CompanionAppSection : Tile
{
    private readonly AppServices _app;
    private readonly Func<SimRacingPlugin, CompanionApp?> _pick;
    private readonly string _about;
    private readonly StackPanel _stack = new SettingsFlow();
    private CompanionApp? _subscribed;

    public CompanionAppSection(AppServices app, Func<SimRacingPlugin, CompanionApp?> pick, string about)
    {
        _app = app;
        _pick = pick;
        _about = about;
        Style = Ui.Res<Style>("Card");
        Child = _stack;
    }

    private CompanionApp? Service => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin plugin ? _pick(plugin) : null;

    public void OnShown()
    {
        if (_subscribed is null && Service is { } service)
        {
            service.Changed += Build;
            service.WatchStatus(true);
            _subscribed = service;
        }
        Build();
    }

    public void OnHidden()
    {
        if (_subscribed is not null)
        {
            _subscribed.Changed -= Build;
            _subscribed.WatchStatus(false);
        }
        _subscribed = null;
    }

    private int _version;

    private async void Build()
    {
        int version = ++_version;
        if (Service is not { } app)
        {
            _stack.Children.Clear();
            return;
        }
        CompanionStatus status;
        try { status = await Task.Run(app.Status); }
        catch (Exception ex) { App.LogError(ex); status = CompanionStatus.Unknown; }
        if (version != _version) return;
        _stack.Children.Clear();
        var spec = app.Spec;
        var s = app.Settings;

        var heading = Ui.Text(spec.Name, "H2");
        heading.Margin = new Thickness(0, 0, 0, 8);
        _stack.Children.Add(heading);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var (text, soft, deep) = status switch
        {
            CompanionStatus.Running => ("Running", "SageSoft", "SageDeep"),
            CompanionStatus.NotRunning => ("Installed · not running", "SlateSoft", "SlateDeep"),
            CompanionStatus.NotInstalled => ("Not installed", "ApricotSoft", "ApricotDeep"),
            _ => ("Unable to determine whether it's running", "WarnSoft", "Warn"),
        };
        var chip = Ui.Chip(text, soft, deep);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        info.Children.Add(chip);
        var where = new TextBlock
        {
            Text = app.ExePath ?? $"{CompanionApp.Upper(spec.Program)} wasn't found on this PC. {_about} Install it from the official website, then come back here.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 10, 0), MaxWidth = 454, HorizontalAlignment = HorizontalAlignment.Left,
        };
        TextCase.SetVerbatim(where, app.ExePath is not null);
        info.Children.Add(where);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var visit = Ui.LabeledButton("I.External", spec.Host, () => Ui.OpenUrl(spec.Site), "GhostButton");
        visit.ToolTip = $"Open {spec.Site}";
        visit.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(visit);
        if (status == CompanionStatus.NotInstalled)
        {
            actions.Children.Add(Ui.LabeledButton("I.Folder", "Choose the program…", () =>
            {
                if (ProgramPicker.BrowseExe() is { } path) app.SetCustomPath(path);
            }));
            var site = Ui.LabeledButton("I.Download", $"Open the {spec.Name} website", () => Ui.OpenUrl(spec.Website), "PrimaryButton");
            site.Margin = new Thickness(6, 0, 0, 0);
            actions.Children.Add(site);
        }
        else
        {
            var check = Ui.IconButton("I.Refresh", "Check again", app.Refresh);
            check.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Add(check);
            bool running = status == CompanionStatus.Running;
            var launch = Ui.LabeledButton(running ? "I.Check" : "I.Play", running ? "Running" : $"Launch {spec.Name}", () =>
            {
                app.Launch();
                _ = Task.Delay(2500).ContinueWith(_ => { if (_subscribed is not null) Build(); }, TaskScheduler.FromCurrentSynchronizationContext());
            }, "PrimaryButton");
            launch.IsEnabled = !running;
            actions.Children.Add(launch);
            if (running)
            {
                Button stop = null!;
                stop = Ui.LabeledButton("I.Stop", "Stop", async () =>
                {
                    if (Window.GetWindow(this) is MainWindow window
                        && !await window.Confirm($"Stop {spec.Program}?", "Pit-Box closes it, and ends it if it doesn't close by itself. Anything it's doing now is cut off.", "Stop", danger: true))
                        return;
                    stop.IsEnabled = false;
                    await app.CloseAsync();
                }, "DangerButton");
                stop.ToolTip = $"Close {spec.Program}";
                stop.Margin = new Thickness(Ui.GroupGap, 0, 0, 0);
                actions.Children.Add(stop);
            }
        }
        _stack.Children.Add(info);
        _stack.Children.Add(actions);

        _stack.Children.Add(Hairline.Rule(new Border { Background = Ui.Res<System.Windows.Media.Brush>("Line"), Margin = new Thickness(0, 11, 0, 3) }));
        _stack.Children.Add(Ui.SettingRow("Start with Pit-Box", "When Pit-Box starts (skipped if it's already running).",
            Ui.Switch(s.LaunchWithPitBox, v => { s.LaunchWithPitBox = v; _app.Save(); })));
        _stack.Children.Add(Ui.SettingRow("Start with iRacing", "When iRacing starts (skipped if it's already running).",
            Ui.Switch(s.LaunchWithIracing, v => { s.LaunchWithIracing = v; _app.Save(); app.UpdateWatch(); })));
        if (spec == CompanionApp.TradingPaints)
        {
            var sim = _app.Config.SimRacing;
            _stack.Children.Add(Ui.SettingRow("Car pictures in your paint", "Drawn by iRacing while it's open; a new paint shows once iRacing has run.",
                Ui.Switch(sim.PaintedCarPictures, v =>
                {
                    sim.PaintedCarPictures = v;
                    _app.Save();
                    if (_app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin plugin) plugin.Liveries?.SetActive(LiveryPictures.On(_app));
                })));
        }
        _stack.Children.Add(new TextBlock
        {
            Text = $"Pit-Box starts {spec.Program} the normal way and never changes or updates it. It keeps running after Pit-Box quits.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0),
        });
    }
}
