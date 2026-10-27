using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class TopographyLayer
{
    public const string ColorKey = "Slate";
    public const double UnitDips = 312;
    public const double Speed = 0.00004;
    public const int FramesPerSecond = 15;

    private readonly AppServices _app;
    private readonly Window _window;
    private readonly Rectangle _layer;
    private readonly FrameworkElement _track;
    private TopographyEffect? _effect;
    private bool _running;
    private double _z = Random.Shared.NextDouble() * 100;
    private bool _shaderFailed;

    public TopographyLayer(AppServices app, Window window, Rectangle layer, FrameworkElement track)
    {
        _app = app;
        _window = window;
        _layer = layer;
        _track = track;
        _layer.SizeChanged += (_, _) => Fit();
        _window.DpiChanged += (_, _) => Fit();
        _window.IsVisibleChanged += (_, _) => UpdateRunning();
        _window.StateChanged += (_, _) => UpdateRunning();
        ThemeManager.Changed += ApplyColors;
        Motion.WindowActiveChanged += Refresh;
        RenderCapability.TierChanged += (_, _) => Refresh();
        PixelShader.InvalidPixelShaderEncountered += (_, _) => { _shaderFailed = true; Refresh(); };
    }

    public bool Wanted => _app.Config.Ui.TopoBackground;

    public bool Supported => !_shaderFailed && !_app.Light && TopographyEffect.Available;

    public bool Showing => _effect is not null;

    public static Color ThemeColor => ThemeManager.ColorOf("TopoLine", ThemeManager.ColorOf(ColorKey, Colors.Teal));

    public Color LineColor => Theme.CustomPalette.Parse(_app.Config.Ui.TopoColor) ?? ThemeColor;

    public void Refresh()
    {
        bool on = Wanted && Supported;
        if (on && _effect is null)
        {
            _effect = new TopographyEffect { Z = _z };
            _layer.Effect = _effect;
            _layer.Visibility = Visibility.Visible;
            _track.Visibility = Visibility.Collapsed;
            Fit();
        }
        else if (!on && _effect is not null)
        {
            Pause();
            _layer.Effect = null;
            _effect = null;
            _layer.Visibility = Visibility.Collapsed;
            _track.SetResourceReference(UIElement.VisibilityProperty, "TrackVisibility");
        }
        ApplyColors();
        UpdateRunning();
    }

    private void ApplyColors()
    {
        if (_effect is null) return;
        _effect.Line = TopographyEffect.Vector(LineColor);
        bool dark = ThemeManager.Dark;
        _effect.Background = TopographyEffect.Vector(dark ? Colors.Black : ThemeManager.ColorOf("Bg", Colors.Black));
        _effect.Haze = dark ? 0 : 0.6;
        _effect.Strength = dark ? 1 : 1.6;
    }

    private void Fit()
    {
        if (_effect is null || PresentationSource.FromVisual(_layer)?.CompositionTarget is not { } target) return;
        var toDevice = target.TransformToDevice;
        _effect.Size = new Point(Math.Max(1, _layer.ActualWidth * toDevice.M11), Math.Max(1, _layer.ActualHeight * toDevice.M22));
        _effect.Unit = UnitDips * toDevice.M11;
    }

    private void UpdateRunning()
    {
        bool run = _effect is not null && _window.IsVisible && _window.WindowState != WindowState.Minimized && Motion.Enabled && Motion.WindowActive && !PcClass.Weak;
        if (run == _running) return;
        if (run) Play();
        else Pause();
    }

    private void Play()
    {
        _running = true;
        var drift = new DoubleAnimation(_z, _z + 256, TimeSpan.FromMilliseconds(256 / Speed)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(drift, FramesPerSecond);
        _effect!.BeginAnimation(TopographyEffect.ZProperty, drift);
    }

    private void Pause()
    {
        if (!_running) return;
        _running = false;
        if (_effect is null) return;
        _z = _effect.Z % 256;
        _effect.BeginAnimation(TopographyEffect.ZProperty, null);
        _effect.Z = _z;
    }
}
