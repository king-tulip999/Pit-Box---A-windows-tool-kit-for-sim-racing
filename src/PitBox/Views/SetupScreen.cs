using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Plugins.BuiltIn;
using PitBox.Services;
using PitBox.Theme;
using PitBox.Views.Pages;

namespace PitBox.Views;

internal sealed class SetupScreen : UserControl
{
    private readonly AppServices _app;
    private readonly bool _firstRun;
    private readonly Action _done;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private Grid _columns = new();
    private FrameworkElement? _brand, _title, _left, _right, _finish;
    private ResourceDictionary? _text;
    private StartupState? _startup;
    private bool _wide = true;
    private ContentControl? _goals, _tools;
    private readonly HashSet<string> _picked = new(StringComparer.Ordinal);

    private const double TwoColumnsAt = 875, ContentWidth = 1440, HeaderGap = 16, LookHeight = 46;

    private static int LookColumns => ThemePicker.Balanced(most: 4);

    public SetupScreen(AppServices app, bool firstRun, Action done)
    {
        _app = app;
        _firstRun = firstRun;
        FontSize = 13;
        _done = done;
        Content = new Grid { Children = { new TopographyBackdrop(), _scroll } };
        SizeChanged += (_, e) => { if (e.WidthChanged) Arrange(); };
        Loaded += (_, _) => { Unsubscribe(); ThemeManager.Changed += Build; _app.Plugins.Changed += RefreshLook; };
        Unloaded += (_, _) => Unsubscribe();
        Build();
        _ = LoadStartupAsync();
        if (firstRun) Loaded += FitWindowOnce;
    }

    private void FitWindowOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= FitWindowOnce;
        Dispatcher.BeginInvoke(() =>
        {
            if (Window.GetWindow(this) is not { WindowState: WindowState.Normal } window || ScreenshotRunner.Active) return;
            double extra = _scroll.ExtentHeight - _scroll.ViewportHeight;
            if (extra <= 1) return;
            var area = WorkAreaOf(window);
            double height = Math.Min(area.Height, window.Height + extra + 8);
            window.Height = height;
            window.Top = area.Top + (area.Height - height) / 2;
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static Rect WorkAreaOf(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || PresentationSource.FromVisual(window)?.CompositionTarget is not { } target) return SystemParameters.WorkArea;
        var px = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var m = target.TransformFromDevice;
        var topLeft = m.Transform(new Point(px.Left, px.Top));
        var bottomRight = m.Transform(new Point(px.Right, px.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void Unsubscribe()
    {
        ThemeManager.Changed -= Build;
        _app.Plugins.Changed -= RefreshLook;
    }

    private void RefreshLook() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_right is not Grid right || right.Children.Count < 2) return;
        using var scope = Ui.Scope(_text);
        right.Children.RemoveAt(1);
        var look = Look();
        Grid.SetRow(look, 1);
        right.Children.Add(look);
    }));

    private void Build()
    {
        _text = LargerText();
        using var scope = Ui.Scope(_text);
        Background = Ui.Res<Brush>("WindowBg");
        _brand = Brand();
        _title = Title();
        _left = Left();
        _right = Right();
        _finish = Finish();
        _columns = new Grid { MaxWidth = ContentWidth, Margin = new Thickness(39, 28, 39, 28), VerticalAlignment = VerticalAlignment.Center };
        _columns.Children.Add(_brand);
        _columns.Children.Add(_title);
        _columns.Children.Add(_left);
        _columns.Children.Add(_right);
        _columns.Children.Add(_finish);
        _scroll.Content = _columns;
        Arrange(force: true);
    }

    private static ResourceDictionary LargerText()
    {
        var styles = new ResourceDictionary();
        foreach (var (key, size) in new[] { ("Body", 14.0), ("Muted", 14.0), ("Caption", 13.0), ("H3", 14.5), ("Eyebrow", 13.0) })
        {
            var style = new Style(typeof(TextBlock), (Style)Application.Current.FindResource(key));
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, size));
            styles[key] = style;
        }
        return styles;
    }

    private void Arrange(bool force = false)
    {
        if (_brand is null || _title is null || _left is null || _right is null || _finish is null) return;
        bool wide = ActualWidth <= 0 || ActualWidth >= TwoColumnsAt;
        if (!force && wide == _wide) return;
        _wide = wide;
        _columns.ColumnDefinitions.Clear();
        _columns.RowDefinitions.Clear();
        if (wide)
        {
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3.4, GridUnitType.Star), MinWidth = 292, MaxWidth = 430 });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
            for (int i = 0; i < 3; i++)
                _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_brand, 0, 0);
            Place(_title, 0, 2);
            Place(_left, 1, 0);
            Place(_right, 1, 2);
            Place(_finish, 2, 2);
        }
        else
        {
            var stacked = new[] { _brand, _title, _left, _right, _finish };
            for (int i = 0; i < stacked.Length; i++)
            {
                _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Place(stacked[i], i, 0);
            }
        }

        static void Place(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
        }
    }

    private FrameworkElement Brand()
    {
        var word = new TextBlock { Foreground = Ui.Res<Brush>("Heading") };
        Ui.FitWordmark(word, 62);
        return new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 6, 16, HeaderGap), Child = word };
    }

    private FrameworkElement Finish()
    {
        string label = _firstRun ? "Get started" : "Done";
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        strip.Children.Add(Compact(new TextBlock { Text = label, FontSize = 15.5 }));
        strip.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.ArrowRight"), Width = 15, Height = 15, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var finish = new Button
        {
            Style = Ui.Res<Style>("PrimaryButton"), Content = strip, IsDefault = true, MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, Ui.PanelSpacing, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(finish, label);
        finish.Click += (_, _) => _done();
        return finish;
    }

    private FrameworkElement Title()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, HeaderGap) };
        stack.Children.Add(new TextBlock { Text = _firstRun ? "Welcome" : "Setup", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 0, 0, 4) });
        var heading = new TextBlock { Text = "Set up your workspace", Style = Ui.Res<Style>("H1"), FontSize = 34, TextWrapping = TextWrapping.Wrap, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        heading.LineHeight = Math.Round(heading.FontSize * 1.1);
        stack.Children.Add(heading);
        return stack;
    }

    private FrameworkElement Look()
    {
        var picker = new ThemePicker(_app.Config.Ui.Theme, PickTheme, columns: LookColumns, height: LookHeight, oled: set => { _app.UseOledColors(set); PickTheme(AppTheme.Custom); },
                usePick: (set, name) => { _app.UsePick(set, name); PickTheme(set.Id); })
            { Margin = new Thickness(0, 0, -8, -8) };
        var look = Section("Look", null, picker);
        look.Margin = new Thickness(0);
        return look;
    }

    private FrameworkElement Left()
    {
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition());
        _goals = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        stack.Children.Add(Section("What do you want Pit-Box for?", null, _goals));
        var start = Section("Start", "Everything here can be changed later in Settings.", StartOptions(), inline: true);
        start.Margin = new Thickness(0);
        Grid.SetRow(start, 1);
        stack.Children.Add(start);
        return stack;
    }

    private void PickTheme(AppTheme theme)
    {
        if (_firstRun)
        {
            foreach (var layout in _app.Config.Overlay.Layouts)
                layout.Colors = OverlayColorPreset.App;
        }
        _app.SetTheme(theme);
    }

    private static TextBlock Compact(TextBlock text) => ToolChooser.Compact(text);

    private static FrameworkElement Section(string title, string? text, UIElement body, UIElement? action = null, bool inline = false)
    {
        var stack = new DockPanel();
        inline |= action is not null;
        var head = new DockPanel { LastChildFill = inline, Margin = new Thickness(0, 0, 0, text is null || inline ? 10 : 3) };
        if (action is not null)
        {
            DockPanel.SetDock(action, Dock.Right);
            head.Children.Add(action);
        }
        var heading = Compact(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(heading, Dock.Left);
        head.Children.Add(heading);
        if (inline && text is not null)
            head.Children.Add(TextFit.TipWhenTrimmed(new TextBlock
            {
                Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 2, 12, 0),
            }));
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);
        if (!inline && text is not null)
        {
            var caption = new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(caption, Dock.Top);
            stack.Children.Add(caption);
        }
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 13, 16, 13), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = stack };
    }

    private FrameworkElement Right()
    {
        _tools = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var tools = Section("Tools", "A tool that's off has no page, keybind or background work.", _tools, ToolChooser.BulkButtons(_app, ByHand));
        Fill();
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition());
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.Children.Add(tools);
        var look = Look();
        Grid.SetRow(look, 1);
        stack.Children.Add(look);
        return stack;
    }

    private void Fill()
    {
        using var scope = Ui.Scope(_text);
        if (_goals is not null) _goals.Content = Goals();
        if (_tools is not null) _tools.Content = ToolChooser.Build(_app, ByHand);
    }

    private void Refill() => Dispatcher.BeginInvoke(new Action(Fill));

    private FrameworkElement Goals()
    {
        var picked = SetupGoals.All.Where(g => _picked.Contains(g.Id)).Select(g => "“" + g.Title + "”").ToList();
        string state = picked.Count == 0 ? "Picking one ticks its tools; each tool can still be changed."
            : "Tools ticked for " + string.Join(" and ", picked) + ".";
        var dock = new DockPanel();
        var line = new TextBlock { Text = state, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(line, Dock.Top);
        dock.Children.Add(line);
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -7, -7) };
        foreach (var goal in SetupGoals.All)
            grid.Children.Add(GoalCard(goal.Id, goal.Title, goal.Caption));
        dock.Children.Add(grid);
        return dock;
    }

    private CheckBox GoalCard(string id, string title, string caption)
    {
        var card = ToggleCards.Dress(Ui.Switch(_picked.Contains(id), _ => Pick(id)), ToggleCards.Title(title, false), caption);
        card.Margin = new Thickness(0, 0, 7, 7);
        return card;
    }

    private void Pick(string id)
    {
        if (!_picked.Remove(id)) _picked.Add(id);
        if (_picked.Count > 0) ApplyGoals();
        Refill();
    }

    private void ApplyGoals()
    {
        var (plugins, tools) = SetupGoals.Resolve(SetupGoals.All.Where(g => _picked.Contains(g.Id)), _app.Plugins.All.Select(p => (p.Id, p.EnabledByDefault)));
        var sim = _app.Config.SimRacing;
        foreach (var tool in SimRacingPage.ToolsInOrder(_app))
            sim.SetPanelHidden(tool.Id, !tools.Contains(tool.Id));
        _app.Save();
        _app.Plugins.SetEnabled(p => plugins.Contains(p.Id));
        SimRacingPlugin.PanelsChanged(_app);
    }

    private void ByHand()
    {
        _picked.Clear();
        Refill();
    }

    private FrameworkElement StartOptions()
    {
        var u = _app.Config.Ui;
        var rows = new StackPanel();
        Option("Start with Windows", _app.Demo ? "Not in demo mode." : "Pit-Box opens when you sign in.", _startup?.Enabled == true, !_app.Demo && _startup is not null, on => _ = SetStartupAsync(on));
        Option("Start in background", "Opens in the tray.", u.StartMinimized, true, on => { u.StartMinimized = on; _app.Save(); });
        Option("Close in background", "The close button hides Pit-Box to the tray.", u.CloseToTray, true, on => { u.CloseToTray = on; _app.Save(); });
        return rows;

        void Option(string title, string caption, bool on, bool enabled, Action<bool> changed)
        {
            var tile = ToolChooser.Choice(title, caption, on, enabled, changed);
            tile.Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 6, 0, 0);
            rows.Children.Add(tile);
        }
    }

    private async Task LoadStartupAsync()
    {
        if (_app.Demo) return;
        try { _startup = await Task.Run(StartupManager.State); }
        catch { _startup = null; }
        Build();
    }

    private async Task SetStartupAsync(bool on)
    {
        if (_app.Demo) return;
        try
        {
            await Task.Run(() => StartupManager.SetEnabled(on));
            _startup = await Task.Run(StartupManager.State);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't change start with Windows", ex.Message, NoticeKind.Warning);
        }
        Build();
    }
}
