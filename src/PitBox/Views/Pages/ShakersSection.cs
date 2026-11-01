using System.Windows;
using System.Windows.Controls;
using PitBox.Audio;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Plugins;
using PitBox.Plugins.BuiltIn;
using PitBox.Services;
using PitBox.SimRacing.Shakers;
using PitBox.SimRacing.Telemetry;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed class ShakersSection : Tile
{
    private readonly AppServices _app;
    private readonly StackPanel _stack = new SettingsFlow();
    private readonly ContentControl _status = new();
    private ShakerHost? _subscribed;
    private List<AudioDevice>? _devices;

    public ShakersSection(AppServices app)
    {
        _app = app;
        Style = Ui.Res<Style>("Card");
        Child = _stack;
        Build();
    }

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
        _devices = null;
        Build();
    }

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
        _app.Save();
        Host?.SettingsChanged();
    }

    private void Build()
    {
        _stack.Children.Clear();
        _devices ??= _app.Demo ? new List<AudioDevice>() : SafeList();
        _stack.Children.Add(_status);
        ShowStatus();

        var choices = new List<Choice> { new(null, "Choose an output…") };
        choices.AddRange(_devices.Select(d => new Choice(d.Id, d.Name)));
        if (S.DeviceId is { } id && _devices.All(d => d.Id != id)) choices.Add(new Choice(id, $"{S.DeviceName ?? "Saved output"} (not connected)"));
        var output = Ui.SettingRow("Output", "The sound output your shaker's amplifier is plugged into. Pick one that nothing else plays through.",
            Ui.Combo(choices, S.DeviceId, c =>
            {
                if (c.Id == S.DeviceId) return;
                S.DeviceId = c.Id;
                S.DeviceName = c.Id is null ? null : c.Text;
                Changed();
                Dispatcher.BeginInvoke(Build);
            }, 220));
        Ui.Anchor(output, "simracing.shakers");
        _stack.Children.Add(output);

        var channels = new[] { ShakerChannel.Both, ShakerChannel.Left, ShakerChannel.Right };
        _stack.Children.Add(Ui.SettingRow("Channel", "Left or right for one shaker on each side of a stereo amplifier.",
            Ui.Segmented(new[] { "Both", "Left", "Right" }, Array.IndexOf(channels, S.Channel), i => { S.Channel = channels[i]; Changed(); }, "shaker-channel")));
        _stack.Children.Add(Ui.SettingRow("Volume", null, Ui.SliderWithValue(0, 100, S.Volume, "{0:0}%", v => { S.Volume = (int)v; Changed(); }, 120)));

        AddRule();
        var modes = new[] { ShakerIracing.Effects, ShakerIracing.GameAudio, ShakerIracing.Off };
        _stack.Children.Add(Ui.SettingRow("In iRacing", "Off leaves the shakers to iRacing's own bass shaker output.",
            Ui.Segmented(new[] { "Effects", "Game audio", "Off" }, Array.IndexOf(modes, S.InIracing), i =>
            {
                S.InIracing = modes[i];
                Changed();
                Dispatcher.BeginInvoke(Build);
            }, "shaker-iracing")));
        var games = Ui.SettingRow("Other games", "The low rumble from a listed game's own sound while it runs. Music, chat and other programs aren't used.",
            Ui.Switch(S.OtherGames, v =>
            {
                S.OtherGames = v;
                Changed();
                Dispatcher.BeginInvoke(Build);
            }));
        Ui.Anchor(games, "simracing.shakers.games");
        _stack.Children.Add(games);
        if (S.OtherGames) _stack.Children.Add(Ui.SettingRow("Games", "Program names, separated by commas.", GamesBox()));
        if (S.OtherGames || S.InIracing == ShakerIracing.GameAudio) AddGameAudio();

        if (S.InIracing != ShakerIracing.Effects) return;
        AddRule();
        AddEffect(ShakerEffect.Engine, "Engine", "A hum that rises from idle to the red line and grows with the throttle.", S.Engine);
        AddEffect(ShakerEffect.Shifts, "Gear shifts", "A short thump on every gear change.", S.Shifts);
        AddEffect(ShakerEffect.Abs, "ABS", "Pulses while the ABS works (cars with ABS only).", S.Abs);
        AddEffect(ShakerEffect.Road, "Kerbs and bumps", "From the car's vertical movement: road texture, kerbs and landings.", S.Road);
        AddEffect(ShakerEffect.Limiter, "Rev limiter", "A buzz on the limiter or at the red line.", S.Limiter);

        AddRule();
        _stack.Children.Add(Ui.SettingRow("Lowest pitch", "The engine at idle, shifts and ABS.",
            Ui.SliderWithValue(ShakerSettings.MinHz, ShakerSettings.MaxHz, S.EngineLowHz, v => $"{v:0} {Units.Hz}", v =>
            {
                S.EngineLowHz = (int)v;
                S.EngineHighHz = Math.Max(S.EngineHighHz, S.EngineLowHz);
                Changed();
            }, 120)));
        _stack.Children.Add(Ui.SettingRow("Highest pitch", "The engine at the red line and the rev limiter.",
            Ui.SliderWithValue(ShakerSettings.MinHz, ShakerSettings.MaxHz, S.EngineHighHz, v => $"{v:0} {Units.Hz}", v =>
            {
                S.EngineHighHz = Math.Max((int)v, S.EngineLowHz);
                Changed();
            }, 120)));
    }

    private void AddRule() =>
        _stack.Children.Add(Hairline.Rule(new Border { Background = Ui.Res<System.Windows.Media.Brush>("Line"), Margin = new Thickness(0, 11, 0, 3) }));

    private void AddGameAudio()
    {
        _stack.Children.Add(Ui.SettingRow("Game audio strength", null,
            Ui.SliderWithValue(0, 100, S.GameAudioStrength, v => v <= 0 ? "Off" : $"{v:0}%", v => { S.GameAudioStrength = (int)v; Changed(); }, 120)));
        _stack.Children.Add(Ui.SettingRow("Rumble below", "Only game sound below this pitch reaches the shakers.",
            Ui.SliderWithValue(ShakerSettings.MinCutoffHz, ShakerSettings.MaxHz, S.GameAudioCutoffHz, v => $"{v:0} {Units.Hz}", v => { S.GameAudioCutoffHz = (int)v; Changed(); }, 120)));
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

    private void AddEffect(ShakerEffect effect, string title, string description, ShakerEffectSettings x)
    {
        var slider = Ui.SliderWithValue(0, 100, x.Strength, v => v <= 0 ? "Off" : $"{v:0}%", v => { x.Strength = (int)v; Changed(); }, 110);
        var test = Ui.IconButton("I.Play", $"Try it ({ShakerMixer.TestSeconds:0.#} {Units.S})", () => Host?.Test(effect));
        test.Margin = new Thickness(6, 0, 0, 0);
        test.IsEnabled = !_app.Demo && S.DeviceId is not null;
        slider.Children.Add(test);
        _stack.Children.Add(Ui.SettingRow(title, description, slider));
    }

    private void ShowStatus()
    {
        var host = Host;
        var (text, soft, deep) = _app.Demo ? ("Silent in demo mode", "SlateSoft", "SlateDeep")
            : host?.Problem is { } problem ? ($"Can't play: {problem}", "WarnSoft", "Warn")
            : S.DeviceId is null ? ("Choose an output to start", "ApricotSoft", "ApricotDeep")
            : host?.Playing == true && host.Source == ShakerSource.GameAudio ? ($"Playing game audio from “{host.Game}”", "SageSoft", "SageDeep")
            : host?.Playing == true ? ("Playing iRacing effects", "SageSoft", "SageDeep")
            : (Waiting(), "SlateSoft", "SlateDeep");
        var chip = Ui.Chip(text, soft, deep);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        chip.Margin = new Thickness(0, 0, 0, 6);
        _status.Content = chip;
    }

    private string Waiting() => (S.InIracing != ShakerIracing.Off, S.OtherGames) switch
    {
        (true, true) => "Waiting for iRacing or a game",
        (true, false) => "Waiting for iRacing",
        (false, true) => "Waiting for a game",
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
