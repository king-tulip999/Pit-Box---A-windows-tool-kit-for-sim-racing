using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Config;
using Core.Controls;
using Core.Plugins;
using Core.Plugins.BuiltIn;
using Core.Services;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Editors;
using Core.Theme;
using Core.Views.HudSettings;

namespace Core.Views.Pages;

internal sealed partial class RaceHudWidgets
{
    private static readonly string[] SessionKinds = { "Practice", "Qualify", "Race", "TimeTrial" };

    private readonly AppServices _app;
    private readonly IHudEditorHost _section;
    private readonly ContentControl _list = new() { Focusable = false };
    private readonly HudPainter _painter = new() { Items = new(), Regions = new(), Overflow = new() };
    private Image? _preview;
    private Canvas? _previewMarks;
    private Canvas? _previewHighlight;
    private float _previewScale = 1;
    private double _previewPixels = 1;
    private readonly TranslateTransform _previewNudge = new();
    private (string Key, string Id, Point From)? _press;
    private Point? _click;
    private bool _dragging;
    private Border? _previewWell;
    private TextBlock? _previewNote;
    private FrameworkElement? _previewHidden;
    private FrameworkElement? _pickedCard;
    private string? _selected;
    private HudSettingContext? _context;
    private readonly List<(HudSetting Setting, FrameworkElement Row)> _rows = new();
    private string? _hoverSetting;

    public RaceHudWidgets(AppServices app, IHudEditorHost section)
    {
        _app = app;
        _section = section;
        Root = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(15, 13, 15, 15), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = _list };
        Root.Unloaded += (_, _) => ReleaseNative();
        Fill();
    }

    private void ReleaseNative()
    {
        _previewTimer?.Stop();
        _painter.ReleaseText();
        _picturePainter.ReleaseText();
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _pictureBitmap?.Dispose();
        _pictureBitmap = null;
    }


    public FrameworkElement Root { get; }

    private RaceHudSettings S => _app.Config.SimRacing.Hud;
    private HudHost? Host => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;

    private HudLayout? Layout => S.Layout(_section.ShownLayout) ?? S.Layouts.FirstOrDefault();

    private string NameOf(HudWidgetConfig w)
    {
        string name = HudCatalog.Find(w.Kind)?.Name ?? w.Kind;
        if (Layout?.Widgets is not { } all) return name;
        int index = 0, count = 0;
        foreach (var other in all)
        {
            if (other.Kind != w.Kind) continue;
            if (ReferenceEquals(other, w)) index = count;
            count++;
        }
        return count > 1 && index > 0 ? $"{name} {index + 1}" : name;
    }

    private string? _filledFor;

    public void FollowLayout()
    {
        if (Layout?.Name == _filledFor) return;
        _selected = null;
        _openPanel = null;
        Host?.Pick(null);
        Fill();
    }

    private void Changed()
    {
        _section.Changed();
        Host?.Pick(_selected);
    }

    public void SelectId(string? id)
    {
        if (id == _selected) return;
        _selected = id;
        _openPanel = null;
        Fill();
        _list.Dispatcher.BeginInvoke(ScrollToPicked, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    internal void SetOption(string kind, string key, string? value)
    {
        if (Layout?.Widgets.FirstOrDefault(w => w.Kind == kind) is not { } w) return;
        if (value is null) w.Options.Remove(key); else w.Options[key] = value;
        _selected = null;
        Select(kind);
    }

    internal void ShowAll(string kind) => Open(kind, new PanelTarget(null, null, null));

    internal void Reveal(string kind, string settingId) => Open(kind, new PanelTarget(settingId, null, null));

    private void Open(string kind, PanelTarget target)
    {
        _selected = Layout?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? _selected;
        _openPanel = target;
        Fill();
    }

    public void Select(string kind)
    {
        _selected = Layout?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? _selected;
        _openPanel = null;
        Fill();
        _list.Dispatcher.BeginInvoke(ScrollToPicked, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    internal void ScrollPageBy(double dy)
    {
        if (PageOf(_pickedCard) is { } page) page.ScrollToVerticalOffset(page.VerticalOffset + dy);
    }

    private static ScrollViewer? PageOf(DependencyObject? d)
    {
        while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        return d as ScrollViewer;
    }

    private void ScrollToPicked()
    {
        if (_pickedCard is not { IsLoaded: true } card) return;
        if (PageOf(card) is not { } page) { card.BringIntoView(); return; }
        double top = card.TransformToVisual(page).Transform(new Point(0, 0)).Y;
        page.ScrollToVerticalOffset(page.VerticalOffset + top - 24);
    }

    private System.Windows.Threading.DispatcherTimer? _previewTimer;
    private long _heartbeat;
    private System.Drawing.Bitmap? _previewBitmap;
    private System.Windows.Media.Imaging.WriteableBitmap? _previewTarget;

    private HudWidgetConfig? Selected() => _selected is null ? null : Layout?.Widgets.FirstOrDefault(w => w.Id == _selected);

    public void Tick()
    {
        _heartbeat = Environment.TickCount64;
        if (_preview is null || Selected() is not { } w) return;
        int hz = Math.Clamp(w.UpdateHz > 0 ? w.UpdateHz : HudCatalog.Find(w.Kind) is { } def ? def.Hz(new HudOptions(def, w)) : 10, 1, 60);
        var interval = TimeSpan.FromMilliseconds(1000.0 / hz);
        if (_previewTimer is null)
        {
            _previewTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render);
            _previewTimer.Tick += (_, _) => PreviewFrame();
        }
        if (_previewTimer.Interval != interval) _previewTimer.Interval = interval;
        if (!_previewTimer.IsEnabled) _previewTimer.Start();
    }

    private void PreviewFrame()
    {
        if (Environment.TickCount64 - _heartbeat > 800 || !Root.IsLoaded) { _previewTimer!.Stop(); return; }
        if (_dragging) return;
        if (_preview is null || Selected() is not { } w) return;
        _section.Preview.Advance(Host);
        DrawPreview(w);
    }

    private void Fill()
    {
        _preview = null;
        _previewWell = null;
        _well = null;
        _panelLayer = null;
        _panel = null;
        _panelScroll = null;
        _pin = (null, null);
        _laidSize = default;
        _previewHidden = null;
        _previewHighlight = null;
        _pickedCard = null;
        _context = null;
        _rows.Clear();
        var layout = Layout;
        _filledFor = layout?.Name;
        var widgets = layout?.Widgets ?? new List<HudWidgetConfig>();
        if (_selected is null || widgets.All(x => x.Id != _selected)) _selected = widgets.FirstOrDefault()?.Id;

        var body = new StackPanel();
        var picked = widgets.FirstOrDefault(x => x.Id == _selected);
        var bar = WidgetBar(widgets, picked);
        _pickedCard = bar;
        body.Children.Add(bar);
        if (picked is null)
            body.Children.Add(new TextBlock { Text = "No widgets in this layout yet: add one.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        else
        {
            var def = HudCatalog.Find(picked.Kind);
            if (def is null) body.Children.Add(TitleCard(NameOf(picked), "This widget was made by a newer version of Velocita. It's kept, but not drawn.", null));
            if (def is not null)
            {
                body.Children.Add(PreviewArea(picked, def));
                var (cards, _, _) = WidgetPanels(picked, def);
                cards.Margin = new Thickness(0, Ui.PanelSpacing, 0, 0);
                body.Children.Add(cards);
            }
        }
        _list.Content = body;
        RiseIn(body);
        if (_preview is not null && Selected() is { } shown2)
        {
            if (_previewWell is { ActualWidth: > 40 }) DrawPreview(shown2);
            ShowPreviewHidden(shown2);
        }
        if (_openPanel is { } reopen && _well is not null)
            _list.Dispatcher.BeginInvoke(() => { if (_openPanel == reopen) OpenPanel(reopen); }, System.Windows.Threading.DispatcherPriority.Loaded);
        else _openPanel = null;
    }

    private const double ComboWidth = 194;

    private const double PanelMinWidth = 330;

    private (FrameworkElement Body, MasonryPanel Grid, FrameworkElement[] Side) WidgetPanels(HudWidgetConfig w, HudWidgetDef def)
    {
        static Tile Card(string title, IEnumerable<FrameworkElement> rows)
        {
            var box = new SettingGroup(title, true, null, foldable: false);
            foreach (var r in rows) box.Add(r);
            return new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(10, 2, 12, 6), Child = box };
        }

        var when = new List<FrameworkElement>();
        if (def.HideWhenEmpty is { } hideByDefault)
            when.Add(SettingRows.Row("Only when needed", def.Kind == "alerts" ? "Hidden until an alert comes up." : "Hidden until a car of another class is near.", true,
                Ui.Switch(w.OnlyWhenNeeded ?? hideByDefault, v => { w.OnlyWhenNeeded = v; Changed(); })));
        when.Add(SettingRows.Row("Only while driving", "Hidden in the garage and while you're out of the car.", true, Ui.Switch(w.OnlyOnTrack, v => { w.OnlyOnTrack = v; Changed(); })));
        when.Add(SettingRows.Row("Hide in replays", null, true, Ui.Switch(w.HideInReplay, v => { w.HideInReplay = v; Changed(); })));
        var sessions = SettingRows.Toggles(SessionKinds.Select(k => (k, SessionLabel(k), (string?)null)).ToList(),
            kind => w.Sessions.Contains(kind, StringComparer.OrdinalIgnoreCase),
            (kind, on) =>
            {
                w.Sessions.RemoveAll(s => string.Equals(s, kind, StringComparison.OrdinalIgnoreCase));
                if (on) w.Sessions.Add(kind);
                Changed();
            });
        when.Add(SettingRows.Row("Only in", "None ticked = every session.", true, sessions));
        if (Layout is { } owner) when.Add(SettingRows.Row("Keybind", null, true, KeybindBox(owner, w)));

        var look = new List<FrameworkElement>();
        foreach (var colorRow in RaceHudSection.LookRows(S.Appearance, () => { Changed(); if (_preview is not null) DrawPreview(w); }, () => _list.Dispatcher.BeginInvoke(Fill, System.Windows.Threading.DispatcherPriority.Background), (t, d, c, _) => SettingRows.Row(t, d, true, c)))
            look.Add((FrameworkElement)colorRow);
        look.Add(SettingRows.Row("Opacity", null, true, Ui.SliderWithValue(20, 100, w.Opacity, "{0:0}%", v => { w.Opacity = (int)v; Changed(); if (_preview is not null) DrawPreview(w); }, ComboWidth - 42, 5)));
        var background = Ui.SliderWithValue(0, 100, w.Background ?? S.Appearance.Background, "{0:0}%", v => { w.Background = (int)v; Changed(); if (_preview is not null) DrawPreview(w); }, ComboWidth - 42, 5);
        var backgroundRow = SettingRows.Row("Background", null, true, background);
        backgroundRow.Visibility = w.Background is not null ? Visibility.Visible : Visibility.Collapsed;
        var own = Ui.Switch(w.Background is not null, on =>
        {
            w.Background = on ? S.Appearance.Background : null;
            backgroundRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Changed();
            if (_preview is not null) DrawPreview(w);
        });
        look.Add(SettingRows.Row("Own background", "Off: uses the overlay's background.", true, own));
        look.Add(backgroundRow);
        var rates = new[] { 0, 1, 2, 5, 10, 15, 20, 30, 60 }.Select(r => new Choice(r.ToString(), r == 0 ? def.MovingHz > 0 ? $"Default ({def.DefaultHz} per second, {def.MovingHz} while it moves)" : $"Default ({def.DefaultHz} per second)" : $"{r} per second"));
        look.Add(SettingRows.Row("Redraw", null, true, Ui.Combo(rates, w.UpdateHz.ToString(), c => { w.UpdateHz = int.Parse(c.Id!); Changed(); }, ComboWidth)));

        var place = new List<FrameworkElement>();
        var monitors = HudHost.MonitorChoices().Select(m => new Choice(m.Id, m.Label)).ToList();
        place.Add(SettingRows.Row("Monitor", null, true, Ui.Combo(monitors, w.Monitor, c => { w.Monitor = c.Id!; Changed(); }, ComboWidth)));
        place.Add(SettingRows.Row("Locked", "Can't be dragged while arranging on screen.", true, Ui.Switch(w.Locked, v => { w.Locked = v; Changed(); })));
        var scaleText = new TextBlock { Text = $"{w.Scale * 100:0}%", Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var resetScale = Ui.LabeledButton("I.Undo", "Reset to 100%", () =>
        {
            Host?.RecordUndo();
            w.Scale = 1;
            Changed();
            Fill();
        }, "GhostButton");
        resetScale.Visibility = Math.Abs(w.Scale - 1) > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        place.Add(SettingRows.Row("Scale", "Drag the widget's corner while arranging on screen to scale it.", true,
            new StackPanel { Orientation = Orientation.Horizontal, Children = { scaleText, resetScale } }));
        var home = Ui.LabeledButton("I.Target", "Bring back to the middle", () =>
        {
            Host?.RecordUndo();
            w.Monitor = "primary";
            w.Anchor = HudAnchor.Center;
            w.X = 0;
            w.Y = 0;
            w.Clamp();
            Changed();
            Fill();
        }, "GhostButton");
        home.ToolTip = "Puts this widget in the middle of the main display, e.g. when it's off screen or behind something";
        home.HorizontalAlignment = HorizontalAlignment.Left;
        home.Margin = new Thickness(0, 4, 0, 4);
        place.Add(home);

        var web = Card("Browser source", BrowserSourceRows(w));
        Ui.Anchor(web, RaceHudSection.WebAnchor);

        var panels = new MasonryPanel { MinColumnWidth = PanelMinWidth, MaxColumns = 4, Gap = 14, EvenBottoms = true };
        panels.Children.Add(Card("When shown", when));
        var lookCard = Card("Panel and redraw", look);
        var placeCard = Card("Placement", place);
        panels.Children.Add(web);
        panels.Children.Add(lookCard);
        panels.Children.Add(placeCard);
        return (new StackPanel { Children = { panels, ActionsRow(w) } }, panels, new FrameworkElement[] { lookCard, placeCard, web });
    }

    private List<FrameworkElement> BrowserSourceRows(HudWidgetConfig w)
    {
        var url = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var copy = Ui.LabeledButton("I.Copy", "Copy URL", () =>
        {
            if (Host?.WebPort is not { } live) return;
            string link = SimRacing.Hud.Web.HudWebServer.Url(live, w.Kind);
            try
            {
                Clipboard.SetText(link);
                _app.Notify("URL copied", link, NoticeKind.Success);
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        });
        ToolTipService.SetShowOnDisabled(copy, true);
        DockPanel.SetDock(copy, Dock.Right);
        var urlRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 4), Children = { copy, url } };
        bool running = Host?.WebPort is not null;
        url.Text = SimRacing.Hud.Web.HudWebServer.Url(Host?.WebPort ?? S.WebPort, w.Kind);
        url.SetResourceReference(TextBlock.ForegroundProperty, running ? "Heading" : "TextFaint");
        copy.IsEnabled = running;
        copy.ToolTip = running ? "This widget as a web page on this PC only, for a wheel screen or streaming software. It follows these options and stays blank outside a session."
            : S.WebOverlays ? "Not running: see Overlay settings" : "Off: turn on Browser sources in Overlay settings";
        var rows = new List<FrameworkElement> { urlRow };
        if (!running)
        {
            var off = Ui.LabeledButton("I.Settings", "Overlay settings", () => _section.ShowSettings(), "GhostButton");
            off.HorizontalAlignment = HorizontalAlignment.Left;
            off.Margin = new Thickness(0, 2, 0, 4);
            rows.Add(new TextBlock { Text = S.WebOverlays ? "Not running." : "Browser sources are off.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 4) });
            rows.Add(off);
        }
        return rows;
    }

    private HotkeyBox KeybindBox(HudLayout layout, HudWidgetConfig w)
    {
        var box = new HotkeyBox { Width = ComboWidth, ToolTip = "Works in any program" };
        box.SetHotkey(w.Hotkey);
        box.HotkeyChanged += hk =>
        {
            if (_app.Hotkeys.Clash(hk, SimRacingPlugin.WidgetHotkeyId(layout, w)) is { } clash)
            {
                _app.Notify("Keybind not changed", clash, NoticeKind.Warning, important: true);
                box.SetHotkey(w.Hotkey);
                return;
            }
            w.Hotkey = string.IsNullOrWhiteSpace(hk) ? null : hk;
            _app.Save();
            _app.Hotkeys.RegisterAll();
        };
        return box;
    }

    private FrameworkElement ActionsRow(HudWidgetConfig w)
    {
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var dup = Ui.LabeledButton("I.Copy", "Duplicate", () =>
        {
            var copy = w.Copy();
            copy.Id = HudWidgetConfig.NewId(w.Kind);
            copy.Y += 24;
            Layout!.Widgets.Add(copy);
            _selected = copy.Id;
            Changed();
            Fill();
        });
        dup.Margin = Ui.AfterButton;
        actions.Children.Add(dup);
        var reset = Ui.LabeledButton("I.Undo", "Default options", () =>
        {
            var before = new Dictionary<string, string>(w.Options, StringComparer.OrdinalIgnoreCase);
            w.Options.Clear();
            Changed();
            Fill();
            _app.Notify("Options reset", "This widget's display options are back to their defaults.", NoticeKind.Success, action: new NoticeAction("Undo", () =>
            {
                w.Options = before;
                Changed();
                Fill();
            }));
        });
        reset.ToolTip = "Puts this widget's display options back as they were when it was added (place and size stay)";
        reset.Margin = Ui.AfterButton;
        actions.Children.Add(reset);
        return actions;
    }

    private FrameworkElement WidgetBar(List<HudWidgetConfig> widgets, HudWidgetConfig? picked)
    {
        var def = picked is null ? null : HudCatalog.Find(picked.Kind);
        var name = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = picked is null ? "No widgets" : NameOf(picked), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        var pick = new Button { Style = Ui.Res<Style>("MenuButton"), Width = WidgetPickWidth, Content = Ui.MenuFace(name), ToolTip = "Pick a widget to edit, show or hide widgets, or add one" };
        System.Windows.Automation.AutomationProperties.SetName(pick, "Widget");
        pick.Click += (_, _) => WidgetMenu(pick, widgets);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { pick } };
        if (picked is not null)
        {
            var w = picked;
            var eye = Ui.IconButton(w.Enabled ? "I.Eye" : "I.EyeOff", w.Enabled ? "Shown in game: click to hide it" : "Hidden in game: click to show it", () =>
            {
                w.Enabled = !w.Enabled;
                Changed();
                Fill();
            });
            eye.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
            eye.SetResourceReference(Control.ForegroundProperty, w.Enabled ? "Heading" : "TextMuted");
            System.Windows.Automation.AutomationProperties.SetItemStatus(eye, w.Enabled ? "Shown" : "Hidden");
            row.Children.Add(eye);
            string about = string.Join("\n\n", new[] { def?.Description, def?.Limits }.OfType<string>());
            if (about.Length > 0)
            {
                var tip = HelpTip.For(about, NameOf(w));
                tip.VerticalAlignment = VerticalAlignment.Center;
                tip.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
                row.Children.Add(tip);
            }
        }
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(row);
        if (picked is not null)
        {
            var remove = RemoveButton(picked);
            Grid.SetColumn(remove, 1);
            grid.Children.Add(remove);
        }
        return grid;
    }

    private const double WidgetPickWidth = 260;

    private void WidgetMenu(Button anchor, List<HudWidgetConfig> widgets)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, MinWidth = WidgetPickWidth };
        Ui.DropBelow(menu);
        bool toggled = false;
        foreach (var w in widgets)
        {
            var widget = w;
            var name = new TextBlock { Text = NameOf(w), VerticalAlignment = VerticalAlignment.Center, Opacity = w.Enabled ? 1 : ToggleCards.OffOpacity };
            var eye = Ui.IconButton(w.Enabled ? "I.Eye" : "I.EyeOff", "Show or hide " + NameOf(w), () => { }, "QuietIconButton");
            eye.Margin = new Thickness(12, 0, 0, 0);
            eye.SetResourceReference(Control.ForegroundProperty, w.Enabled ? "Heading" : "TextMuted");
            eye.ToolTip = w.Enabled ? "Shown in game: click to hide it" : "Hidden in game: click to show it";
            eye.Click += (_, e) =>
            {
                e.Handled = true;
                widget.Enabled = !widget.Enabled;
                toggled = true;
                Changed();
                eye.Content = new Icon { Data = Ui.Res<Geometry>(widget.Enabled ? "I.Eye" : "I.EyeOff"), Width = 13, Height = 13 };
                eye.SetResourceReference(Control.ForegroundProperty, widget.Enabled ? "Heading" : "TextMuted");
                eye.ToolTip = widget.Enabled ? "Shown in game: click to hide it" : "Hidden in game: click to show it";
                name.Opacity = widget.Enabled ? 1 : ToggleCards.OffOpacity;
            };
            var head = new DockPanel { LastChildFill = true, Children = { eye, name } };
            DockPanel.SetDock(eye, Dock.Right);
            var item = new MenuItem { Header = head, IsCheckable = true, IsChecked = w.Id == _selected, StaysOpenOnClick = false };
            item.Click += (_, _) =>
            {
                if (widget.Id == _selected) return;
                _selected = widget.Id;
                _openPanel = null;
                Host?.Pick(widget.Id);
                Fill();
            };
            menu.Items.Add(item);
        }
        if (widgets.Count > 0) menu.Items.Add(new Separator());
        var add = new MenuItem { Header = "Add widget", Icon = new Icon { Data = Ui.Res<Geometry>("I.Plus"), Width = 12, Height = 12 } };
        foreach (var def in HudCatalog.All.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var kind = def;
            var sub = new MenuItem { Header = def.Name, ToolTip = def.Description };
            sub.Click += (_, _) => Add(kind);
            add.Items.Add(sub);
        }
        menu.Items.Add(add);
        menu.Closed += (_, _) => { if (toggled) Fill(); };
        menu.IsOpen = true;
    }

    private static FrameworkElement TitleCard(string title, string text, FrameworkElement? toggle, string? more = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (more is null) words.Children.Add(name);
        else
        {
            var tip = HelpTip.For(more, title);
            tip.VerticalAlignment = VerticalAlignment.Center;
            words.Children.Add(new TrimRow { Children = { name, tip } });
        }
        words.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 13, 0) });
        grid.Children.Add(words);
        if (toggle is not null)
        {
            toggle.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
        }
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 13, 16, 13), Margin = new Thickness(0, 0, 0, 8), Child = grid };
    }

    private FrameworkElement Linked(HudSetting setting, FrameworkElement row)
    {
        var box = new Border { Child = row, Tag = setting.Id, Background = Brushes.Transparent, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(-5, 0, -5, 0) };
        box.MouseEnter += (_, _) => Highlight(setting.Id);
        box.MouseLeave += (_, _) => { if (!box.IsKeyboardFocusWithin && _hoverSetting == setting.Id) Highlight(null); };
        box.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is true) Highlight(setting.Id);
            else if (_hoverSetting == setting.Id && !box.IsMouseOver) Highlight(null);
        };
        return box;
    }

    private void RefreshVisibility()
    {
        if (_context is null) return;
        var o = _context.Options;
        foreach (var (setting, row) in _rows) row.Visibility = setting.Visible(o) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (head, rows) in _groupHeads) head.Visibility = rows.Any(r => r.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetValues(HudWidgetConfig w, HudWidgetDef def, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (key, value) in values)
        {
            if (def.Option(key) is not { } o) continue;
            if (value is null || value == o.Default) w.Options.Remove(key); else w.Options[key] = value;
        }
        Changed();
        if (def.EditorDef.Settings.OfType<PickerSetting>().Any(p => p.OffersFollow is { } follows && values.ContainsKey(follows)))
        {
            Fill();
            return;
        }
        if (_preview is not null) DrawPreview(w);
        RefreshVisibility();
        _well?.Dispatcher.BeginInvoke(PlacePanel, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static string SessionLabel(string kind) => kind switch { "Qualify" => "Qualifying", "TimeTrial" => "Time trial", _ => kind };

    private Button RemoveButton(HudWidgetConfig w)
    {
        var remove = Ui.IconButton("I.Trash", "Remove this widget from the layout", async () =>
        {
            if (!await _section.Confirm($"Remove this {HudCatalog.Find(w.Kind)?.Name ?? w.Kind}?", $"It's taken out of the {Layout?.Name} layout with its settings.", "Remove", danger: true)) return;
            Layout?.Widgets.Remove(w);
            if (_selected == w.Id) _selected = null;
            Changed();
            if (w.Hotkey is not null) _app.Hotkeys.RegisterAll();
            Fill();
        }, "QuietIconButton");
        remove.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + (HudCatalog.Find(w.Kind)?.Name ?? w.Kind));
        return remove;
    }

    public void WarmPreview()
    {
        if ((Selected() ?? Layout?.Widgets.FirstOrDefault()) is { } w && Host is { } host) host.SizeOf(w, _section.Preview.Race);
    }

    private void DrawPreview(HudWidgetConfig w)
    {
        if (_preview is null) return;
        if (Host is not { } host) { _previewNote!.Text = "The preview needs the Sim Racing Tools plugin on."; return; }
        var race = _section.Preview.Race;
        try
        {
            double room = _well is { ActualWidth: > 40 } well ? well.ActualWidth - WellPad * 2 : 560;
            var laid = host.SizeOf(w, race);
            float scale = (float)Math.Clamp(room / Math.Max(1, laid.Width), MinPreviewScale, 1);
            AlignPreview();
            double k = _previewPixels;
            host.RenderPreview(w, race, _painter, (float)(scale * k), ref _previewBitmap);
            var bitmap = _previewBitmap!;
            _previewScale = scale;
            if (_previewTarget is null || _previewTarget.PixelWidth != bitmap.Width || _previewTarget.PixelHeight != bitmap.Height || Math.Abs(_previewTarget.DpiX - 96 * k) > 0.01)
                _previewTarget = new System.Windows.Media.Imaging.WriteableBitmap(bitmap.Width, bitmap.Height, 96 * k, 96 * k, PixelFormats.Pbgra32, null);
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try { _previewTarget.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), data.Scan0, data.Stride * bitmap.Height, data.Stride); }
            finally { bitmap.UnlockBits(data); }
            if (_preview.Source != _previewTarget) _preview.Source = _previewTarget;
            _preview.Opacity = w.Opacity / 100.0;
            _preview.Width = bitmap.Width / k;
            _preview.Height = bitmap.Height / k;
            string size = Math.Abs(scale - 1) < 0.01 ? "actual size" : $"{scale * 100:0}% of its size";
            string note = (race.Frame.Simulated ? "Sample data (no live session)" : "Live from iRacing") + $" · {size}";
            if (!TextCase.Plain(_previewNote!.Text).Equals(note, StringComparison.OrdinalIgnoreCase)) _previewNote.Text = note;
            DrawHighlight();
            if (_laidSize != (laid.Width, laid.Height, scale))
            {
                _laidSize = (laid.Width, laid.Height, scale);
                _laidWidth = laid.Width;
                _laidHeight = laid.Height;
                if (_openPanel is null && _well is not null) _well.Height = WellHeight();
                else PlacePanel();
            }
        }
        catch (Exception ex)
        {
            _previewNote!.Text = "Couldn't draw the preview: " + ex.Message;
            App.LogError(ex);
        }
    }

    private void AlignPreview()
    {
        if (_preview is null || PresentationSource.FromVisual(_preview) is not { RootVisual: { } root, CompositionTarget: { } target }) return;
        var m = Matrix.Identity;
        DependencyObject? at = _preview;
        while (at is Visual v && !ReferenceEquals(v, root))
        {
            if (VisualTreeHelper.GetTransform(v) is { } t) m.Append(t.Value);
            var offset = VisualTreeHelper.GetOffset(v);
            m.Translate(offset.X, offset.Y);
            at = VisualTreeHelper.GetParent(v);
        }
        if (!ReferenceEquals(at, root)) return;
        if (!ScreenshotRunner.DrawsAt96) m.Append(target.TransformToDevice);
        double k = m.M11;
        if (k < 0.1 || Math.Abs(m.M22 - k) > 0.001 || m.M12 != 0 || m.M21 != 0) return;
        _previewPixels = k;
        var origin = m.Transform(new Point(0, 0));
        _previewNudge.X = Math.Clamp(_previewNudge.X - (origin.X - Math.Round(origin.X)) / k, -1, 1);
        _previewNudge.Y = Math.Clamp(_previewNudge.Y - (origin.Y - Math.Round(origin.Y)) / k, -1, 1);
    }

    private List<(string Id, System.Drawing.RectangleF Box)> Drawn(string key) =>
        (_painter.Items ?? new()).Where(i => i.Key == key).Select(i => (i.Id, i.Box)).ToList();

    private System.Drawing.PointF PainterPoint(Point at) => new((float)(at.X / _previewScale), (float)(at.Y / _previewScale));

    private (string Key, string Id)? ItemAt(Point at)
    {
        if (_painter.Items is not { } items) return null;
        var p = PainterPoint(at);
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i].Box.Contains(p) && Drawn(items[i].Key).Count > 1) return (items[i].Key, items[i].Id);
        return null;
    }

    private bool HasMovableItems() =>
        (_painter.Items ?? new()).GroupBy(i => i.Key).Any(g => g.Count() > 1);

    private const float MinPreviewScale = 0.5f;
    private (double Width, double Height, float Scale) _laidSize;
    private (string Key, string Id)? _hoverItem;

    private void Highlight(string? settingId, (string Key, string Id)? item = null)
    {
        if (_hoverSetting == settingId && _hoverItem == item) return;
        _hoverSetting = settingId;
        _hoverItem = item;
        _highlightBoxes.Clear();
        DrawHighlight();
    }

    private readonly List<System.Drawing.RectangleF> _highlightBoxes = new();
    private HudWidgetConfig? _highlightWidget;
    private Point? _lastHover;

    private void DrawHighlight()
    {
        if (_previewHighlight is null) return;
        var shown = _previewHighlight.Children;
        if (_hoverSetting is not { } id || _dragging || Selected() is not { } w || HudCatalog.Find(w.Kind) is not { } def)
        {
            if (shown.Count > 0) shown.Clear();
            return;
        }
        if (!ReferenceEquals(_highlightWidget, w))
        {
            _highlightWidget = w;
            _highlightBoxes.Clear();
        }
        List<System.Drawing.RectangleF> boxes;
        if (_hoverItem is { } item)
            boxes = (_painter.Items ?? new()).Where(i => i.Key == item.Key && i.Id == item.Id).Select(i => i.Box).ToList();
        else
        {
            var names = def.EditorDef.RegionsOf(id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            boxes = (_painter.Regions ?? new()).Where(r => names.Contains(r.Id)).Select(r => r.Box).ToList();
            foreach (var group in (_painter.Items ?? new()).Where(i => names.Contains(i.Key)).GroupBy(i => i.Key))
                boxes.Add(group.Select(i => i.Box).Aggregate(System.Drawing.RectangleF.Union));
        }
        if (boxes.Count > 0 || _hoverItem is not null)
        {
            _highlightBoxes.Clear();
            _highlightBoxes.AddRange(boxes);
        }
        double s = _previewScale;
        var dpi = VisualTreeHelper.GetDpi(_previewHighlight);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        double maxW = _previewHighlight.ActualWidth, maxH = _previewHighlight.ActualHeight;
        var edge = new Thickness(Math.Round(2 * sx) / sx, Math.Round(2 * sy) / sy, Math.Round(2 * sx) / sx, Math.Round(2 * sy) / sy);
        while (shown.Count > _highlightBoxes.Count) shown.RemoveAt(shown.Count - 1);
        while (shown.Count < _highlightBoxes.Count)
        {
            var mark = new Border { BorderThickness = edge, SnapsToDevicePixels = true };
            mark.SetResourceReference(Border.BorderBrushProperty, "AccentText");
            var fill = (Ui.Res<Brush>("AccentSoft") as SolidColorBrush)?.Clone();
            if (fill is not null) { fill.Opacity = 0.35; fill.Freeze(); mark.Background = fill; }
            shown.Add(mark);
        }
        for (int i = 0; i < _highlightBoxes.Count; i++)
        {
            var box = _highlightBoxes[i];
            var mark = (Border)shown[i];
            if (mark.BorderThickness != edge) mark.BorderThickness = edge;
            double l = Math.Max(0, box.X * s - 2), t = Math.Max(0, box.Y * s - 2);
            double r = box.Right * s + 2, b = box.Bottom * s + 2;
            if (maxW > 0) r = Math.Min(maxW, r);
            if (maxH > 0) b = Math.Min(maxH, b);
            double left = Math.Round(l * sx) / sx, top = Math.Round(t * sy) / sy;
            double width = Math.Max(4, Math.Round(r * sx) / sx - left), height = Math.Max(4, Math.Round(b * sy) / sy - top);
            if (mark.Width != width) mark.Width = width;
            if (mark.Height != height) mark.Height = height;
            if (Canvas.GetLeft(mark) != left) Canvas.SetLeft(mark, left);
            if (Canvas.GetTop(mark) != top) Canvas.SetTop(mark, top);
        }
    }

    private string? _dragBefore;
    private System.Windows.Vector _grab;
    private ImageBrush? _liftedLook;

    private void HookPreviewDrag(FrameworkElement host, HudWidgetConfig w)
    {
        var editor = HudCatalog.Find(w.Kind)?.EditorDef;
        host.MouseMove += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (_press is not { } press)
            {
                if (_click is not null || editor is null) return;
                if (_lastHover is { } last && (at - last).Length < 0.5) return;
                _lastHover = at;
                var part = PartAt(editor, at);
                host.Cursor = part is null ? HasParts(editor) ? null : Cursors.Hand : ItemAt(at) is not null ? Cursors.SizeAll : Cursors.Hand;
                if (part is { } hover) Highlight(hover.Setting, hover.Id is { } hoverId ? (hover.Key, hoverId) : null);
                else Highlight(_pin.Setting, _pin.Item);
                return;
            }
            if (!_dragging && (at - press.From).Length < 4) return;
            if (!_dragging)
            {
                _liftedLook = Lift(press.Key, press.Id);
                Highlight(null);
            }
            _dragging = true;
            Reorder(w, press.Key, press.Id, at);
            ShowDrag(press.Key, press.Id, at);
        };
        host.MouseLeave += (_, _) =>
        {
            _lastHover = null;
            if (_press is not null) return;
            Highlight(_pin.Setting, _pin.Item);
        };
        host.MouseLeftButtonDown += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (ItemAt(at) is not { } item)
            {
                _click = at;
                e.Handled = true;
                return;
            }
            _press = (item.Key, item.Id, at);
            _dragBefore = w.Options.GetValueOrDefault(item.Key);
            var box = Drawn(item.Key).FirstOrDefault(d => d.Id == item.Id).Box;
            _grab = new System.Windows.Vector(at.X - box.X * _previewScale, at.Y - box.Y * _previewScale);
            host.CaptureMouse();
            e.Handled = true;
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (_click is { } click)
            {
                _click = null;
                if ((at - click).Length >= 4 || editor is null) return;
                if (PartAt(editor, at) is { } part) OpenPanel(new PanelTarget(part.Setting, part.Key, part.Id));
                else if (!HasParts(editor)) OpenPanel(new PanelTarget(null, null, null));
                else ClosePanel();
                return;
            }
            if (_press is not { } press) return;
            bool dragged = _dragging;
            _press = null;
            EndDrag(host);
            if (dragged) Commit(w, press.Key);
            else if (editor?.SettingForRegion(press.Key) is { } id) OpenPanel(new PanelTarget(id, press.Key, press.Id));
        };
        host.LostMouseCapture += (_, _) =>
        {
            if (_press is not { } press) return;
            bool dragged = _dragging;
            EndDrag(host);
            if (!dragged) return;
            if (_dragBefore is null) w.Options.Remove(press.Key); else w.Options[press.Key] = _dragBefore;
            DrawPreview(w);
        };
    }

    private void EndDrag(FrameworkElement host)
    {
        _press = null;
        _dragging = false;
        _previewMarks?.Children.Clear();
        if (host.IsMouseCaptured) host.ReleaseMouseCapture();
    }

    private void Reorder(HudWidgetConfig w, string key, string id, Point at)
    {
        var def = HudCatalog.Find(w.Kind);
        if (def?.Option(key) is not { Kind: HudOptionKind.Columns } option) return;
        var drawn = Drawn(key);
        if (drawn.Count == 0) return;
        var box = drawn.FirstOrDefault(d => d.Id == id).Box;
        var centre = new System.Drawing.PointF((float)((at.X - _grab.X) / _previewScale + box.Width / 2), (float)((at.Y - _grab.Y) / _previewScale + box.Height / 2));
        var before = HudItemDrag.Before(drawn, id, centre);
        string? last = drawn.Where(d => d.Id != id).Select(d => d.Id).LastOrDefault();
        var order = new HudOptions(def, w).Columns(key);
        string value = string.Join(",", HudItemDrag.Move(order, id, before, last));
        if (value == string.Join(",", order)) return;
        if (value == option.Default) w.Options.Remove(key); else w.Options[key] = value;
        DrawPreview(w);
    }

    private void ShowDrag(string key, string id, Point at)
    {
        if (_previewMarks is null) return;
        _previewMarks.Children.Clear();
        double s = _previewScale;
        var drawn = Drawn(key);
        var (_, box) = drawn.FirstOrDefault(d => d.Id == id);
        if (box.Width <= 0) return;
        foreach (var (otherId, other) in drawn)
        {
            if (otherId == id) continue;
            var outline = Hairline.Frame(new Border { Width = other.Width * s, Height = other.Height * s, SnapsToDevicePixels = true });
            outline.SetResourceReference(Border.BorderBrushProperty, "AccentText");
            outline.Opacity = 0.6;
            Canvas.SetLeft(outline, other.X * s);
            Canvas.SetTop(outline, other.Y * s);
            _previewMarks.Children.Add(outline);
        }
        var slot = new Border { Width = box.Width * s, Height = box.Height * s, BorderThickness = new Thickness(1.5) };
        slot.SetResourceReference(Border.BorderBrushProperty, "AccentText");
        slot.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        slot.Opacity = 0.45;
        Canvas.SetLeft(slot, box.X * s);
        Canvas.SetTop(slot, box.Y * s);
        _previewMarks.Children.Add(slot);
        var lifted = new Border { Width = box.Width * s, Height = box.Height * s, BorderThickness = new Thickness(2) };
        lifted.SetResourceReference(Border.BorderBrushProperty, "Accent");
        lifted.Background = _liftedLook;
        Canvas.SetLeft(lifted, at.X - _grab.X);
        Canvas.SetTop(lifted, at.Y - _grab.Y);
        _previewMarks.Children.Add(lifted);
    }

    private ImageBrush? Lift(string key, string id)
    {
        if (_preview?.Source is not System.Windows.Media.Imaging.BitmapSource bitmap) return null;
        double s = _previewScale * _previewPixels;
        var (_, box) = Drawn(key).FirstOrDefault(d => d.Id == id);
        var crop = new Int32Rect((int)Math.Max(0, box.X * s), (int)Math.Max(0, box.Y * s), 0, 0);
        crop.Width = (int)Math.Min(bitmap.PixelWidth - crop.X, Math.Ceiling(box.Width * s));
        crop.Height = (int)Math.Min(bitmap.PixelHeight - crop.Y, Math.Ceiling(box.Height * s));
        if (crop.Width <= 0 || crop.Height <= 0) return null;
        var copy = new System.Windows.Media.Imaging.CroppedBitmap(bitmap, crop);
        var frozen = new System.Windows.Media.Imaging.WriteableBitmap(copy);
        frozen.Freeze();
        var brush = new ImageBrush(frozen) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        brush.Freeze();
        return brush;
    }

    private void Commit(HudWidgetConfig w, string key)
    {
        string? now = w.Options.GetValueOrDefault(key);
        if (now == _dragBefore) return;
        Changed();
        DrawPreview(w);
        RebuildPanel();
    }

    public void DrawEveryKind()
    {
        if (Host is not { } host) return;
        string? dir = ScreenshotRunner.Folder is { } folder ? System.IO.Path.Combine(folder, "hud-widgets") : null;
        if (dir is not null) System.IO.Directory.CreateDirectory(dir);
        foreach (var def in HudCatalog.All)
        {
            var config = new HudWidgetConfig { Id = "check-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
            using (var a = host.RenderPreview(config, _section.Preview.Race, _painter, 1))
                if (dir is not null) a.Save(System.IO.Path.Combine(dir, def.Kind + ".png"));
            config.W = def.MinW;
            config.H = def.MinH;
            using (var b = host.RenderPreview(config, _section.Preview.Race, _painter, 1))
                if (dir is not null) b.Save(System.IO.Path.Combine(dir, def.Kind + "-min.png"));
        }
    }

    private void Add(HudWidgetDef def)
    {
        var layout = Layout;
        if (layout is null) return;
        var w = new HudWidgetConfig { Id = HudWidgetConfig.NewId(def.Kind), Kind = def.Kind, W = def.W, H = def.H, Anchor = HudAnchor.Center, Background = def.Background };
        var monitors = HudHost.ReadMonitors();
        var monitor = HudGeometry.MonitorFor("primary", monitors);
        var others = layout.Widgets.Where(x => x.Enabled && HudGeometry.MonitorFor(x.Monitor, monitors) == monitor).Select(x => HudGeometry.Place(x, monitor)).ToList();
        var rect = HudGeometry.Resolve(HudGeometry.Place(w, monitor), others, monitor.Bounds);
        HudGeometry.Store(w, rect, monitor);
        layout.Widgets.Add(w);
        _selected = w.Id;
        Changed();
        Fill();
    }
}
