using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Overlay;
using PitBox.Services;
using PitBox.Theme;
using WinForms = System.Windows.Forms;

namespace PitBox.Views.Pages;

internal sealed class OverlayPage : UserControl, IPage, IScreenshotStates
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly bool _embedded;
    private readonly StackPanel _body;
    private readonly StackPanel _left = new(), _right = new();
    private StackPanel _position = new();
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, SnapsToDevicePixels = true, ToolTip = "Drag a value or a group name to change the order" };
    private readonly Canvas _scene = new() { ClipToBounds = true };
    private (double W, double H) _overlaySize;
    private (string Group, OverlayMetric? Metric)? _drag;
    private bool _reordered;
    private Vector _grab;
    private Size _heldSize;
    private readonly Border _ghost = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1.5), Opacity = 0.92 };
    private readonly Border _slot = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1.5) };
    private readonly Canvas _targets = new() { IsHitTestVisible = false };
    private readonly CheckBox _visible;
    private readonly TextBlock _previewNote = new();
    private readonly System.Windows.Shapes.Ellipse _liveDot = new() { Width = 6, Height = 6, Opacity = 0.3, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DockPanel _noteRow = new();
    private readonly System.Windows.Threading.DispatcherTimer _refresh = new();
    private readonly OverlayPreviewMotion _motion;
    private readonly Dictionary<OverlayMetric, (DockPanel Row, TextBlock Label)> _metricRows = new();
    private (DockPanel Row, TextBlock Label)? _litRow;
    private bool _wasVisible, _glideNext, _growNext;
    private readonly TextBlock _contrast = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Border _offBadge = new();

    private OverlaySettings O => _app.Config.Overlay;
    private OverlayLayout L => O.Current;

    public OverlayPage(AppServices app, MainWindow window, bool embedded = false)
    {
        _app = app;
        _window = window;
        _embedded = embedded;
        _body = new StackPanel { Margin = embedded ? new Thickness(0) : new Thickness(23, 0, 23, 23) };
        _visible = Ui.Switch(O.Visible, on => _app.SetOverlayVisible(on));
        _visible.Content = "Show overlay";
        _visible.ToolTip = "Show or hide the overlay over your game (also from its keybind and the tray)";
        Ui.Anchor(_visible, "overlay.visible");
        Content = embedded ? _body : new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        _scene.Children.Add(_preview);
        _scene.Children.Add(_targets);
        _scene.Children.Add(_slot);
        _scene.Children.Add(_ghost);
        _motion = new OverlayPreviewMotion(_scene, _preview, () => _app.Overlay.PreviewHits);
        HookPreviewDrag();
        _liveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentText");
        DockPanel.SetDock(_liveDot, Dock.Left);
        _noteRow.Children.Add(_liveDot);
        _noteRow.Children.Add(_previewNote);
        _refresh.Tick += (_, _) => { RenderPreview(); Blink(); };
        BuildFrame();
        BuildSettings();
    }

    public void OnShown()
    {
        _visible.IsChecked = O.Visible;
        RefreshStatus();
        if (!ReferenceEquals(_built, L) || MonitorSignature() != _builtMonitors) BuildSettings();
        RenderPreview();
        _wasVisible = O.Visible;
        ShowOffBadge();
        _motion.SetShown(O.Visible, false);
        if (O.Visible && Motion.Flourish)
        {
            _motion.Hide();
            Motion.AfterPageSettles(Dispatcher, () => { if (IsVisible) _motion.Boot(); else _motion.Stop(); });
        }
        _refresh.Interval = TimeSpan.FromMilliseconds(RefreshMs);
        _refresh.Start();
        _app.OverlayChanged += OnOverlayChanged;
        _app.Displays.Changed += OnDisplaysChanged;
        _app.Network.Acquire("Overlay page");
        _app.Frames.Acquire("Overlay page");
        if (!_embedded) Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _refresh.Stop();
        _motion.Stop();
        _app.OverlayChanged -= OnOverlayChanged;
        _app.Displays.Changed -= OnDisplaysChanged;
        _app.Network.Release("Overlay page");
        _app.Frames.Release("Overlay page");
    }

    private void OnOverlayChanged()
    {
        _visible.IsChecked = O.Visible;
        RefreshStatus();
        if (!ReferenceEquals(_built, L)) QueueBuild();
        RenderPreview();
        if (O.Visible == _wasVisible) return;
        _wasVisible = O.Visible;
        ShowOffBadge();
        _motion.SetShown(O.Visible, true);
        if (O.Visible) _motion.Boot();
    }

    private int RefreshMs => Math.Clamp(O.UpdateIntervalMs, 250, 1000);

    private void Blink()
    {
        if (!Motion.Flourish) return;
        _liveDot.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(Math.Min(320, RefreshMs * 0.8))) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
    }

    private void Morphing(Action change, double ms = 340)
    {
        var before = _motion.Capture();
        change();
        if (before is { } frame) _motion.Morph(frame, ms);
    }

    private void Wiping(Action change, bool glow = false)
    {
        var before = _motion.Capture();
        change();
        if (before is { } frame) _motion.Wipe(frame, glow, glow ? 620 : 450);
    }

    private void Fading(Action change)
    {
        var before = _motion.Capture();
        change();
        if (before is { } frame) _motion.Fade(frame);
    }

    private bool _buildQueued;

    private void QueueBuild()
    {
        if (_buildQueued) return;
        _buildQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _buildQueued = false;
            if (!ReferenceEquals(_built, L)) BuildSettings();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private string _builtMonitors = "";

    private string MonitorSignature() => string.Join("|", SafeMonitors().Select(m => m.Id + "=" + m.Label));

    private void OnDisplaysChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (IsVisible && MonitorSignature() != _builtMonitors) BuildSettings();
    }), System.Windows.Threading.DispatcherPriority.Background);

    private void Changed()
    {
        _app.OverlaySettingsChanged();
        UpdateContrast();
    }

    private void BuildFrame()
    {
        if (!_embedded)
            BuildTitle();
        BuildStatus();

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_right, 2);
        columns.Children.Add(_left);
        columns.Children.Add(_right);
        _body.Children.Add(columns);
    }

    private void BuildTitle()
    {
        var header = new Grid { Margin = Ui.HeaderMargin };
        var titles = new StackPanel();
        titles.Children.Add(Ui.PluginTitle(Plugins.PluginIds.Overlay, "Performance Overlay"));
        header.Children.Add(titles);
        _body.Children.Add(header);
    }

    private OverlayLayout? _built;

    private void BuildSettings()
    {
        _built = L;
        _left.Children.Clear();
        _right.Children.Clear();
        BuildLayouts();
        BuildPreview();
        BuildGeneral();
        BuildPosition();
        BuildMetrics();
        BuildLook();
        UpdateContrast();
    }

    private readonly ContentControl _statusChip = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };

    private void BuildStatus()
    {
        var title = new TextBlock { Text = "Show the overlay", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var tip = PitBox.Views.HudSettings.HelpTip.For("Nothing is injected into the game, so it's safe with anti-cheat. On some setups a window on top of the game can stop Windows using its lowest-latency presentation mode; hide the overlay with the keybind for races if you notice it.", "Performance Overlay");
        tip.VerticalAlignment = VerticalAlignment.Center;
        var text = new TextBlock { Text = "Over borderless and windowed games, not exclusive fullscreen.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TrimRow { Children = { title, tip } }, text } };
        Detached(_visible).VerticalAlignment = VerticalAlignment.Center;
        _visible.Margin = new Thickness(10, 0, 0, 0);
        _visible.Content = null;
        AutomationProperties.SetName(_visible, "Show overlay");
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _statusChip, _visible } };
        RefreshStatus();
        _body.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 13), Child = Ui.SplitRow(left, right, 24, stackLeft: true, together: 420) });
    }

    private void RefreshStatus()
    {
        _statusChip.Content = null;
    }

    private void BuildPreview()
    {
        var scene = new Grid { Height = 243, ClipToBounds = true };
        scene.SizeChanged += (_, e) => { if (e.WidthChanged) scene.Height = Math.Round(e.NewSize.Width * 9 / 16); PlacePreview(); };
        scene.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/OverlayPreview.jpg")),
            Stretch = Stretch.Uniform,
        });
        RenderOptions.SetBitmapScalingMode(scene.Children[0], BitmapScalingMode.HighQuality);
        scene.Children.Add(Detached(_scene));
        scene.Children.Add(Detached(BuildOffBadge()));
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var title = Ui.Text("Preview", "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        var hint = TextFit.TipWhenTrimmed(new TextBlock { Text = "Drag values or group names to reorder", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(hint, 1);
        head.Children.Add(hint);
        stack.Children.Add(head);
        stack.Children.Add(new Tile { CornerRadius = ThemeManager.Round(12), Margin = new Thickness(0, 10, 0, 0), Child = new Tile { CornerRadius = ThemeManager.Round(12), Child = scene, ClipToBounds = true } });
        _previewNote.Style = Ui.Res<Style>("Caption");
        _previewNote.TextWrapping = TextWrapping.Wrap;
        _noteRow.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(Detached(_noteRow));
        _left.Children.Add(Card(stack));
    }

    private Border BuildOffBadge()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Ui.Text("Overlay is off · preview only");
        text.FontWeight = FontWeights.SemiBold;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(text);
        var show = new Button { Style = Ui.Res<Style>("GhostButton"), Content = "Show overlay" };
        show.Click += (_, _) => _app.SetOverlayVisible(true);
        Grid.SetColumn(show, 1);
        grid.Children.Add(show);
        _offBadge.Child = grid;
        _offBadge.Padding = new Thickness(14, 8, 8, 8);
        Hairline.Frame(_offBadge);
        _offBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _offBadge.VerticalAlignment = VerticalAlignment.Bottom;
        _offBadge.Margin = new Thickness(0, 0, 0, 16);
        _offBadge.SetResourceReference(Border.BackgroundProperty, "Surface");
        _offBadge.SetResourceReference(Border.BorderBrushProperty, "Accent");
        ShowOffBadge();
        return _offBadge;
    }

    private void ShowOffBadge() => _offBadge.Visibility = O.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void HookPreviewDrag()
    {
        _preview.SizeChanged += (_, _) => PlacePreview();
        _preview.MouseLeftButtonDown += (_, e) =>
        {
            var at = InFrame(e);
            if (HitAt(at) is not { } hit || HeldRect((hit.Group, hit.Metric)) is not { } box) return;
            Light(null, null);
            _drag = (hit.Group, hit.Metric);
            _grab = at - box.TopLeft;
            _heldSize = box.Size;
            PickUp(box);
            _preview.CaptureMouse();
            MoveGhost(at);
            e.Handled = true;
        };
        _preview.MouseMove += (_, e) =>
        {
            var at = InFrame(e);
            if (_drag is { } held && _preview.IsMouseCaptured)
            {
                MoveGhost(at);
                DragTo(held, at - _grab + new Vector(_heldSize.Width / 2, _heldSize.Height / 2));
                ShowSlot();
            }
            else if (HitAt(at) is { } hover) Light(hover.Group, hover.Metric);
            else Light(null, null);
        };
        _preview.MouseLeave += (_, _) => { if (_drag is null) Light(null, null); };
        _preview.MouseLeftButtonUp += (_, _) =>
        {
            if (_drag is not null) _preview.ReleaseMouseCapture();
        };
        _preview.LostMouseCapture += (_, _) =>
        {
            _drag = null;
            _ghost.Visibility = _slot.Visibility = Visibility.Collapsed;
            _ghost.Child = null;
            _targets.Children.Clear();
            if (!_reordered) return;
            _reordered = false;
            Dispatcher.InvokeAsync(BuildSettings);
        };
    }

    private Point InFrame(MouseEventArgs e)
    {
        var p = e.GetPosition(_preview);
        double k = _preview.ActualWidth > 0 ? _overlaySize.W / _preview.ActualWidth : 1;
        return new Point(p.X * k, p.Y * k);
    }

    private Rect? HeldRect((string Group, OverlayMetric? Metric) held)
    {
        Rect? box = null;
        foreach (var hit in _app.Overlay.PreviewHits)
        {
            if (hit.Group != held.Group || (held.Metric is not null && hit.Metric != held.Metric)) continue;
            var r = new Rect(hit.Rect.Left, hit.Rect.Top, hit.Rect.Width, hit.Rect.Height);
            box = box is { } b ? Rect.Union(b, r) : r;
        }
        return box;
    }

    private void PickUp(Rect box)
    {
        if (_preview.Source is not BitmapSource source || OverlayPreviewMotion.Crop(source, box) is not { } copy) return;
        _ghost.Child = new Image { Source = copy, Stretch = Stretch.Fill };
        var lift = new ScaleTransform(1.05, 1.05);
        _ghost.RenderTransformOrigin = new Point(0.5, 0.5);
        _ghost.RenderTransform = lift;
        if (Motion.Flourish)
        {
            var up = new System.Windows.Media.Animation.DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(140)) { EasingFunction = Motion.Ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            lift.BeginAnimation(ScaleTransform.ScaleXProperty, up);
            lift.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        }
        _ghost.Background = Ui.Res<Brush>("Surface");
        _ghost.BorderBrush = Ui.Res<Brush>("AccentText");
        _slot.BorderBrush = Ui.Res<Brush>("AccentText");
        _slot.Background = Ui.Res<Brush>("AccentSoft").CloneCurrentValue();
        _slot.Background.Opacity = 0.35;
        _ghost.Visibility = Visibility.Visible;
        ShowSlot();
    }

    private double PreviewScale => _overlaySize.W > 0 && _preview.ActualWidth > 0 ? _preview.ActualWidth / _overlaySize.W : 1;

    private void MoveGhost(Point at)
    {
        double k = PreviewScale;
        var topLeft = at - _grab;
        _ghost.Width = _heldSize.Width * k + 3;
        _ghost.Height = _heldSize.Height * k + 3;
        Canvas.SetLeft(_ghost, Canvas.GetLeft(_preview) + topLeft.X * k - 1.5 + 3);
        Canvas.SetTop(_ghost, Canvas.GetTop(_preview) + topLeft.Y * k - 1.5 - 3);
    }

    private void ShowSlot()
    {
        if (_drag is not { } held || HeldRect(held) is not { } box) return;
        double k = PreviewScale;
        _slot.Width = box.Width * k + 3;
        _slot.Height = box.Height * k + 3;
        Canvas.SetLeft(_slot, Canvas.GetLeft(_preview) + box.X * k - 1.5);
        Canvas.SetTop(_slot, Canvas.GetTop(_preview) + box.Y * k - 1.5);
        _slot.Visibility = Visibility.Visible;
        ShowTargets(held, k);
    }

    private void ShowTargets((string Group, OverlayMetric? Metric) held, double k)
    {
        var boxes = _app.Overlay.PreviewHits
            .Where(h => held.Metric is null ? h.Group != held.Group : h.Group == held.Group && h.Metric is not null && h.Metric != held.Metric)
            .GroupBy(h => held.Metric is null ? (object)h.Group : h.Metric!.Value)
            .Select(g => g.Select(h => h.Rect).Aggregate(System.Drawing.RectangleF.Union))
            .ToList();
        var shown = _targets.Children;
        while (shown.Count > boxes.Count) shown.RemoveAt(shown.Count - 1);
        while (shown.Count < boxes.Count)
        {
            var outline = Hairline.Frame(new Border { SnapsToDevicePixels = true, Opacity = 0.6 });
            outline.SetResourceReference(Border.BorderBrushProperty, "AccentText");
            shown.Add(outline);
        }
        double left = Canvas.GetLeft(_preview), top = Canvas.GetTop(_preview);
        for (int i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            var outline = (Border)shown[i];
            outline.Width = box.Width * k + 3;
            outline.Height = box.Height * k + 3;
            Canvas.SetLeft(outline, left + box.X * k - 1.5);
            Canvas.SetTop(outline, top + box.Y * k - 1.5);
        }
    }

    private OverlayHit? HitAt(Point p)
    {
        foreach (var hit in _app.Overlay.PreviewHits)
            if (hit.Rect.Contains((float)p.X, (float)p.Y)) return hit;
        return null;
    }

    private void DragTo((string Group, OverlayMetric? Metric) held, Point at)
    {
        var hits = _app.Overlay.PreviewHits;
        if (held.Metric is { } metric)
        {
            var cells = hits.Where(h => h.Group == held.Group && h.Metric is not null).GroupBy(h => h.Metric!.Value)
                .Select(g => (Metric: g.Key, Start: g.Min(h => h.Rect.Left), End: g.Max(h => h.Rect.Right))).ToList();
            int from = cells.FindIndex(c => c.Metric == metric);
            if (from < 0) return;
            int to = OverlayRenderer.DropIndex(cells.Select(c => (c.Start, c.End)).ToList(), from, (float)at.X);
            if (to == from) return;
            var members = L.Metrics.Where(m => OverlayLayout.GroupOf(m.Metric) == held.Group).Select(m => m.Metric).ToList();
            L.MoveWithinGroup(metric, members.IndexOf(cells[to].Metric));
        }
        else
        {
            bool across = L.Style == OverlayStyle.Compact;
            var groups = hits.GroupBy(h => h.Group)
                .Select(g => (Group: g.Key, Start: g.Min(h => across ? h.Rect.Left : h.Rect.Top), End: g.Max(h => across ? h.Rect.Right : h.Rect.Bottom))).ToList();
            int from = groups.FindIndex(g => g.Group == held.Group);
            if (from < 0) return;
            int to = OverlayRenderer.DropIndex(groups.Select(g => (g.Start, g.End)).ToList(), from, (float)(across ? at.X : at.Y));
            if (to == from) return;
            L.MoveGroup(held.Group, L.GroupOrder().IndexOf(groups[to].Group));
        }
        _reordered = true;
        var before = _motion.Capture();
        Changed();
        if (before is { } frame) _motion.Morph(frame, 160);
    }

    private (double X, double Y) Room() =>
        (Math.Max(0, _scene.ActualWidth - _preview.ActualWidth), Math.Max(0, _scene.ActualHeight - _preview.ActualHeight));

    private void PlacePreview()
    {
        if (_scene.ActualWidth <= 0) return;
        _preview.MaxWidth = _scene.ActualWidth;
        _preview.MaxHeight = _scene.ActualHeight;
        var (monitorW, monitorH) = OverlayController.MonitorSize(L);
        var (fx, fy) = OverlayController.FractionOf(L, monitorW, monitorH, _overlaySize.W, _overlaySize.H);
        var (maxX, maxY) = Room();
        var from = new Point(Canvas.GetLeft(_preview), Canvas.GetTop(_preview));
        Canvas.SetLeft(_preview, Math.Round(fx * maxX));
        Canvas.SetTop(_preview, Math.Round(fy * maxY));
        if (!_glideNext) return;
        _glideNext = false;
        _motion.Glide(from);
    }

    private void Light(string? group, OverlayMetric? metric)
    {
        _motion.Spotlight(group, metric);
        if (_litRow is { } was)
        {
            was.Row.Background = Brushes.Transparent;
            Paint(was.Label, was.Label.Tag is true);
        }
        _litRow = null;
        if (metric is { } m && _metricRows.TryGetValue(m, out var row))
        {
            row.Row.SetResourceReference(Panel.BackgroundProperty, "AccentSoft");
            row.Label.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            _litRow = row;
        }
    }

    private static void Paint(TextBlock label, bool on) => label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Text" : "TextMuted");

    private void BuildLayouts()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("Layout", "H2"));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pick = Ui.Combo(O.Layouts.Select(l => new Choice(l.Name, l.Name)), O.ActiveLayout, c =>
        {
            Morphing(() =>
            {
                O.ActiveLayout = c.Id!;
                Changed();
            }, 380);
            QueueBuild();
        }, 162);
        pick.Margin = new Thickness(0, 0, 5, 5);
        row.Children.Add(pick);
        bool builtIn = L.IsDefault;
        var dup = Ui.IconButton("I.Copy", "Duplicate this layout", () => _ = NewLayoutAsync(copy: true));
        var rename = Ui.IconButton("I.Pencil", builtIn ? "The default layout keeps its name" : "Rename this layout", () => _ = RenameAsync());
        rename.IsEnabled = !builtIn;
        var delete = Ui.IconButton("I.Trash", builtIn ? "The default layout is always there" : "Delete this layout", () => _ = DeleteAsync());
        delete.IsEnabled = !builtIn && O.Layouts.Count > 1;
        foreach (var b in new[] { dup, rename, delete })
        {
            ToolTipService.SetShowOnDisabled(b, true);
            row.Children.Add(Wrapped(b));
        }
        row.Children.Add(Wrapped(Ui.LabeledButton("I.Plus", "New layout", () => _ = NewLayoutAsync(copy: false), "PrimaryButton")));
        var reset = Ui.LabeledButton("I.Restore", "Reset to default", () => _ = ResetLayoutAsync(), "GhostButton");
        reset.ToolTip = "Puts every setting of this layout back as it comes with Pit-Box";
        row.Children.Add(Wrapped(reset));
        stack.Children.Add(row);
        var card = Card(stack);
        Ui.Anchor(card, "overlay.layouts");
        _left.Children.Add(card);
    }

    private void BuildGeneral()
    {
        var hotkey = HotkeyEditor("overlay", () => O.Hotkey, hk => O.Hotkey = hk);
        var std = new OverlayLayout();
        Ui.SettingDefault D(Func<bool> same, Action reset, string shown) => new(same, () => { reset(); Changed(); BuildSettings(); }, shown);
        var keys = new SettingsFlow();
        keys.Children.Add(Ui.Text("Keybinds", "H2"));
        keys.Children.Add(Anchored(Ui.SettingRow("Show / hide", null, hotkey), "overlay.hotkey"));
        keys.Children.Add(Anchored(Ui.SettingRow("Reset FPS statistics", null,
            HotkeyEditor("overlay.fps", () => O.ResetFpsHotkey, hk => O.ResetFpsHotkey = hk)), "overlay.fpshotkey"));
        _left.Children.Add(Card(keys));
        var general = new SettingsFlow();
        general.Children.Add(Ui.Text("Data", "H2"));
        general.Children.Add(Anchored(Ui.SettingRow("Update rate", "How often the overlay redraws, in every layout. Sensors are still read at Settings → Performance's rate.",
            Ui.Segmented(new[] { "Fast", "Normal", "Relaxed" }, O.UpdateIntervalMs switch { <= 250 => 0, >= 1000 => 2, _ => 1 },
                i => { O.UpdateIntervalMs = i switch { 0 => 250, 2 => 1000, _ => 500 }; _refresh.Interval = TimeSpan.FromMilliseconds(RefreshMs); Changed(); }, "Rate",
                new[] { "Fast: 4 times a second. Smoothest numbers, slightly more CPU", "Normal: twice a second", "Relaxed: once a second. The lightest; numbers jump less" }),
            D(() => O.UpdateIntervalMs == 500, () => O.UpdateIntervalMs = 500, "Normal")), "overlay.rate"));
        general.Children.Add(Ui.SettingRow("FPS statistics", "Average and 1% low cover the whole session for each game; average temperatures, the time the overlay has been showing.",
            ResetButton()));
        _left.Children.Add(Card(general));
    }

    private void BuildLook()
    {
        var std = new OverlayLayout();
        Ui.SettingDefault D(Func<bool> same, Action reset, string shown) => new(same, () => { reset(); Changed(); BuildSettings(); }, shown);
        var general = new SettingsFlow();
        general.Children.Add(Ui.Text("Look", "H2"));
        general.Children.Add(Anchored(Ui.SettingRow("Style", null,
            Ui.Segmented(new[] { "Expanded", "Compact" }, (int)L.Style, i => Morphing(() => { L.Style = (OverlayStyle)i; Changed(); }), "Style",
                new[] { "One row per group (CPU, GPU, FPS…), easy to read at a glance", "Everything on one line: takes the least room over your game" }),
            D(() => L.Style == std.Style, () => L.Style = std.Style, std.Style.ToString())), "overlay.style"));
        general.Children.Add(Anchored(Ui.SettingRow("Text size", null, MarksStartSize(Ui.SliderWithValue(60, 200, L.FontScale, "{0:0}%", v => { _growNext = true; L.FontScale = (int)v; Changed(); }, 105, 5)),
            D(() => L.FontScale == std.FontScale, () => L.FontScale = std.FontScale, std.FontScale + "%")), "overlay.font"));
        general.Children.Add(Anchored(Ui.SettingRow("Background", null, Ui.SliderWithValue(0, 100, L.BackgroundOpacity, "{0:0}%", v => Fading(() => { L.BackgroundOpacity = (int)v; Changed(); }), 105, 5),
            D(() => L.BackgroundOpacity == std.BackgroundOpacity, () => L.BackgroundOpacity = std.BackgroundOpacity, std.BackgroundOpacity + "%")), "overlay.opacity"));
        general.Children.Add(Anchored(Ui.SettingRow("Frame-time graph", "Spikes are stutters.",
            Ui.Switch(L.ShowGraph, v => { L.ShowGraph = v; Changed(); }),
            D(() => L.ShowGraph == std.ShowGraph, () => L.ShowGraph = std.ShowGraph, std.ShowGraph ? "On" : "Off")), "overlay.graph"));
        var rename = Ui.LabeledButton("I.Pencil", "Rename items…", () => _ = RenameItemsAsync());
        rename.ToolTip = "Your own names for what the overlay shows (e.g. GPU → 7900 XTX)";
        general.Children.Add(Anchored(Ui.SettingRow("Names", null, rename), "overlay.names"));
        BuildColors(general);
        _right.Children.Add(Card(general));
    }

    private void BuildPosition()
    {
        var position = new SettingsFlow();
        position.Children.Add(Ui.Text("Position", "H2"));
        _builtMonitors = MonitorSignature();
        var map = new OverlayPlacementMap(() => L, () => { _glideNext = true; Changed(); }) { Margin = new Thickness(0, 10, 0, 0) };
        position.Children.Add(Anchored(map, "overlay.position"));
        _position = position;
        _right.Children.Add(Card(position));
    }

    private void BuildColors(StackPanel colors)
    {
        var custom = new WrapPanel { ItemWidth = 118, Visibility = L.Colors == OverlayColorPreset.Custom ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 5, 0, 0) };
        colors.Children.Add(Anchored(Ui.SettingRow("Colors", null,
            ThemeMenu.OverlayButton(
                () => new ThemeMenu.Pick(L.Colors.IsApp, L.Colors == OverlayColorPreset.Custom, L.Colors.Theme ?? ThemeManager.Current.Id, L.ColorSet),
                pick =>
                {
                    if (pick.Custom)
                    {
                        if (L.Colors != OverlayColorPreset.Custom)
                        {
                            var layout = L;
                            var before = L.Custom;
                            L.Custom = OverlayPresets.For(L.Colors, L.Custom, L.ColorSet);
                            if (!SameColors(before, L.Custom))
                                _app.Notify("Custom colors start from this look", "Your earlier custom colors were replaced.", NoticeKind.Info, action: new NoticeAction("Undo", () =>
                                {
                                    layout.Custom = before;
                                    Changed();
                                    if (ReferenceEquals(layout, L)) BuildSettings();
                                }));
                        }
                        L.Colors = OverlayColorPreset.Custom;
                    }
                    else if (pick.FollowApp) { L.Colors = OverlayColorPreset.App; L.ColorSet = null; }
                    else { L.Colors = OverlayLayout.For(pick.Theme); L.ColorSet = pick.Set; }
                    Wiping(Changed);
                    BuildSettings();
                }, followApp: true, customLabel: "Custom", minWidth: 162)), "overlay.colors"));

        var c = L.Custom;
        custom.Children.Add(ColorRow("Main text", () => c.Text, v => c.Text = v));
        custom.Children.Add(ColorRow("Secondary text", () => c.Secondary, v => c.Secondary = v));
        custom.Children.Add(ColorRow("Accent (labels)", () => c.Accent, v => c.Accent = v));
        custom.Children.Add(ColorRow("Background", () => c.Background, v => c.Background = v));
        custom.Children.Add(ColorRow("Graph", () => c.Graph, v => c.Graph = v));
        custom.Children.Add(ColorRow("FPS", () => c.Fps, v => c.Fps = v));
        custom.Children.Add(ColorRow("Warning", () => c.Warning, v => c.Warning = v));
        custom.Children.Add(ColorRow("Critical", () => c.Critical, v => c.Critical = v));
        colors.Children.Add(custom);
        _contrast.Style = Ui.Res<Style>("Caption");
        _contrast.Foreground = Ui.Res<Brush>("WarnDeep");
        _contrast.Margin = new Thickness(0, 6, 0, 0);
        colors.Children.Add(Detached(_contrast));
    }

    private StackPanel ColorRow(string label, Func<string> get, Action<string> set)
    {
        var swatch = new Button { Width = 24, Height = 21, MinHeight = 0, Padding = new Thickness(0), ToolTip = "Pick a color", Margin = new Thickness(0, 0, 5, 0) };
        var hex = new TextBox { Width = 78, Text = get() };
        void Paint()
        {
            var color = OverlayRenderer.Parse(get(), System.Drawing.Color.Gray);
            swatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }
        hex.TextChanged += (_, _) =>
        {
            string text = hex.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
            {
                set(text.ToUpperInvariant());
                Paint();
                Changed();
            }
        };
        swatch.Click += (_, _) =>
        {
            if (!PitBox.Services.External.Allowed("color dialog")) return;
            var current = OverlayRenderer.Parse(get(), System.Drawing.Color.Gray);
            using var dialog = new WinForms.ColorDialog { Color = current, FullOpen = true, AnyColor = true };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                hex.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        };
        Paint();
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(swatch);
        controls.Children.Add(hex);
        var cell = new StackPanel { Margin = new Thickness(0, 3, 6, 5) };
        cell.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = label, Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 0, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis }));
        cell.Children.Add(controls);
        return cell;
    }

    internal static bool SameColors(OverlayColors a, OverlayColors b) =>
        new[] { (a.Text, b.Text), (a.Secondary, b.Secondary), (a.Accent, b.Accent), (a.Background, b.Background), (a.Graph, b.Graph), (a.Fps, b.Fps), (a.Warning, b.Warning), (a.Critical, b.Critical) }
            .All(p => string.Equals(p.Item1?.Trim(), p.Item2?.Trim(), StringComparison.OrdinalIgnoreCase));

    private void UpdateContrast()
    {
        var colors = OverlayPresets.For(L.Colors, L.Custom, L.ColorSet);
        double ratio = Palettes.TryHex(colors.Text, out var text) && Palettes.TryHex(colors.Background, out var bg) ? ThemeManager.Contrast(text, bg) : double.NaN;
        bool weak = ratio < 3 && L.BackgroundOpacity >= 40;
        _contrast.Text = weak ? $"Text may be hard to read on this background (contrast {ratio:0.0}:1, 3:1 or more is easier on the eyes)." : "";
        _contrast.Visibility = weak ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildMetrics()
    {
        _metricRows.Clear();
        _litRow = null;
        var metrics = new StackPanel { Margin = new Thickness(0, 11, 0, 0) };
        metrics.Children.Add(Ui.Text("What to show", "H2"));
        var cards = new MasonryPanel { MinColumnWidth = MetricCardWidth, MaxColumns = 4, Gap = 8, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var group in L.GroupOrder())
            cards.Children.Add(MetricCard(group, L.Metrics.Where(m => OverlayLayout.GroupOf(m.Metric) == group).ToList()));
        metrics.Children.Add(cards);
        Ui.Anchor(metrics, "overlay.metrics");
        _position.Children.Add(Ui.Divider());
        _position.Children.Add(metrics);
    }

    private const double MetricCardWidth = 150;

    private Tile MetricCard(string group, List<OverlayMetricEntry> entries)
    {
        var name = new TextBlock { Text = group, Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center };
        var count = new Odometer(10.5, "TextFaint", italic: false, heavy: false) { RollMs = 360, Margin = new Thickness(7, 0, 0, 0) };
        var all = new Button { Style = Ui.Res<Style>("GhostButton"), Height = 22, MinWidth = 0, Padding = new Thickness(7, 0, 7, 0), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        if (entries.Count < 2) all.Visibility = Visibility.Hidden;
        var head = new DockPanel { Margin = new Thickness(10, 6, 6, 6), LastChildFill = false };
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(name);
        head.Children.Add(count);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(Hairline.Rule(new Border { Background = Ui.Res<Brush>("Line") }));
        var setters = new List<Action<bool>>();

        void Sync()
        {
            int on = entries.Count(e => e.Enabled);
            count.Set($"{on}/{entries.Count}");
            all.Content = on == entries.Count ? "None" : "All";
            name.SetResourceReference(TextBlock.ForegroundProperty, on == 0 ? "TextFaint" : "EyebrowText");
        }

        foreach (var entry in entries)
        {
            var label = TextFit.TipWhenTrimmed(new TextBlock { Text = ShortLabel(entry.Metric), Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var toggle = Ui.Switch(entry.Enabled, _ => { });
            AutomationProperties.SetName(toggle, MetricLabel(entry.Metric));
            var row = new DockPanel { Height = 30, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = MetricLabel(entry.Metric) };
            DockPanel.SetDock(toggle, Dock.Right);
            toggle.Margin = new Thickness(8, 0, 10, 0);
            label.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(toggle);
            row.Children.Add(label);

            void Show(bool on)
            {
                toggle.IsChecked = on;
                label.Tag = on;
                Paint(label, on);
            }
            void Apply(bool on)
            {
                Morphing(() =>
                {
                    entry.Enabled = on;
                    Show(on);
                    Sync();
                    Changed();
                });
                if (row.IsMouseOver) Light(group, entry.Metric);
            }
            toggle.Click += (_, _) => Apply(toggle.IsChecked == true);
            row.MouseLeftButtonUp += (_, e) => { if (!toggle.IsMouseOver) { Apply(!entry.Enabled); e.Handled = true; } };
            row.MouseEnter += (_, _) => Light(group, entry.Metric);
            row.MouseLeave += (_, _) => Light(null, null);
            _metricRows[entry.Metric] = (row, label);
            setters.Add(on => { entry.Enabled = on; Show(on); });
            Show(entry.Enabled);
            body.Children.Add(row);
        }

        all.Click += (_, _) =>
        {
            bool on = entries.Any(e => !e.Enabled);
            Morphing(() =>
            {
                setters.ForEach(set => set(on));
                Sync();
                Changed();
            });
        };
        Sync();
        return new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(0, 0, 0, 4), Child = body };
    }

    private Button ResetButton()
    {
        var button = Ui.LabeledButton("I.Refresh", "Reset", () => { });
        button.Click += (_, _) =>
        {
            Spin(button);
            Wiping(() =>
            {
                _app.ResetFpsStats();
                RenderPreview();
            }, glow: true);
        };
        return button;
    }

    private static void Spin(Button button)
    {
        if (!Motion.Flourish || button.Content is not StackPanel { Children: [Icon icon, ..] }) return;
        var turn = new RotateTransform();
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = turn;
        turn.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, -360, TimeSpan.FromMilliseconds(500)) { EasingFunction = Motion.Ease, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
    }

    private StackPanel MarksStartSize(StackPanel sliderRow)
    {
        if (sliderRow.Children.OfType<Slider>().FirstOrDefault() is { } slider)
        {
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _motion.MarkStartSize(true)));
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _motion.MarkStartSize(false)));
        }
        return sliderRow;
    }

    private static string ShortLabel(OverlayMetric m) => m switch
    {
        OverlayMetric.Fps => "FPS",
        OverlayMetric.AverageFps => "Average",
        OverlayMetric.OnePercentLow => "1% low",
        OverlayMetric.FrameTime => "Frame time",
        OverlayMetric.GpuTemp or OverlayMetric.CpuTemp => "Temp",
        OverlayMetric.AverageGpuTemp or OverlayMetric.AverageCpuTemp => "Average temp",
        OverlayMetric.GpuLoad or OverlayMetric.CpuLoad => "Usage",
        OverlayMetric.GpuHotSpot => "Hot spot",
        OverlayMetric.GpuClock or OverlayMetric.CpuClock => "Clock",
        OverlayMetric.GpuPower or OverlayMetric.CpuPower => "Power",
        OverlayMetric.GpuFan => "Fan speed",
        OverlayMetric.Vram => "VRAM used",
        OverlayMetric.Ram => "RAM used",
        OverlayMetric.Ping => "Ping",
        OverlayMetric.Network => "Down / up",
        _ => "Fan speeds",
    };

    public static string MetricLabel(OverlayMetric m) => m switch
    {
        OverlayMetric.Fps => "FPS",
        OverlayMetric.AverageFps => "Average FPS",
        OverlayMetric.OnePercentLow => "1% low",
        OverlayMetric.FrameTime => "Frame time",
        OverlayMetric.GpuTemp => "GPU temperature",
        OverlayMetric.AverageGpuTemp => "GPU average temperature",
        OverlayMetric.GpuLoad => "GPU usage",
        OverlayMetric.GpuHotSpot => "GPU hot spot",
        OverlayMetric.GpuClock => "GPU clock",
        OverlayMetric.GpuPower => "GPU power",
        OverlayMetric.GpuFan => "GPU fan speed",
        OverlayMetric.Vram => "GPU VRAM",
        OverlayMetric.CpuTemp => "CPU temperature",
        OverlayMetric.AverageCpuTemp => "CPU average temperature",
        OverlayMetric.CpuLoad => "CPU usage",
        OverlayMetric.CpuClock => "CPU clock",
        OverlayMetric.CpuPower => "CPU power",
        OverlayMetric.Ram => "RAM usage",
        OverlayMetric.Ping => "Network ping",
        OverlayMetric.Network => "Network download / upload",
        OverlayMetric.Fans => "Fan speeds",
        _ => m.ToString(),
    };

    private HotkeyBox HotkeyEditor(string id, Func<string?> get, Action<string?> set)
    {
        var box = new HotkeyBox();
        box.SetHotkey(get());
        box.HotkeyChanged += hk =>
        {
            if (_app.Hotkeys.Clash(hk, id) is { } clash)
            {
                _app.Notify("Keybind not changed", clash, NoticeKind.Warning, important: true);
                box.SetHotkey(get());
                return;
            }
            set(hk);
            _app.Save();
            _app.Hotkeys.RegisterAll();
        };
        return box;
    }

    private async Task NewLayoutAsync(bool copy)
    {
        string? name = copy
            ? await _window.Prompt("Duplicate layout", "A copy of the current layout, to change from there.", L.Name + " copy", n => ValidateName(n))
            : await _window.Prompt("New overlay layout", "Starts from the default layout.", $"Layout {O.Layouts.Count + 1}", n => ValidateName(n));
        if (name is null) return;
        var layout = copy ? ConfigStore.Clone(L) : new OverlayLayout();
        layout.Name = name;
        layout.Complete();
        O.Layouts.Add(layout);
        O.ActiveLayout = name;
        Changed();
        BuildSettings();
    }

    private async Task RenameItemsAsync()
    {
        var layout = L;
        var groups = layout.Metrics.Select(m => OverlayLayout.GroupOf(m.Metric)).Distinct().ToList();
        var stack = new StackPanel();
        var groupBoxes = new Dictionary<string, TextBox>();
        var tagBoxes = new Dictionary<OverlayMetric, TextBox>();
        stack.Children.Add(Ui.EditorSection("Groups"));
        foreach (var g in groups)
        {
            var box = new TextBox { Text = layout.GroupNames.TryGetValue(g, out var own) ? own : "", Tag = g, MaxLength = 24, Width = 178, HorizontalAlignment = HorizontalAlignment.Left };
            groupBoxes[g] = box;
            stack.Children.Add(Ui.FieldRow(g, box));
        }
        stack.Children.Add(Ui.EditorSection("Value tags"));
        stack.Children.Add(new TextBlock { Text = "The small word in front of a value. Leave a box empty for no tag.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (metric, standard) in TagDefaults)
        {
            if (!layout.Metrics.Any(m => m.Metric == metric)) continue;
            var box = new TextBox { Text = layout.MetricTag(metric, standard), Tag = standard.Length > 0 ? standard : "no tag", MaxLength = 16, Width = 178, HorizontalAlignment = HorizontalAlignment.Left };
            tagBoxes[metric] = box;
            stack.Children.Add(Ui.FieldRow(MetricLabel(metric), box));
        }
        var reset = Ui.LabeledButton("I.Refresh", "Standard names", () =>
        {
            foreach (var b in groupBoxes.Values) b.Text = "";
            foreach (var (metric, standard) in TagDefaults) if (tagBoxes.TryGetValue(metric, out var b)) b.Text = standard;
        }, "GhostButton");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(reset);
        var card = Ui.Dialog(_window, "Rename items", $"Names shown in the overlay for “{layout.Name}”.", new ScrollViewer { Content = stack, MaxHeight = 421, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Ui.DialogButtons(_window, "Save", () =>
            {
                layout.GroupNames = groupBoxes.Where(p => !string.IsNullOrWhiteSpace(p.Value.Text) && p.Value.Text.Trim() != p.Key)
                    .ToDictionary(p => p.Key, p => p.Value.Text.Trim(), StringComparer.OrdinalIgnoreCase);
                layout.MetricTags = tagBoxes.Where(p => p.Value.Text.Trim() != TagDefaults.First(d => d.Metric == p.Key).Tag)
                    .ToDictionary(p => p.Key, p => p.Value.Text.Trim());
                return true;
            }), null, 421);
        if (await _window.ShowModal(card)) Changed();
    }

    private static readonly (OverlayMetric Metric, string Tag)[] TagDefaults =
    {
        (OverlayMetric.AverageFps, "avg"), (OverlayMetric.OnePercentLow, "1%"), (OverlayMetric.GpuHotSpot, "hotspot"), (OverlayMetric.GpuFan, "fan"),
        (OverlayMetric.AverageGpuTemp, "avg"), (OverlayMetric.AverageCpuTemp, "avg"),
        (OverlayMetric.Fps, ""), (OverlayMetric.GpuTemp, ""), (OverlayMetric.GpuLoad, ""), (OverlayMetric.CpuTemp, ""), (OverlayMetric.CpuLoad, ""),
    };

    private async Task RenameAsync()
    {
        var layout = L;
        if (layout.IsDefault) return;
        string? name = await _window.Prompt("Rename layout", "A name for this layout.", layout.Name, n => n == layout.Name ? null : ValidateName(n, layout));
        if (name is null || name == layout.Name) return;
        string old = layout.Name;
        layout.Name = name;
        O.ActiveLayout = name;
        Changed();
        BuildSettings();
    }

    private async Task ResetLayoutAsync()
    {
        var layout = L;
        if (!await _window.Confirm($"Reset “{layout.Name}” to default?", "What it shows, its order, names, look and place on screen go back to the defaults.", "Reset"))
            return;
        var fresh = new OverlayLayout { Name = layout.Name };
        fresh.Complete();
        O.Layouts[O.Layouts.IndexOf(layout)] = fresh;
        Changed();
        BuildSettings();
    }

    private async Task DeleteAsync()
    {
        var layout = L;
        if (layout.IsDefault || O.Layouts.Count <= 1 || !await _window.Confirm($"Delete “{layout.Name}”?", "This can't be undone.", "Delete", danger: true))
            return;
        O.Layouts.Remove(layout);
        O.ActiveLayout = (O.Layouts.FirstOrDefault(l => l.IsDefault) ?? O.Layouts[0]).Name;
        Changed();
        BuildSettings();
    }

    private string? ValidateName(string name, OverlayLayout? self = null) =>
        name.Length == 0 ? "Give the layout a name."
        : O.Layouts.Any(l => l != self && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ? $"There's already a layout named “{name}”." : null;

    private static Border Card(UIElement child) => new Tile { Style = Ui.Res<Style>("Card"), Child = child, Margin = new Thickness(0, 0, 0, 13) };

    private static FrameworkElement Wrapped(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 0, 5, 5);
        return e;
    }

    private static T Detached<T>(T element) where T : FrameworkElement
    {
        if (element.Parent is Panel panel)
            panel.Children.Remove(element);
        return element;
    }

    private static FrameworkElement Anchored(FrameworkElement e, string id)
    {
        Ui.Anchor(e, id);
        return e;
    }

    private static IEnumerable<(string Id, string Label)> SafeMonitors()
    {
        try { return OverlayController.AvailableMonitors().ToList(); }
        catch { return new[] { ("primary", "Primary monitor") }; }
    }

    private void RenderPreview()
    {
        var game = _app.Frames.GetMostActive();
        var stats = game?.Stats ?? new FrameStats(144, 141, 118, 6.9);
        var times = game is { } g ? _app.Frames.GetFrameTimes(g.ProcessId, 120) : FrameRateService.SampleFrameTimes(120);
        _previewNote.Text = game is not null
            ? $"Live values · FPS from {game.Value.Name}"
            : "Live temperatures and load. FPS and the frame-time graph show sample values until a game is running.";
        try
        {
            using var bitmap = _app.Overlay.RenderPreview(L, stats, times);
            _preview.Source = BitmapConvert.ToSource(bitmap);
            double wasWidth = _overlaySize.W;
            _overlaySize = (bitmap.Width, bitmap.Height);
            if (_growNext && wasWidth > 0)
                _motion.Grow(wasWidth / bitmap.Width, L.Corner is OverlayCorner.TopRight or OverlayCorner.BottomRight, L.Corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight);
            _growNext = false;
        }
        catch
        {
            return;
        }
        PlacePreview();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("compact-custom", () =>
        {
            L.Style = OverlayStyle.Compact;
            L.ShowGraph = true;
            L.Colors = OverlayColorPreset.Custom;
            L.Custom = new OverlayColors { Text = "#EEEEEE", Background = "#D8D8D8" };
            Changed();
            BuildSettings();
            return Task.CompletedTask;
        });
    }
}
