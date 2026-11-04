using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Overlay;
using PitBox.Services;
using PitBox.Theme;

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
    private readonly CheckBox _visible;
    private readonly TextBlock _previewNote = new();
    private readonly System.Windows.Shapes.Ellipse _liveDot = new() { Width = 6, Height = 6, Opacity = 0.3, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DockPanel _noteRow = new();
    private readonly System.Windows.Threading.DispatcherTimer _refresh = new();
    private readonly OverlayPreviewMotion _motion;
    private readonly Dictionary<OverlayMetric, (Grid Row, TextBlock Label)> _metricRows = new();
    private (Grid Row, TextBlock Label)? _litRow;
    private bool _glideNext, _growNext;
    private readonly TextBlock _contrast = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Image _closeUp = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
    private readonly Border _closeUpBox = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };

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
        _refresh.Interval = TimeSpan.FromMilliseconds(RefreshMs);
        _refresh.Start();
        _app.OverlayChanged += OnOverlayChanged;
        _app.Displays.Changed += OnDisplaysChanged;
        _app.Network.Acquire("Overlay page");
        _app.Frames.Acquire("Overlay page");
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
    }

    private int RefreshMs => Math.Clamp(O.UpdateIntervalMs, 250, 1000);

    private void Blink()
    {
        if (!Motion.Flourish || !O.Visible) return;
        var length = TimeSpan.FromMilliseconds(Math.Min(320, RefreshMs * 0.8));
        if (_blink is null || _blink.Duration.TimeSpan != length)
        {
            _blink = new System.Windows.Media.Animation.DoubleAnimation(1, 0.3, length) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            _blink.Freeze();
        }
        _liveDot.BeginAnimation(OpacityProperty, _blink);
    }

    private System.Windows.Media.Animation.DoubleAnimation? _blink;

    private void Morphing(Action change, double ms = 340)
    {
        var before = _motion.Capture();
        change();
        if (before is { } frame) _motion.Fade(frame, ms);
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
        else
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
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(Ui.PageTitle("Performance Overlay"));
        header.Children.Add(titles);
        Detached(_visible);
        _visible.Content = null;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _layoutBar.Margin = new Thickness(0, 0, 26, 0);
        tools.Children.Add(_layoutBar);
        tools.Children.Add(Ui.PageSwitch("Show the overlay", _visible, ShowHelp));
        Grid.SetColumn(tools, 1);
        header.Children.Add(tools);
        _body.Children.Add(header);
    }

    private readonly StackPanel _layoutBar = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private const string ShowHelp = "Shows over borderless and windowed games, not exclusive fullscreen. Nothing is injected into the game, so it's safe with anti-cheat. On some setups a window on top of the game can stop Windows using its lowest-latency presentation mode; hide the overlay with the keybind for races if you notice it.";

    private OverlayLayout? _built;

    private void BuildSettings()
    {
        _built = L;
        _left.Children.Clear();
        _right.Children.Clear();
        BuildLayouts();
        BuildPreview();
        BuildLook();
        BuildPosition();
        BuildMetrics();
        BuildGeneral();
        UpdateContrast();
    }

    private readonly ContentControl _statusChip = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };

    private void BuildStatus()
    {
        var title = ToggleCards.RowTitle("Show the overlay");
        Ui.AddInfo(title, "Nothing is injected into the game, so it's safe with anti-cheat. On some setups a window on top of the game can stop Windows using its lowest-latency presentation mode; hide the overlay with the keybind for races if you notice it.");
        Detached(_visible);
        _visible.Margin = new Thickness(0);
        AutomationProperties.SetName(_visible, "Show overlay");
        ToggleCards.Row(_visible, title, "Over borderless and windowed games, not exclusive fullscreen.");
        RefreshStatus();
        _body.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = _visible });
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
        if (_closeUpBox.Child is null)
        {
            var closeUpStack = new StackPanel();
            closeUpStack.Children.Add(new TextBlock { Text = "Actual size", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 4) });
            closeUpStack.Children.Add(new ScrollViewer { Content = _closeUp, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
            _closeUpBox.Child = closeUpStack;
        }
        stack.Children.Add(Detached(_closeUpBox));
        _previewNote.Style = Ui.Res<Style>("Caption");
        _previewNote.TextWrapping = TextWrapping.Wrap;
        _noteRow.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(Detached(_noteRow));
        _left.Children.Add(Card(stack));
    }

    private void HookPreviewDrag()
    {
        _preview.SizeChanged += (_, _) => PlacePreview();
        _preview.MouseLeftButtonDown += (_, e) =>
        {
            var at = InFrame(e);
            if (HitAt(at) is not { } hit || HeldRect((hit.Group, hit.Metric)) is not { } box) return;
            _drag = (hit.Group, hit.Metric);
            _grab = at - box.TopLeft;
            _heldSize = box.Size;
            _motion.Holding = true;
            Light(hit.Group, hit.Metric);
            _motion.Spotlight(hit.Group, hit.Metric);
            _preview.CaptureMouse();
            e.Handled = true;
        };
        _preview.MouseMove += (_, e) =>
        {
            var at = InFrame(e);
            if (_drag is { } held && _preview.IsMouseCaptured)
                DragTo(held, at - _grab + new Vector(_heldSize.Width / 2, _heldSize.Height / 2));
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
            _motion.Holding = false;
            if (_lit.Group is not null) _motion.Spotlight(_lit.Group, _lit.Metric);
            if (!_reordered) return;
            _reordered = false;
            Dispatcher.InvokeAsync(BuildSettings);
        };
    }

    private Point InFrame(MouseEventArgs e)
    {
        var p = e.GetPosition(_preview);
        double k = _preview.ActualWidth > 0 && _preview.Source is System.Windows.Media.Imaging.BitmapSource source ? source.PixelWidth / _preview.ActualWidth : 1;
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
        Changed();
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

    private (string? Group, OverlayMetric? Metric) _lit;

    private void Light(string? group, OverlayMetric? metric)
    {
        if (_lit == (group, metric)) return;
        _lit = (group, metric);
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
        var row = _embedded ? new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) } : _layoutBar;
        row.Children.Clear();
        var label = new TextBlock { Text = "Layout", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        row.Children.Add(label);
        var pick = Ui.Combo(O.Layouts.Select(l => new Choice(l.Name, l.Name)), O.ActiveLayout, c =>
        {
            Morphing(() =>
            {
                O.ActiveLayout = c.Id!;
                Changed();
            }, 380);
            QueueBuild();
        }, 162);
        AutomationProperties.SetName(pick, "Layout");
        row.Children.Add(pick);
        var edit = Ui.IconButton("I.Pencil", "Rename, duplicate, reset or delete this layout", () => _ = EditLayoutAsync(), "GhostButton");
        edit.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
        row.Children.Add(edit);
        var add = Ui.IconButton("I.Plus", "New layout", () => _ = NewLayoutAsync(copy: false), "GhostButton");
        add.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
        row.Children.Add(add);
        Ui.Anchor(row, "overlay.layouts");
        if (!_embedded) return;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("Layout", "H2"));
        stack.Children.Add(row);
        _left.Children.Add(Card(stack));
    }

    private async Task EditLayoutAsync()
    {
        var layout = L;
        bool builtIn = layout.IsDefault;
        var name = new TextBox { Text = layout.Name, MaxLength = Ui.NameLength, IsEnabled = !builtIn };
        var error = new TextBlock { Visibility = Visibility.Collapsed };
        var content = new StackPanel();
        content.Children.Add(Ui.FieldRow("Name", name, hint: builtIn ? "The default layout keeps its name." : null));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var dup = Ui.LabeledButton("I.Copy", "Duplicate", () => { _window.CloseModal(false); _ = NewLayoutAsync(copy: true); });
        var reset = Ui.LabeledButton("I.Undo", "Reset to default", () => { _window.CloseModal(false); _ = ResetLayoutAsync(); });
        reset.ToolTip = "What it shows, its order, names, look and place on screen go back to the defaults";
        reset.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
        actions.Children.Add(dup);
        actions.Children.Add(reset);
        content.Children.Add(actions);
        Button? delete = null;
        if (!builtIn && O.Layouts.Count > 1)
        {
            delete = new Button { Content = "Delete", Style = Ui.Res<Style>("DangerButton") };
            delete.Click += (_, _) => { _window.CloseModal(false); _ = DeleteAsync(); };
        }
        var card = Ui.Dialog(_window, "Edit layout", "", content,
            Ui.DialogButtons(_window, "Save", () =>
            {
                string wanted = name.Text.Trim();
                if (builtIn || wanted == layout.Name) return true;
                if (ValidateName(wanted, layout) is { } problem)
                {
                    error.Text = problem;
                    error.Visibility = Visibility.Visible;
                    return false;
                }
                layout.Name = wanted;
                O.ActiveLayout = wanted;
                return true;
            }), error, 421, delete);
        if (!await _window.ShowModal(card)) return;
        Changed();
        BuildSettings();
    }

    private void BuildGeneral()
    {
        var hotkey = HotkeyEditor("overlay", () => O.Hotkey, hk => O.Hotkey = hk);
        Ui.SettingDefault D(Func<bool> same, Action reset, string shown) => new(same, () => { reset(); Changed(); BuildSettings(); }, shown);
        var general = new SettingsFlow();
        general.Children.Add(Ui.Text("Data and keybinds", "H2"));
        general.Children.Add(Anchored(Ui.SettingRow("Update rate", "All layouts. Sensors follow Settings → Performance.",
            Ui.Segmented(new[] { "4 " + Units.Hz, "2 " + Units.Hz, "1 " + Units.Hz }, O.UpdateIntervalMs switch { <= 250 => 0, >= 1000 => 2, _ => 1 },
                i => { O.UpdateIntervalMs = i switch { 0 => 250, 2 => 1000, _ => 500 }; _refresh.Interval = TimeSpan.FromMilliseconds(RefreshMs); Changed(); }, "Rate",
                new[] { "Smoothest numbers, slightly more CPU", "Redraws twice a second", "The lightest; numbers jump less" }),
            D(() => O.UpdateIntervalMs == 500, () => O.UpdateIntervalMs = 500, "2 " + Units.Hz)), "overlay.rate"));
        general.Children.Add(Ui.SettingRow("FPS statistics", "Averages and 1% low, per game session.", ResetButton()));
        general.Children.Add(Anchored(Ui.SettingRow("Show / hide", null, hotkey), "overlay.hotkey"));
        general.Children.Add(Anchored(Ui.SettingRow("Reset FPS statistics", null,
            HotkeyEditor("overlay.fps", () => O.ResetFpsHotkey, hk => O.ResetFpsHotkey = hk)), "overlay.fpshotkey"));
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
        general.Children.Add(Anchored(Ui.SettingRow("Frame-time graph", null,
            Ui.Switch(L.ShowGraph, v => { L.ShowGraph = v; Changed(); RenderPreview(); }),
            D(() => L.ShowGraph == std.ShowGraph, () => L.ShowGraph = std.ShowGraph, std.ShowGraph ? "On" : "Off")), "overlay.graph"));
        BuildColors(general);
        _left.Children.Add(Card(general));
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
        colors.Children.Add(Anchored(Ui.SettingRow("Colors", null,
            ThemeMenu.OverlayButton(
                () => new ThemeMenu.Pick(L.Colors.IsApp, false, L.Colors.Theme ?? ThemeManager.Current.Id, L.ColorSet),
                pick =>
                {
                    if (pick.FollowApp) { L.Colors = OverlayColorPreset.App; L.ColorSet = null; }
                    else { L.Colors = OverlayLayout.For(pick.Theme); L.ColorSet = pick.Set; }
                    Wiping(Changed);
                    BuildSettings();
                }, followApp: true, customLabel: null, minWidth: 162)), "overlay.colors"));

        _contrast.Style = Ui.Res<Style>("Caption");
        _contrast.Foreground = Ui.Res<Brush>("WarnDeep");
        _contrast.Margin = new Thickness(0, 6, 0, 0);
        colors.Children.Add(Detached(_contrast));
    }

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
        _lit = default;
        _motion.Spotlight(null, null);
        var metrics = new StackPanel { Margin = new Thickness(0, 11, 0, 0) };
        var head = new DockPanel { LastChildFill = false };
        var rename = Ui.LabeledButton("I.Pencil", "Rename items…", () => _ = RenameItemsAsync());
        rename.ToolTip = "Your own names for the groups and values the overlay shows (e.g. GPU → 7900 XTX). The layout's own name is under the pencil next to Layout.";
        DockPanel.SetDock(rename, Dock.Right);
        head.Children.Add(Anchored(rename, "overlay.names"));
        var title = Ui.Text("What to show", "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        metrics.Children.Add(head);
        var groups = L.GroupOrder().Select(g => (Group: g, Entries: L.Metrics.Where(m => OverlayLayout.GroupOf(m.Metric) == g).ToList())).ToList();
        var sizes = groups.Select(g => g.Entries.Count + 1.0).ToArray();
        var cards = new MasonryPanel
        {
            MinColumnWidth = MetricCardWidth, MaxColumns = 4, Gap = 8, Margin = new Thickness(0, 10, 0, 0),
            ColumnOf = (columns, index) => index < sizes.Length ? SplitInOrder(sizes, columns)[index] : null,
        };
        foreach (var (group, entries) in groups)
            cards.Children.Add(MetricCard(group, entries));
        metrics.Children.Add(cards);
        Ui.Anchor(metrics, "overlay.metrics");
        _position.Children.Add(Ui.Divider());
        _position.Children.Add(metrics);
    }

    private const double MetricCardWidth = 150;

    internal static int[] SplitInOrder(IReadOnlyList<double> sizes, int columns)
    {
        var result = new int[sizes.Count];
        if (sizes.Count == 0 || columns <= 1) return result;
        double low = sizes.Max(), high = sizes.Sum();
        for (int step = 0; step < 40; step++)
        {
            double mid = (low + high) / 2;
            if (ColumnsNeeded(sizes, mid) <= columns) high = mid; else low = mid;
        }
        int column = 0;
        double filled = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            if (filled > 0 && filled + sizes[i] > high + 1e-6 && column < columns - 1) { column++; filled = 0; }
            filled += sizes[i];
            result[i] = column;
        }
        return result;
    }

    private static int ColumnsNeeded(IReadOnlyList<double> sizes, double capacity)
    {
        int columns = 1;
        double filled = 0;
        foreach (var size in sizes)
        {
            if (filled > 0 && filled + size > capacity + 1e-6) { columns++; filled = 0; }
            filled += size;
        }
        return columns;
    }

    private Tile MetricCard(string group, List<OverlayMetricEntry> entries)
    {
        var name = new TextBlock { Text = group, Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center };
        var count = RaceParts.LiveFigure("", 10.5, "TextFaint", italic: false, heavy: false);
        count.Margin = new Thickness(7, 0, 0, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        var allLamp = new PitBox.Controls.Lamp();
        var partly = new System.Windows.Shapes.Rectangle { Width = 7, Height = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, SnapsToDevicePixels = true, Visibility = Visibility.Collapsed };
        partly.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
        bool several = entries.Count > 1;
        var title = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, count } };
        var lampHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Children = { allLamp, partly } };
        DockPanel.SetDock(lampHost, Dock.Left);
        FrameworkElement headFace = several ? new DockPanel { LastChildFill = true, Children = { lampHost, title } } : title;
        var head = new Border { Padding = new Thickness(16, 0, 10, 0), MinHeight = 34, Background = Brushes.Transparent, Child = headFace };
        headFace.VerticalAlignment = VerticalAlignment.Center;
        if (several)
        {
            head.Cursor = Cursors.Hand;
            head.Focusable = true;
            head.FocusVisualStyle = Ui.Res<Style>("FocusRing");
            head.ToolTip = $"Show or hide every {group} value";
            AutomationProperties.SetName(head, $"All {group} values");
            head.MouseEnter += (_, _) => allLamp.Hot = true;
            head.MouseLeave += (_, _) => allLamp.Hot = false;
        }

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(Hairline.Rule(new Border { Background = Ui.Res<Brush>("Line") }));
        var setters = new List<Action<bool>>();

        void Sync()
        {
            int on = entries.Count(e => e.Enabled);
            TextCase.Set(count, $"{on}/{entries.Count}");
            allLamp.On = on == entries.Count;
            bool some = on > 0 && on < entries.Count;
            partly.Visibility = some ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetItemStatus(head, on == entries.Count ? "On" : some ? "Partly on" : "Off");
            name.SetResourceReference(TextBlock.ForegroundProperty, on == 0 ? "TextFaint" : "AccentText");
        }

        foreach (var entry in entries)
        {
            var label = TextFit.TipWhenTrimmed(new TextBlock { Text = ShortLabel(entry.Metric), Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var bar = new PitBox.Controls.Lamp();
            var face = ToggleCards.WithLamp(bar, label);
            face.Margin = new Thickness(10, 0, 10, 0);
            face.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { MinHeight = 36, Background = Brushes.Transparent, Children = { face } };
            var tile = new Border
            {
                Child = row, Cursor = Cursors.Hand, ToolTip = MetricLabel(entry.Metric), Focusable = true, FocusVisualStyle = Ui.Res<Style>("FocusRing"),
                Margin = new Thickness(6, 4, 6, 0), BorderThickness = Hairline.Edges(),
            };
            tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            tile.SetResourceReference(Border.BorderBrushProperty, "Line");
            tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "Line");
            AutomationProperties.SetName(tile, MetricLabel(entry.Metric));

            void Show(bool on)
            {
                label.Tag = on;
                Paint(label, on);
                bar.On = on;
                AutomationProperties.SetItemStatus(tile, on ? "On" : "Off");
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
            tile.MouseLeftButtonUp += (_, e) => { Apply(!entry.Enabled); e.Handled = true; };
            tile.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Space or Key.Enter)) return;
                e.Handled = true;
                Apply(!entry.Enabled);
            };
            row.MouseEnter += (_, _) => Light(group, entry.Metric);
            row.MouseLeave += (_, _) => Light(null, null);
            _metricRows[entry.Metric] = (row, label);
            setters.Add(on => { entry.Enabled = on; Show(on); });
            Show(entry.Enabled);
            body.Children.Add(tile);
        }

        void ToggleAll()
        {
            bool on = entries.Any(e => !e.Enabled);
            Morphing(() =>
            {
                setters.ForEach(set => set(on));
                Sync();
                Changed();
            });
        }
        if (several)
        {
            head.MouseLeftButtonUp += (_, e) => { ToggleAll(); e.Handled = true; };
            head.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Space or Key.Enter)) return;
                e.Handled = true;
                ToggleAll();
            };
        }
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
        _ => "Speeds",
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

    private async Task ResetLayoutAsync()
    {
        var layout = L;
        if (!await _window.Confirm($"Reset “{layout.Name}” to default?", "What it shows, its order, names, look and place on screen go back to the defaults.", "Reset", danger: true))
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

    private static Border Card(UIElement child) => new Tile { Style = Ui.Res<Style>("Card"), Child = child, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing) };

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
            : L.ShowGraph ? "Sample FPS and frame times until a game is running." : "Sample FPS until a game is running.";
        try
        {
            double dpi = VisualTreeHelper.GetDpi(_preview).DpiScaleX;
            using var bitmap = _app.Overlay.RenderPreview(L, stats, times, (float)dpi);
            _preview.Source = BitmapConvert.ToSource(bitmap, dpi);
            bool closeUp = L.Style == OverlayStyle.Compact;
            _closeUp.Source = closeUp ? _preview.Source : null;
            _closeUpBox.Visibility = closeUp ? Visibility.Visible : Visibility.Collapsed;
            double wasWidth = _overlaySize.W;
            _overlaySize = (bitmap.Width / dpi, bitmap.Height / dpi);
            if (_growNext && wasWidth > 0)
                _motion.Grow(wasWidth / _overlaySize.W, L.Corner is OverlayCorner.TopRight or OverlayCorner.BottomRight, L.Corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight);
            _growNext = false;
        }
        catch
        {
            return;
        }
        PlacePreview();
        if (_lit.Group is not null) _motion.Spotlight(_lit.Group, _lit.Metric);
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("compact-color-set", () =>
        {
            L.Style = OverlayStyle.Compact;
            L.ShowGraph = true;
            L.Colors = OverlayLayout.For(AppTheme.Custom);
            L.ColorSet = "Synthwave";
            Changed();
            BuildSettings();
            return Task.CompletedTask;
        });
    }
}
