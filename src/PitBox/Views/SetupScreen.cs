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
    private FrameworkElement? _brand, _title, _left, _right;
    private ResourceDictionary? _text;
    private StartupState? _startup;
    private bool _wide = true;
    private ContentControl? _goals, _tools;
    private readonly HashSet<string> _picked = new(StringComparer.Ordinal);

    private const double TwoColumnsAt = 875, ContentWidth = 1440, HeaderGap = 16;

    private static int LookColumns => Math.Clamp(Themes.All.Count, 2, 5);

    private static double LookHeight => LookColumns > 4 ? 56 : 46;

    private const double MaxLookHeight = 56, MaxLookWidth = 5 * 92;

    private static double LookWidth => LookColumns * (LookColumns switch { > 4 => 92, > 3 => 108, _ => 134 });

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
        if (_title is null || !_columns.Children.Contains(_title)) return;
        using var scope = Ui.Scope(_text);
        int row = Grid.GetRow(_title), column = Grid.GetColumn(_title);
        _columns.Children.Remove(_title);
        _title = Title();
        Grid.SetRow(_title, row);
        Grid.SetColumn(_title, column);
        _columns.Children.Add(_title);
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
        _columns = new Grid { MaxWidth = ContentWidth, Margin = new Thickness(39, 28, 39, 28), VerticalAlignment = VerticalAlignment.Center };
        _columns.Children.Add(_brand);
        _columns.Children.Add(_title);
        _columns.Children.Add(_left);
        _columns.Children.Add(_right);
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
        if (_brand is null || _title is null || _left is null || _right is null) return;
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
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_brand, 0, 0);
            Place(_title, 0, 2);
            Place(_left, 1, 0);
            Place(_right, 1, 2);
        }
        else
        {
            var stacked = new[] { _brand, _title, _left, _right };
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
        var lockup = new Border { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 6, 16, 10), Child = word };

        string label = _firstRun ? "Get started" : "Done";
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var arrow = Compact(new TextBlock { Text = "\u2192", FontSize = 17, Foreground = Ui.Res<Brush>("OnAccent"), Margin = new Thickness(8, 0, 0, 0) });
        strip.Children.Add(Compact(new TextBlock { Text = label, FontSize = 16, Foreground = Ui.Res<Brush>("OnAccent") }));
        strip.Children.Add(arrow);
        var bar = new Border { Padding = new Thickness(16, 6, 16, 6), Child = strip, Background = Ui.Res<Brush>("Accent") };

        var face = new Border { BorderThickness = Hairline.Edges(), Child = bar };
        var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        var finish = new Button
        {
            Template = template, Content = face, IsDefault = true, Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        System.Windows.Automation.AutomationProperties.SetName(finish, label);
        void Paint()
        {
            bool hot = finish.IsMouseOver || finish.IsKeyboardFocused;
            face.BorderBrush = hot ? Ui.Res<Brush>("Accent") : Brushes.Transparent;
            bar.Background = Ui.Res<Brush>(finish.IsPressed ? "AccentPressed" : hot ? "AccentHover" : "Accent");
        }
        Paint();
        finish.MouseEnter += (_, _) => Paint();
        finish.MouseLeave += (_, _) => Paint();
        finish.GotKeyboardFocus += (_, _) => Paint();
        finish.LostKeyboardFocus += (_, _) => Paint();
        finish.PreviewMouseLeftButtonDown += (_, _) => Dispatcher.BeginInvoke(new Action(Paint));
        finish.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(new Action(Paint));
        finish.Click += (_, _) => _done();
        return new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, HeaderGap), Children = { lockup, finish } };
    }

    private FrameworkElement Title()
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, HeaderGap) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MaxLookWidth + 16) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        stack.Children.Add(new TextBlock { Text = _firstRun ? "Welcome" : "Setup", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = "Set up your workspace", Style = Ui.Res<Style>("H1"), FontSize = 28, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(stack);
        var look = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0) };
        look.Children.Add(new TextBlock { Text = "Look", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 0, 0, 6) });
        var picker = new ThemePicker(_app.Config.Ui.Theme, PickTheme, columns: LookColumns, height: LookHeight, oled: set => { _app.UseOledColors(set); PickTheme(AppTheme.Custom); },
                usePick: (set, name) => { _app.UsePick(set, name); PickTheme(set.Id); })
            { Margin = new Thickness(0, 0, -8, -8), Width = LookWidth, VerticalAlignment = VerticalAlignment.Bottom };
        look.Children.Add(new Grid { Height = MaxLookHeight, Children = { picker } });
        Grid.SetColumn(look, 1);
        row.Children.Add(look);
        return row;
    }

    private FrameworkElement Left()
    {
        var stack = new DockPanel();
        var start = Section("Start", "Everything here can be changed later in Settings.", StartOptions(), inline: true);
        DockPanel.SetDock(start, Dock.Top);
        stack.Children.Add(start);
        _goals = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var goals = Section("What do you want Pit-Box for?", "Pick one or more.", _goals);
        goals.Margin = new Thickness(0);
        stack.Children.Add(goals);
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
        var heading = Compact(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), FontSize = 19, VerticalAlignment = VerticalAlignment.Center });
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
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 13, 16, 13), Margin = new Thickness(0, 0, 0, 13), Child = stack };
    }

    private FrameworkElement Right()
    {
        _tools = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var tools = Section("Tools", "A tool that's off has no page, keybind or background work.", _tools, ToolChooser.BulkButtons(_app, ByHand));
        tools.Margin = new Thickness(0);
        Fill();
        return tools;
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
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -7, -7) };
        foreach (var goal in SetupGoals.All)
            grid.Children.Add(GoalCard(goal.Id, goal.Title, goal.Caption));
        return grid;
    }

    private Button GoalCard(string id, string title, string caption)
    {
        bool on = _picked.Contains(id);
        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        face.Children.Add(TextFit.TipWhenTrimmed(Compact(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center })));
        face.Children.Add(new Border { Width = 30, Height = 3, Margin = new Thickness(0, 4, 0, 5), HorizontalAlignment = HorizontalAlignment.Center, Background = Ui.Res<Brush>(GoalColors.GetValueOrDefault(id, "LineStrong")) });
        face.Children.Add(new TextBlock { Text = caption, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var button = new Button
        {
            Style = Ui.Res<Style>("Btn"), Height = double.NaN, Margin = new Thickness(0, 0, 7, 7), Padding = new Thickness(12, 8, 12, 8),
            VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Ui.Res<Brush>(on ? "AccentSoft" : "SurfaceAlt"), BorderBrush = Ui.Res<Brush>("Line"),
            BorderThickness = Hairline.Edges(), Content = face, Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.MouseEnter += (_, _) => button.BorderBrush = Ui.Res<Brush>("LineStrong");
        button.MouseLeave += (_, _) => button.BorderBrush = Ui.Res<Brush>("Line");
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        System.Windows.Automation.AutomationProperties.SetItemStatus(button, on ? "Selected" : "Not selected");
        button.Click += (_, _) => Pick(id);
        return button;
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

    private static readonly Dictionary<string, string> GoalColors = new()
    {
        ["overlays"] = "Coral", ["racing"] = "Slate", ["pc"] = "Sage", ["everything"] = "Apricot",
    };

    private void ByHand()
    {
        _picked.Clear();
        Refill();
    }

    private FrameworkElement StartOptions()
    {
        var u = _app.Config.Ui;
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -6, 0) };
        Add(ToolChooser.Choice("Auto start", _app.Demo ? "Not in demo mode." : "Starts at sign-in.",
            _startup?.Enabled == true, !_app.Demo && _startup is not null, "Accent", on => _ = SetStartupAsync(on)));
        Add(ToolChooser.Choice("Start hidden", "Opens in the tray.", u.StartMinimized, true, "Accent", on => { u.StartMinimized = on; _app.Save(); }));
        Add(ToolChooser.Choice("Close to tray", "X hides to the tray.", u.CloseToTray, true, "Accent", on => { u.CloseToTray = on; _app.Save(); }));
        return row;

        void Add(Border option)
        {
            option.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(option);
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
