using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class SetupScreen : UserControl
{
    private readonly AppServices _app;
    private readonly bool _firstRun;
    private readonly Action _done;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Border _footer = Hairline.Frame(new Border(), Sides.Top);
    private Grid _columns = new();
    private FrameworkElement? _left, _right;
    private StartupState? _startup;
    private bool _wide = true;

    private const double TwoColumnsAt = 875;

    public SetupScreen(AppServices app, bool firstRun, Action done)
    {
        _app = app;
        _firstRun = firstRun;
        _done = done;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_scroll);
        Grid.SetRow(_footer, 1);
        root.Children.Add(_footer);
        Content = root;
        SizeChanged += (_, e) => { if (e.WidthChanged) Arrange(); };
        Loaded += (_, _) => { Unsubscribe(); ThemeManager.Changed += Build; };
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

    private void Unsubscribe() => ThemeManager.Changed -= Build;

    private void Build()
    {
        Background = Ui.Res<Brush>("WindowBg");
        _left = Left();
        _right = Right();
        _columns = new Grid { MaxWidth = 1037, Margin = new Thickness(39, 36, 39, 13) };
        _columns.Children.Add(_left);
        _columns.Children.Add(_right);
        _scroll.Content = _columns;
        _footer.SetResourceReference(Border.BorderBrushProperty, "Line");
        _footer.SetResourceReference(Border.BackgroundProperty, "WindowBg");
        var finish = new Button
        {
            Content = _firstRun ? "Get started" : "Done", Style = Ui.Res<Style>("PrimaryButton"), FontSize = 12, Padding = new Thickness(23, 7, 23, 7), Height = double.NaN,
            IsDefault = true, HorizontalAlignment = HorizontalAlignment.Left,
        };
        finish.Click += (_, _) => _done();
        _footer.Child = new Border { MaxWidth = 1037, Margin = new Thickness(39, 11, 39, 13), Child = finish };
        Arrange(force: true);
    }

    private void Arrange(bool force = false)
    {
        if (_left is null || _right is null) return;
        bool wide = ActualWidth <= 0 || ActualWidth >= TwoColumnsAt;
        if (!force && wide == _wide) return;
        _wide = wide;
        _columns.ColumnDefinitions.Clear();
        _columns.RowDefinitions.Clear();
        if (wide)
        {
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 292 });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
            Grid.SetColumn(_right, 2);
            Grid.SetRow(_right, 0);
        }
        else
        {
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_right, 0);
            Grid.SetRow(_right, 1);
        }
    }

    private FrameworkElement Left()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 19) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = new Icon { Data = Ui.Res<Geometry>("I.Bag"), StrokeThickness = 1.7, Foreground = Ui.Res<Brush>("LogoFg") };
        var tile = new Tile
        {
            Background = Ui.Res<Brush>("LogoBg"), BorderBrush = Ui.Res<Brush>("LogoEdge"),
            BorderThickness = ThemeManager.Current.LogoEdge, Child = glyph,
        };
        var word = new TextBlock { Foreground = Ui.Res<Brush>("Heading") };
        Ui.FitLockup(tile, glyph, word, maxHeight: 49, width: 324, gap: 15);
        brand.Children.Add(tile);
        brand.Children.Add(word);
        stack.Children.Add(brand);

        stack.Children.Add(new TextBlock { Text = _firstRun ? "Welcome" : "Setup", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 24, 0, 5) });
        stack.Children.Add(new TextBlock { Text = "Set up your workspace", Style = Ui.Res<Style>("H1"), FontSize = 26, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock
        {
            Text = "Everything here can be changed later in Settings.",
            Style = Ui.Res<Style>("Muted"), FontSize = 12, Margin = new Thickness(0, 6, 0, 18), MaxWidth = 373, HorizontalAlignment = HorizontalAlignment.Left,
        });

        stack.Children.Add(Section("Look", "I.Palette", null,
            new ThemePicker(_app.Config.Ui.Theme, PickTheme, columns: 3, height: 52, oled: set => { _app.UseOledColors(set); PickTheme(AppTheme.Custom); }) { Margin = new Thickness(0, 2, -8, -8) }));
        stack.Children.Add(Section("Start", "I.Power", null, StartOptions()));
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

    private static FrameworkElement Section(string title, string icon, string? text, UIElement body)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, text is null ? 8 : 3) };
        head.Children.Add(new Tile { Style = Ui.Res<Style>("IconBadge"), Width = 24, Height = 24, Child = new Icon { Data = Ui.Res<Geometry>(icon), Width = 13, Height = 13 } });
        head.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), FontSize = 15.5, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        if (text is not null)
            stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(33, 0, 0, 10) });
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 15, 16, 15), Margin = new Thickness(0, 0, 0, 13), Child = stack };
    }

    private FrameworkElement Right()
    {
        var stack = new StackPanel();
        stack.Children.Add(Section("Tools", "I.Plug", "A tool that's off has no page, keybind or background work.", Plugins()));
        return stack;
    }

    private FrameworkElement Plugins()
    {
        var panel = new StackPanel();
        var list = new StackPanel();
        var switches = new List<CheckBox>();
        foreach (var plugin in PluginManager.InNavOrder(_app.Plugins.All, _app.Config.Ui.NavOrder))
        {
            if (plugin.Id == PluginIds.Overlay) continue;
            var (soft, deep) = plugin.Colors;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = Ui.Badge(plugin.Icon, soft, deep, 24, 6.5, 13);
            badge.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(badge);
            var text = new StackPanel { Margin = new Thickness(9, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = plugin.Name, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis }));
            text.Children.Add(new TextBlock { Text = plugin.Description, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var toggle = Ui.Switch(_app.Plugins.IsEnabled(plugin), on => _app.Plugins.SetEnabled(plugin, on));
            toggle.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(toggle, plugin.Name);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
            switches.Add(toggle);
            list.Children.Add(new Border { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 0, 6), Child = row });
        }
        panel.Children.Add(list);

        var bulk = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        void All(bool on)
        {
            _app.Plugins.SetEnabled(_ => on);
            foreach (var toggle in switches)
                toggle.IsChecked = on;
        }
        var allOn = new Button { Content = "Turn all on", Style = Ui.Res<Style>("SoftButton"), Margin = new Thickness(0, 0, 6, 0) };
        allOn.Click += (_, _) => All(true);
        var allOff = new Button { Content = "Turn all off", Style = Ui.Res<Style>("GhostButton") };
        allOff.Click += (_, _) => All(false);
        bulk.Children.Add(allOn);
        bulk.Children.Add(allOff);
        panel.Children.Add(bulk);
        return panel;
    }

    private FrameworkElement StartOptions()
    {
        var u = _app.Config.Ui;
        var panel = new StackPanel();
        var start = Ui.Switch(_startup?.Enabled == true, on => _ = SetStartupAsync(on));
        start.IsEnabled = !_app.Demo && _startup is not null;
        panel.Children.Add(Row(Ui.SettingRow("Start with Windows",
            _app.Demo ? "Not available in demo mode." : "Starts when you sign in, so window profiles, keybinds and fans are ready. With Start in background on, it starts in the tray.", start)));
        panel.Children.Add(Ui.Divider());
        panel.Children.Add(Row(Ui.SettingRow("Start in background", "Opens in the tray instead of this window whenever Goodie Bag starts. Click the bag icon in the tray to open it.",
            Ui.Switch(u.StartMinimized, v => { u.StartMinimized = v; _app.Save(); }))));
        panel.Children.Add(Ui.Divider());
        panel.Children.Add(Row(Ui.SettingRow("Close in background", "The X button keeps Goodie Bag running in the tray (keybinds, fans, overlay). When off, X quits Goodie Bag.",
            Ui.Switch(u.CloseToTray, v => { u.CloseToTray = v; _app.Save(); }))));
        return panel;

        static Grid Row(Grid row)
        {
            row.Margin = new Thickness(0, 6, 0, 6);
            return row;
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
