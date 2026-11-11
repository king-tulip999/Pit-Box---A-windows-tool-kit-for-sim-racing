using System.Windows;
using System.Windows.Controls;
using Core.Audio;
using Core.Config;
using Core.Controls;
using Core.Plugins;
using Core.Plugins.BuiltIn;
using Core.Services;
using Core.SimRacing.Shakers;
using Core.SimRacing.Shakers.Games;
using Core.SimRacing.Telemetry;
using Core.Theme;

namespace Core.Views.Pages;

internal sealed class ShakersSection : Tile
{
    private readonly AppServices _app;
    private readonly StackPanel _stack = new();
    private readonly ContentControl _status = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private ShakerHost? _subscribed;
    private List<AudioDevice>? _devices;
    private string _builtFor = "";
    private long _listedAt = long.MinValue / 2;
    private int _listing;
    private const int FreshListMs = 2000;
    private bool _gamesOpen;

    public ShakersSection(AppServices app)
    {
        _app = app;
        Style = Ui.Res<Style>("Card");
        Child = _stack;
        Build();
    }

    public FrameworkElement TitleTools => _status;

    private ShakerHost? Host => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin plugin ? plugin.Shakers : null;

    private ShakerSettings S => _app.Config.SimRacing.Shakers;

    public void OnShown()
    {
        if (_subscribed is null && Host is { } host)
        {
            host.Changed += ShowStatus;
            _app.Telemetry.StatusChanged += OnStatus;
            _subscribed = host;
        }
        if (Signature() != _builtFor) Build();
        else ShowStatus();
        if (!_app.Demo && Environment.TickCount64 - _listedAt > FreshListMs) _ = ListDevices();
    }

    private async Task ListDevices()
    {
        int listing = ++_listing;
        _listedAt = Environment.TickCount64;
        var devices = await Task.Run(SafeList);
        if (listing != _listing || _devices is not null && devices.SequenceEqual(_devices)) return;
        _devices = devices;
        Build();
    }

    private string Signature() => System.Text.Json.JsonSerializer.Serialize(S, ConfigStore.JsonOptions);

    public void OnHidden()
    {
        if (_subscribed is null) return;
        _subscribed.Changed -= ShowStatus;
        _app.Telemetry.StatusChanged -= OnStatus;
        _subscribed = null;
    }

    private void OnStatus(SimStatus _) => Dispatcher.BeginInvoke(ShowStatus);

    private void Changed()
    {
        _builtFor = Signature();
        _app.Save();
        Host?.SettingsChanged();
    }

    private void Build()
    {
        _stack.Children.Clear();
        _builtFor = Signature();
        if (_devices is null)
        {
            _devices = _app.Demo ? new List<AudioDevice>() : SafeList();
            _listedAt = Environment.TickCount64;
        }
        ShowStatus();

        var outputs = Group("Output", first: true, columns: 2);
        var choices = new List<Choice> { new(null, "Choose an output…") };
        choices.AddRange(_devices.Select(d => new Choice(d.Id, d.Name)));
        if (S.DeviceId is { } id && _devices.All(d => d.Id != id)) choices.Add(new Choice(id, $"{S.DeviceName ?? "Saved output"} (not connected)"));
        var output = Line("Device", "The sound output your shaker's amplifier is plugged into. Pick one that nothing else plays through.",
            Ui.Combo(choices, S.DeviceId, c =>
            {
                if (c.Id == S.DeviceId) return;
                S.DeviceId = c.Id;
                S.DeviceName = c.Id is null ? null : c.Text;
                Changed();
                Dispatcher.BeginInvoke(Build);
            }, 240));
        Ui.Anchor(output, "simracing.shakers");
        outputs.Children.Add(output);
        var channels = new[] { ShakerChannel.Both, ShakerChannel.Left, ShakerChannel.Right };
        outputs.Children.Add(Line("Channel", "Left or right for one shaker on each side of a stereo amplifier.",
            Ui.Segmented(new[] { "Both", "Left", "Right" }, Array.IndexOf(channels, S.Channel), i => { S.Channel = channels[i]; Changed(); }, "shaker-channel")));
        outputs.Children.Add(Line("Volume", null, Ui.SliderWithValue(0, 100, S.Volume, "{0:0}%", v => { S.Volume = (int)v; Changed(); }, 240)));

        var sources = Group("Sources");
        var modes = new[] { ShakerMode.Effects, ShakerMode.GameAudio, ShakerMode.Off };
        sources.Children.Add(Line("In supported games", "iRacing and the games below. Off leaves the shakers to a game's own bass shaker output.",
            Ui.Segmented(new[] { "Effects", "Game audio", "Off" }, Array.IndexOf(modes, S.Mode), i =>
            {
                S.Mode = modes[i];
                Changed();
                Dispatcher.BeginInvoke(Build);
            }, "shaker-iracing")));
        if (S.Mode != ShakerMode.Off)
        {
            var iracing = Ui.SettingRow("iRacing", "Off leaves iRacing to its own bass shaker output. The other games still play.",
                Ui.Switch(S.IracingOn, v =>
                {
                    S.SetGameOn(ShakerSettings.IracingId, v);
                    Changed();
                }));
            Ui.Anchor(iracing, "simracing.shakers.iracing");
            sources.Children.Add(iracing);
            sources.Children.Add(SupportedGames());
        }
        var gamesBox = GamesBox();
        gamesBox.MaxWidth = double.PositiveInfinity;
        gamesBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        gamesBox.Margin = new Thickness(0, 6, 0, 0);
        Ui.NameFor(gamesBox, "Games");
        var gameList = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 8),
            Children =
            {
                new TextBlock { Text = "Games", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = "Program names, separated by commas.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) },
                gamesBox,
            },
        };
        var audioRows = AddGameAudio();
        void ShowUsed()
        {
            Use(gameList, S.OtherGames);
            foreach (var row in audioRows) Use(row, S.OtherGames || S.Mode == ShakerMode.GameAudio);
        }
        static void Use(FrameworkElement row, bool on)
        {
            row.IsEnabled = on;
            row.Opacity = on ? 1 : 0.45;
        }
        var games = Ui.SettingRow("Other games", "The low rumble from a listed game's own sound while it runs, also for a supported game that sends no telemetry. Music, chat and other programs aren't used.",
            Ui.Switch(S.OtherGames, v =>
            {
                S.OtherGames = v;
                Changed();
                ShowUsed();
            }));
        Ui.Anchor(games, "simracing.shakers.games");
        sources.Children.Add(games);
        sources.Children.Add(gameList);
        foreach (var row in audioRows) sources.Children.Add(row);
        ShowUsed();

        if (S.Mode != ShakerMode.Effects) return;
        _stack.Children.Add(Ui.SubHeader("Effects"));
        var effects = new EvenGrid { MinItemWidth = EffectMin, ColumnSteps = new[] { 1, 3, 5 }, ColumnGap = 8, RowGap = 8, Margin = new Thickness(0, 4, 0, 0) };
        effects.Children.Add(EffectTile(ShakerEffect.Engine, "Engine", "A hum that rises from idle to the red line and grows with the throttle.", S.Engine));
        effects.Children.Add(EffectTile(ShakerEffect.Shifts, "Gear shifts", "A short thump on every gear change.", S.Shifts));
        effects.Children.Add(EffectTile(ShakerEffect.Abs, "ABS", "Pulses while the ABS works (cars with ABS only).", S.Abs));
        effects.Children.Add(EffectTile(ShakerEffect.Road, "Curbs and bumps", "From the car's vertical movement: road texture, curbs and landings.", S.Road));
        effects.Children.Add(EffectTile(ShakerEffect.Limiter, "Rev limiter", "A buzz on the limiter or at the red line.", S.Limiter));
        _stack.Children.Add(effects);

        var pitch = Group("Engine pitch");
        pitch.Children.Add(Line("Lowest", "At idle, and for shifts and ABS.",
            Ui.SliderWithValue(ShakerSettings.MinHz, ShakerSettings.MaxHz, S.EngineLowHz, v => $"{v:0} {Units.Hz}", v =>
            {
                S.EngineLowHz = (int)v;
                S.EngineHighHz = Math.Max(S.EngineHighHz, S.EngineLowHz);
                Changed();
            }, 240)));
        pitch.Children.Add(Line("Highest", "At the red line and on the rev limiter.",
            Ui.SliderWithValue(ShakerSettings.MinHz, ShakerSettings.MaxHz, S.EngineHighHz, v => $"{v:0} {Units.Hz}", v =>
            {
                S.EngineHighHz = Math.Max((int)v, S.EngineLowHz);
                Changed();
            }, 240)));
    }

    private const double EffectMin = 200;

    private StackPanel SupportedGames()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var list = new StackPanel { Visibility = _gamesOpen ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(3, 3, 0, 0) };
        var toggle = Ui.FoldHead("Supported games", _gamesOpen, out var chevron, out var count);
        void Count() => count.Text = $"Supported games ({ShakerGames.All.Count(g => S.GameOn(g.Id))} of {ShakerGames.All.Count} on)";
        Count();
        toggle.Click += (_, _) =>
        {
            _gamesOpen = list.Visibility != Visibility.Visible;
            list.Visibility = _gamesOpen ? Visibility.Visible : Visibility.Collapsed;
            Ui.SetFold(chevron, _gamesOpen);
        };
        Ui.Anchor(toggle, "simracing.shakers.supported");
        var flow = new SettingsFlow();
        foreach (var game in ShakerGames.All)
        {
            FrameworkElement? portRow = null;
            if (game.UsesPort)
            {
                var port = new NumberBox(S.Port(game.Id, game.Port), "", 64) { Minimum = ShakerSettings.MinPort, Maximum = ShakerSettings.MaxPort };
                port.ValueChanged += v =>
                {
                    int p = (int)v;
                    if (p == game.Port) S.Ports.Remove(game.Id);
                    else S.Ports[game.Id] = p;
                    Changed();
                };
                portRow = Ui.SettingRow($"{game.Name} port", null, port);
                portRow.Margin = new Thickness(24, 0, 0, 4);
                portRow.Visibility = S.GameOn(game.Id) ? Visibility.Visible : Visibility.Collapsed;
            }
            flow.Children.Add(Ui.SettingRow(game.Name, game.Setup, Ui.Switch(S.GameOn(game.Id), v =>
            {
                S.SetGameOn(game.Id, v);
                if (portRow is not null) portRow.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
                Count();
                Changed();
            })));
            if (portRow is not null) flow.Children.Add(portRow);
        }
        list.Children.Add(flow);
        panel.Children.Add(toggle);
        panel.Children.Add(list);
        return panel;
    }

    private SettingsFlow Group(string title, bool first = false, int columns = 3)
    {
        _stack.Children.Add(Ui.SubHeader(title, first));
        var flow = new SettingsFlow { MaxColumns = columns };
        _stack.Children.Add(flow);
        return flow;
    }

    private FrameworkElement[] AddGameAudio()
    {
        var strength = Ui.SliderWithValue(0, 100, S.GameAudioStrength, v => v <= 0 ? "Off" : $"{v:0}%", v => { S.GameAudioStrength = (int)v; Changed(); }, 160);
        var cutoff = Ui.SliderWithValue(ShakerSettings.MinCutoffHz, ShakerSettings.MaxHz, S.GameAudioCutoffHz, v => $"{v:0} {Units.Hz}", v => { S.GameAudioCutoffHz = (int)v; Changed(); }, 160);
        Ui.NameFor(strength, "Game audio strength");
        Ui.NameFor(cutoff, "Rumble below");
        var row = new Grid { Height = 40 };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        void Put(FrameworkElement e, int column, double right)
        {
            e.VerticalAlignment = VerticalAlignment.Center;
            e.Margin = new Thickness(0, 0, right, 0);
            Grid.SetColumn(e, column);
            row.Children.Add(e);
        }
        Put(Label("Game audio strength"), 0, 14);
        Put(strength, 1, 14);
        Put(Label("Rumble below"), 3, 14);
        var note = TextFit.TipWhenTrimmed(new TextBlock { Text = "Only game sound below this pitch reaches the shakers.", Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        Put(note, 4, 14);
        Put(cutoff, 5, 0);
        return [row];
    }

    private static TextBlock Label(string text) => new() { Text = text, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold };

    private static SettingRowGrid Line(string title, string? description, FrameworkElement control)
    {
        Ui.NameFor(control, title);
        var row = new SettingRowGrid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = Label(title);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        if (description is null) return row;
        var text = TextFit.TipWhenTrimmed(new TextBlock { Text = description, Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetRow(text, 1);
        Grid.SetColumnSpan(text, 2);
        row.Children.Add(text);
        return row;
    }

    private TextBox GamesBox()
    {
        var box = new TextBox { Text = string.Join(", ", S.Games), Tag = "BeamNG.drive.x64.exe", MinWidth = 220, MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
        box.LostKeyboardFocus += (_, _) =>
        {
            var games = ShakerSettings.ParseGames(box.Text);
            if (games.SequenceEqual(S.Games)) return;
            S.Games = games;
            box.Text = string.Join(", ", games);
            Changed();
        };
        return box;
    }

    private Border EffectTile(ShakerEffect effect, string title, string description, ShakerEffectSettings x)
    {
        static string Shown(double v) => v <= 0 ? "Off" : $"{v:0}%";
        var head = new DockPanel();
        var test = Ui.IconButton("I.Play", $"Try it ({ShakerMixer.TestSeconds:0.#} {Units.S})", () => Host?.Test(effect));
        test.IsEnabled = !_app.Demo && S.DeviceId is not null;
        test.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(test, Dock.Right);
        head.Children.Add(test);
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        head.Children.Add(name);

        var slider = new Slider { Minimum = 0, Maximum = 100, Value = x.Strength, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        SliderFace.SetText(slider, Shown(x.Strength));
        slider.ValueChanged += (_, e) =>
        {
            SliderFace.SetText(slider, Shown(e.NewValue));
            x.Strength = (int)e.NewValue;
            Changed();
        };
        var strength = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        strength.Children.Add(slider);

        var body = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(strength, Dock.Bottom);
        body.Children.Add(head);
        body.Children.Add(strength);
        body.Children.Add(new TextBlock { Text = description, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        var tile = Hairline.Frame(new Border { Padding = new Thickness(12, 9, 10, 10), Child = body });
        tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        tile.SetResourceReference(Border.BorderBrushProperty, "Line");
        return tile;
    }

    private void ShowStatus()
    {
        var host = Host;
        var (text, soft, deep) = _app.Demo ? ("Silent in demo mode", "SlateSoft", "SlateDeep")
            : host?.Problem is { } problem ? ($"Can't play: {problem}", "WarnSoft", "Warn")
            : S.DeviceId is null ? ("Choose an output to start", "ApricotSoft", "ApricotDeep")
            : host?.Playing == true && host.Source == ShakerSource.GameAudio ? ($"Playing game audio from “{host.Game}”", "SageSoft", "SageDeep")
            : host?.Playing == true && host.Game is { } playing ? ($"Playing effects for “{playing}”", "SageSoft", "SageDeep")
            : host?.Playing == true ? ("Playing effects", "SageSoft", "SageDeep")
            : host?.WaitingFor is { } waiting ? ($"“{waiting.Name}” is running but sends no telemetry", "ApricotSoft", "ApricotDeep")
            : (Waiting(), "SlateSoft", "SlateDeep");
        var chip = Ui.Chip(text, soft, deep);
        chip.Margin = new Thickness(0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        _status.Content = chip;
    }

    private string Waiting() => (S.Mode != ShakerMode.Off, S.OtherGames) switch
    {
        (true, _) => "Waiting for a game",
        (false, true) => "Waiting for a listed game",
        _ => "Off in every game",
    };

    private static List<AudioDevice> SafeList()
    {
        try { return AudioManager.List(EDataFlow.Render); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            App.LogError(ex);
            return new List<AudioDevice>();
        }
    }
}
