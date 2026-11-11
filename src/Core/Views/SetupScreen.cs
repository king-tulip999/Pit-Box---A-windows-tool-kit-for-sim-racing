using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Config;
using Core.Controls;
using Core.Grr;
using Core.Services;
using Core.Theme;
using Core.Views.Pages;

namespace Core.Views;

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

    private const double TwoColumnsAt = 875, ContentWidth = 1440, OneColumnWidth = 600, HeaderGap = 16, WordmarkHeight = 62, MarkGap = 14, ThemeTileHeight = 46, ThreeChipRowsHeight = 181;

    private double _colorsHeight = ThreeChipRowsHeight;

    public SetupScreen(AppServices app, bool firstRun, Action done)
    {
        _app = app;
        _firstRun = firstRun;
        FontSize = 13;
        _done = done;
        Content = new Grid { Children = { new TopographyBackdrop(), _scroll } };
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

    private void Unsubscribe()
    {
        ThemeManager.Changed -= Build;
    }

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
        _columns = new Grid { MaxWidth = AppIdentity.IsTecnica ? OneColumnWidth : ContentWidth, Margin = new Thickness(39, 28, 39, 28), VerticalAlignment = VerticalAlignment.Center };
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
        bool wide = !AppIdentity.IsTecnica && (ActualWidth <= 0 || ActualWidth >= TwoColumnsAt);
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
        var word = new WordmarkText();
        Ui.FitWordmark(word, WordmarkHeight);
        var mark = new BrandMark { Width = WordmarkHeight, Height = WordmarkHeight, Margin = new Thickness(0, 0, MarkGap, 0), VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(BrandMark.ForegroundProperty, "LogoFg");
        mark.SetResourceReference(BrandMark.AccentProperty, "LogoAccent");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, word } };
        return new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 6, 16, HeaderGap), Child = row };
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
        var picker = new ThemePicker(_app.Config.Ui.Theme, PickTheme, columns: 4, height: ThemeTileHeight) { Margin = new Thickness(0, 0, -8, 3) };
        var colors = new CustomThemeSection(_app);
        var colorsHost = new Border { MinHeight = _colorsHeight, Child = colors };
        colors.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height <= _colorsHeight) return;
            _colorsHeight = e.NewSize.Height;
            colorsHost.MinHeight = _colorsHeight;
        };
        var look = Section("Look", null, new StackPanel { Children = { picker, colorsHost } });
        look.Margin = new Thickness(0);
        return look;
    }

    private FrameworkElement Left()
    {
        var start = Section("Start", "Everything here can be changed later in Settings.", StartOptions(), inline: true);
        if (AppIdentity.IsTecnica)
        {
            start.Margin = new Thickness(0);
            return start;
        }
        var pages = Section("Pages", null, Pages());
        pages.Margin = new Thickness(0);
        return new StackPanel { Children = { start, pages } };
    }

    private FrameworkElement Pages()
    {
        var grr = _app.Config.Grr;
        return ToggleCards.Choice("GRR League", "Your league's next race, standings, results and schedule.", grr.ShowPage, true, on =>
        {
            grr.ShowPage = on;
            _app.Save();
            _app.Plugins.RaiseChanged();
        });
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

    private static TextBlock Compact(TextBlock text) => ToggleCards.Strong(text);

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

    private FrameworkElement Right() => AppIdentity.IsTecnica ? new Border() : Look();

    private FrameworkElement StartOptions()
    {
        var u = _app.Config.Ui;
        var rows = new StackPanel();
        Option("Start with Windows", _app.Demo ? "Not in demo mode." : $"{AppIdentity.Name} opens when you sign in.", _startup?.Enabled == true, !_app.Demo && _startup is not null, on => _ = SetStartupAsync(on));
        Option("Start in background", "Opens in the tray.", u.StartMinimized, true, on => { u.StartMinimized = on; _app.Save(); });
        Option("Close in background", $"The close button hides {AppIdentity.Name} to the tray.", u.CloseToTray, true, on => { u.CloseToTray = on; _app.Save(); });
        return rows;

        void Option(string title, string caption, bool on, bool enabled, Action<bool> changed)
        {
            var tile = ToggleCards.Choice(title, caption, on, enabled, changed);
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
