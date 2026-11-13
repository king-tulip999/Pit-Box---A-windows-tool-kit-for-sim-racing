using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Controls;
using Core.Grr;
using Core.Services;
using Core.Theme;

namespace Core.Views;

internal sealed class SetupScreen : UserControl
{
    private readonly AppServices _app;
    private readonly bool _firstRun;
    private readonly Action _done;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private ResourceDictionary? _text;
    private StartupState? _startup;

    private const double ColumnWidth = 600, HeaderGap = 16, WordmarkHeight = 62, MarkGap = 14;

    public SetupScreen(AppServices app, bool firstRun, Action done)
    {
        _app = app;
        _firstRun = firstRun;
        FontSize = 13;
        _done = done;
        Content = new Grid { Children = { new TopographyBackdrop(), _scroll } };
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
        var column = new StackPanel { MaxWidth = AppIdentity.IsTecnica ? ColumnWidth : ColumnWidth * 2 + Ui.PanelSpacing, Margin = new Thickness(39, 28, 39, 28), VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(Brand());
        column.Children.Add(Sections());
        column.Children.Add(Finish());
        _scroll.Content = column;
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

    private FrameworkElement Brand()
    {
        var word = new WordmarkText();
        Ui.FitWordmark(word, WordmarkHeight);
        var mark = new BrandMark { Width = WordmarkHeight, Height = WordmarkHeight, Margin = new Thickness(0, 0, MarkGap, 0), VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(BrandMark.ForegroundProperty, "LogoFg");
        mark.SetResourceReference(BrandMark.AccentProperty, "LogoAccent");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, word } };
        var logo = new Border { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 6, 16, 12), Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = row } };
        return new StackPanel { Margin = new Thickness(0, 0, 0, HeaderGap), Children = { logo, new DividerLine() } };
    }

    private FrameworkElement Finish()
    {
        string label = _firstRun ? "Get started" : "Done";
        var finish = new Button
        {
            Style = Ui.Res<Style>("PrimaryButton"), Content = Compact(new TextBlock { Text = label, FontSize = 15.5 }), IsDefault = true, MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, Ui.PanelSpacing, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(finish, label);
        finish.Click += (_, _) => _done();
        return finish;
    }

    private FrameworkElement Sections()
    {
        var start = Section("Start", "Everything here can be changed later in Settings.", StartOptions(), inline: true);
        if (AppIdentity.IsTecnica)
        {
            var hardware = Section("Hardware", null, HardwareOptions());
            hardware.Margin = new Thickness(0);
            return new StackPanel { Children = { start, hardware } };
        }
        start.Margin = new Thickness(0);
        var right = new DockPanel { LastChildFill = true };
        right.Children.Add(Section("iRacing", null, IracingOptions()));
        foreach (UIElement card in right.Children) DockPanel.SetDock(card, Dock.Top);
        ((FrameworkElement)right.Children[^1]).Margin = new Thickness(0);
        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        start.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(right, 2);
        cards.Children.Add(start);
        cards.Children.Add(right);
        return cards;
    }

    private FrameworkElement OpenOn()
    {
        var u = _app.Config.Ui;
        var pages = _app.NavPages().Prepend((Key: Config.UiSettings.LastUsedPage, Title: "Last page used")).ToList();
        var pick = new ComboBox { MinWidth = 180, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, title) in pages) pick.Items.Add(new ComboBoxItem { Content = title, Tag = key });
        pick.SelectedIndex = Math.Max(0, pages.FindIndex(p => p.Key == u.StartPage));
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedItem is ComboBoxItem { Tag: string key }) { u.StartPage = key; _app.Save(); }
        };
        var label = Compact(new TextBlock { Text = "Open on", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(pick, Dock.Right);
        return new DockPanel { Margin = new Thickness(2, 10, 0, 0), Children = { pick, label } };
    }

    private FrameworkElement IracingOptions()
    {
        var rows = new StackPanel();
        var grr = _app.Config.Grr;
        Add(rows, "GRR League page", "Your league's next race, standings, results and schedule.", grr.ShowPage, true, on =>
        {
            grr.ShowPage = on;
            _app.Save();
            _app.Plugins.RaiseChanged();
        });
        var sim = _app.Config.SimRacing;
        var plugin = _app.Plugins.Find(Plugins.PluginIds.SimRacing) as Plugins.BuiltIn.SimRacingPlugin;
        Add(rows, "Trading Paints with iRacing", "Starts the Trading Paints Downloader when iRacing starts.", sim.TradingPaints.LaunchWithIracing, true,
            on => { sim.TradingPaints.LaunchWithIracing = on; _app.Save(); plugin?.TradingPaints?.UpdateWatch(); });
        Add(rows, "Garage 61 with iRacing", "Starts the Garage 61 Telemetry Agent when iRacing starts.", sim.Garage61.LaunchWithIracing, true,
            on => { sim.Garage61.LaunchWithIracing = on; _app.Save(); plugin?.Garage61?.UpdateWatch(); });
        var hud = sim.Hud;
        Add(rows, "Browser sources", "Overlay widgets as pages on this PC, for streaming software or a wheel screen.", hud.WebOverlays, true,
            on => { hud.WebOverlays = on; _app.Save(); plugin?.Hud?.SyncWeb(); });
        return rows;
    }

    private FrameworkElement HardwareOptions()
    {
        var rows = new StackPanel();
        var state = _app.Hardware.PawnIoState;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Compact(new TextBlock { Text = "Hardware driver", FontSize = 14 }));
        text.Children.Add(new TextBlock
        {
            Text = state == Hardware.PawnIoState.Installed ? "PawnIO is installed." : "PawnIO is needed for fan control and some temperatures.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap,
        });
        var row = new DockPanel { Margin = new Thickness(2, 0, 0, 0) };
        if (state == Hardware.PawnIoState.Missing)
        {
            var install = new Button { Content = "Install PawnIO", Style = Ui.Res<Style>("GhostButton"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            install.Click += (_, _) => Core.Views.Pages.SettingsPage.InstallPawnIo(_app);
            DockPanel.SetDock(install, Dock.Right);
            row.Children.Add(install);
        }
        row.Children.Add(text);
        rows.Children.Add(row);
        return rows;
    }

    private static void Add(StackPanel rows, string title, string caption, bool on, bool enabled, Action<bool> changed)
    {
        var tile = ToggleCards.Choice(title, caption, on, enabled, changed);
        tile.Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 6, 0, 0);
        rows.Children.Add(tile);
    }

    private static TextBlock Compact(TextBlock text) => ToggleCards.Strong(text);

    private static FrameworkElement Section(string title, string? text, UIElement body, bool inline = false)
    {
        var stack = new DockPanel();
        var head = new DockPanel { LastChildFill = inline, Margin = new Thickness(0, 0, 0, text is null || inline ? 10 : 3) };
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

    private FrameworkElement StartOptions()
    {
        var u = _app.Config.Ui;
        var rows = new StackPanel();
        Option("Start with Windows", _app.Demo ? "Not in demo mode." : $"{AppIdentity.Name} opens when you sign in.", _startup?.Enabled == true, !_app.Demo && _startup is not null, on => _ = SetStartupAsync(on));
        Option("Start in background", "Opens in the tray.", u.StartMinimized, true, on => { u.StartMinimized = on; _app.Save(); });
        Option("Close in background", $"The close button hides {AppIdentity.Name} to the tray.", u.CloseToTray, true, on => { u.CloseToTray = on; _app.Save(); });
        rows.Children.Add(OpenOn());
        return rows;

        void Option(string title, string caption, bool on, bool enabled, Action<bool> changed) => Add(rows, title, caption, on, enabled, changed);
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
