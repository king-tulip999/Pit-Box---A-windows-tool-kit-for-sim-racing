using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Config;

namespace PitBox.Views.Pages;

internal sealed partial class RaceHudWidgets
{
    private const double RiseStepMs = 60, RiseDistance = 8, RiseMs = 260, FoldMs = 320, CrossFadeMs = 300, BlinkMs = 260;

    private readonly Canvas _navLayer = new() { IsHitTestVisible = false };
    private RadioButton? _navChecked;
    private string? _navCheckedId;
    private Rect? _navFrom;
    private string? _navFromId;
    private RadioButton? _navLanding;
    private bool _navQuiet;
    private bool _filledOnce;
    private string? _enteredFor;
    private (string Id, bool On)? _toggled;

    private FrameworkElement NavWithLayer(FrameworkElement nav) => new Grid { Children = { _navLayer, nav } };

    private void BeginNav()
    {
        _navFrom = Glide.Of(_navChecked, _navLayer);
        _navFromId = _navCheckedId;
        _navLanding = null;
    }

    private void NavItem(RadioButton b, string id, bool on)
    {
        if (!on) return;
        if (_navFrom is not null && _navFromId != id && _filledOnce)
        {
            b.IsChecked = false;
            _navLanding = b;
        }
        _navChecked = b;
        _navCheckedId = id;
    }

    private void EndNav()
    {
        if (_navLanding is not { } target || _navFrom is not { } from) return;
        Glide.To(_navLayer, from, target, marker =>
        {
            marker.BorderThickness = new Thickness(3, 0, 0, 0);
            marker.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            marker.SetResourceReference(Border.BorderBrushProperty, "AccentText");
        }, () =>
        {
            _navQuiet = true;
            target.IsChecked = true;
            _navQuiet = false;
        });
    }

    private void RiseIn(Panel body)
    {
        string? shownFor = _general ? null : _selected;
        if (_filledOnce && shownFor != _enteredFor && Motion.Flourish)
            for (int i = 0; i < body.Children.Count; i++) Motion.Enter(body.Children[i], i * RiseStepMs, RiseDistance, RiseMs);
        _filledOnce = true;
        _enteredFor = shownFor;
    }

    private void FoldPreview(HudWidgetConfig w)
    {
        if (_preview?.Parent is not FrameworkElement host) return;
        if (_previewHidden is { } hidden) hidden.Visibility = w.Enabled ? Visibility.Collapsed : Visibility.Visible;
        if (_toggled is not { } toggled || toggled.Id != w.Id) return;
        _toggled = null;
        if (!Motion.Flourish) return;
        var scale = new ScaleTransform();
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        host.RenderTransform = scale;
        double line = 0.015;
        if (!toggled.On)
        {
            double shut = BlinkMs * 0.45;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Views.Reveal.Keys(BlinkMs, Motion.Ease, (0, 1), (shut, line), (BlinkMs, 1)));
            _previewHidden?.BeginAnimation(UIElement.OpacityProperty, Views.Reveal.Keys(BlinkMs + 160, Motion.Ease, (0, 0), (shut, 0), (BlinkMs + 160, 1)));
        }
        else
        {
            double open = FoldMs * 0.45;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Views.Reveal.Keys(FoldMs, Motion.Ease, (0, 0), (open, 1)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Views.Reveal.Keys(FoldMs, Motion.Ease, (0, line), (open, line), (FoldMs, 1)));
        }
    }

    private (ImageSource Source, double Width, double Height)? Snapshot()
    {
        if (_preview is not { Source: System.Windows.Media.Imaging.BitmapSource source } image) return null;
        var copy = source.Clone();
        copy.Freeze();
        return (copy, image.Width, image.Height);
    }

    private void CrossFade((ImageSource Source, double Width, double Height)? before)
    {
        if (before is not { } old || !Motion.Flourish || _preview?.Parent is not Grid host) return;
        var ghost = new Image { Source = old.Source, Width = old.Width, Height = old.Height, Stretch = Stretch.None, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapScalingMode(ghost, BitmapScalingMode.NearestNeighbor);
        host.Children.Add(ghost);
        var fade = Views.Reveal.Keys(CrossFadeMs, Motion.Ease, (0, 1), (CrossFadeMs, 0));
        fade.Completed += (_, _) => host.Children.Remove(ghost);
        ghost.BeginAnimation(UIElement.OpacityProperty, fade);
        _preview.BeginAnimation(UIElement.OpacityProperty, Views.Reveal.Keys(CrossFadeMs, Motion.Ease, (0, 0), (CrossFadeMs, 1)));
    }
}
