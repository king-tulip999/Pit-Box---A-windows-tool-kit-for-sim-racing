using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Config;
using Core.Services;

namespace Core.Views.Pages;

internal sealed class SimRacingPage : UserControl, IPage, IScreenshotStates, IRevealAnchor, ISectionedPage, IPageHistory
{
    internal static readonly string[] DefaultPanelOrder = { "setups", "shakers", "iracing", "fov", "companions" };

    private const string PageKey = "simracing";

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(23, 0, 23, 23) };
    private FovSection? _fov;
    private IracingControllerSection? _controller;
    private CompanionAppSection? _tradingPaints, _garage61;
    private readonly System.Windows.Controls.Primitives.UniformGrid _companions = new() { Columns = 1, VerticalAlignment = VerticalAlignment.Top };
    private SetupManagerSection? _setups;
    private ShakersSection? _shakers;
    private readonly ScrollViewer _scroll;
    private readonly SectionView _view;
    private bool _shown;
    private ContentFrame _frame = null!;
    private const double SetupsWidth = Ui.PageWidth;

    public event Action? SectionsChanged;

    public SimRacingPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        var scroll = _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _frame = Ui.PageFrame(_body), Focusable = false };
        Ui.ChainWheel(scroll);
        Content = scroll;
        _view = new SectionView(() => _scroll);

        var heading = _view.Heading;
        heading.Margin = Ui.HeaderMargin;
        _body.Children.Add(heading);
        _body.Children.Add(_view.Content);

        _view.Add(Def("fov", "The correct field of view for your seat and screens, so speed, distance and apexes look real.",
            () => _fov ??= new FovSection(app, window), "simracing.fov", () => _fov?.OnShown(), tools: () => _fov?.TitleTools));
        _view.Add(Def("iracing", "Every iRacing setting with a plain-language description, a health check and backups.",
            () => _controller = new IracingControllerSection(_app, _window), "simracing.iracing",
            () => _controller?.OnShown(), tools: () => _controller?.TitleTools));
        _view.Add(Def("setups", "Your car setups by car: find, rename, move, import and share them with league mates without File Explorer.",
            () => _setups = new SetupManagerSection(_app, _window), "simracing.setups",
            () => _setups?.OnShown(), () => _setups?.OnHidden(), () => _setups?.TitleTools));
        _view.Add(Def("shakers", "Tactile transducers under your seat or pedals, driven by iRacing's telemetry or game audio through a sound output.",
            () => _shakers = new ShakersSection(_app), "simracing.shakers",
            () => _shakers?.OnShown(), () => _shakers?.OnHidden(), () => _shakers?.TitleTools));
        _view.Add(Def("companions", "",
            BuildCompanions, "simracing.companions",
            ShowCompanions, HideCompanions));

        var visible = Visible();
        if (visible.Contains("iracing")) IracingControllerSection.Preload(app);
        if (visible.Contains("fov")) FovSection.Warm();
        Layout(animate: false);
    }

    private UIElement BuildCompanions()
    {
        _tradingPaints = new CompanionAppSection(_app, p => p.TradingPaints,
            "It downloads the custom paints of the cars around you so you see them in iRacing; it's free.");
        Ui.Anchor(_tradingPaints, "simracing.tradingpaints");
        _garage61 = new CompanionAppSection(_app, p => p.Garage61,
            "It records your iRacing laps and telemetry and uploads them to garage61.net, to compare with your team and other drivers.");
        _garage61.Margin = new Thickness(0, Ui.PanelSpacing, 0, 0);
        Ui.Anchor(_garage61, "simracing.garage61");
        FillCompanions();
        _companions.SizeChanged += (_, e) => { if ((e.PreviousSize.Width >= Ui.TwoCardsFrom) != (e.NewSize.Width >= Ui.TwoCardsFrom)) LayCompanions(); };
        return _companions;
    }

    private void FillCompanions()
    {
        _companions.Children.Clear();
        if (_tradingPaints is null || _garage61 is null) return;
        _companions.Children.Add(_tradingPaints);
        _companions.Children.Add(_garage61);
        LayCompanions();
    }

    private void LayCompanions()
    {
        if (_garage61 is null) return;
        bool both = _companions.Children.Count == 2, side = both && _companions.ActualWidth >= Ui.TwoCardsFrom;
        _companions.Columns = side ? 2 : 1;
        _garage61.Margin = !both ? new Thickness(0) : side ? new Thickness(Ui.PanelSpacing / 2, 0, 0, 0) : new Thickness(0, Ui.PanelSpacing, 0, 0);
        if (_tradingPaints is not null) _tradingPaints.Margin = side ? new Thickness(0, 0, Ui.PanelSpacing / 2, 0) : new Thickness(0);
    }

    private void ShowCompanions()
    {
        FillCompanions();
        _tradingPaints?.OnShown();
        _garage61?.OnShown();
    }

    private void HideCompanions()
    {
        _tradingPaints?.OnHidden();
        _garage61?.OnHidden();
    }

    private static SectionDef Def(string id, string subtitle, Func<UIElement> build, string anchor, Action? shown = null, Action? hidden = null, Func<FrameworkElement?>? tools = null)
    {
        var t = Tool(id);
        if (tools is null) return new SectionDef(id, t.Title, subtitle, t.Icon, build, anchor, shown, hidden);
        var slot = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        UIElement Build()
        {
            var built = build();
            slot.Content = tools();
            return built;
        }
        return new SectionDef(id, t.Title, subtitle, t.Icon, Build, anchor, shown, hidden, slot);
    }

    private SimRacingSettings S => _app.Config.SimRacing;

    private static readonly PageSection[] Tools =
    {
        new("setups", "iRacing Setup Manager", "I.Folder"),
        new("shakers", "Bass shakers", "I.Speaker"),
        new("fov", "FOV calculator", "I.Compass"),
        new("iracing", "iRacing Controller", "I.Keyboard"),
        new("companions", "Companion apps", "I.Rocket"),
    };

    private static List<string> OrderedFor(SimRacingSettings s)
    {
        int Slot(string id) { int i = s.PanelLayout.FindIndex(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
        return DefaultPanelOrder.OrderBy(Slot).ThenBy(id => Array.IndexOf(DefaultPanelOrder, id)).ToList();
    }

    private static List<string> VisibleFor(SimRacingSettings s) => OrderedFor(s);

    private static List<PageSection> SectionsOf(SimRacingSettings s) => VisibleFor(s).Select(id => Tools.First(t => t.Id == id)).ToList();

    internal static SectionList SectionsFor(AppServices app)
    {
        var visible = VisibleFor(app.Config.SimRacing);
        string? saved = app.Config.Ui.PageSections.GetValueOrDefault(PageKey);
        return new SectionList(SectionsOf(app.Config.SimRacing), visible.FirstOrDefault(v => string.Equals(v, saved, StringComparison.OrdinalIgnoreCase)) ?? visible.FirstOrDefault());
    }


    private static PageSection Tool(string id) => Tools.First(t => t.Id == id);

    private List<string> Ordered() => OrderedFor(S);

    private List<string> Visible() => VisibleFor(S);

    public IReadOnlyList<PageSection> Sections => SectionsOf(S);

    public string? CurrentSection => _view.Current;

    private string? Saved => _app.Config.Ui.PageSections.GetValueOrDefault(PageKey);

    private void Layout(bool animate)
    {
        var visible = Visible();
        string? want = _view.Current is { } current && visible.Contains(current) ? current
            : Saved is { } saved && visible.Contains(saved, StringComparer.OrdinalIgnoreCase) ? visible.First(v => string.Equals(v, saved, StringComparison.OrdinalIgnoreCase))
            : visible.FirstOrDefault();
        _view.Show(want, animate && _shown);
        FitFrame();
        SectionsChanged?.Invoke();
    }

    private void FitFrame() => _frame.DesignWidth = _view.Current == "setups" ? SetupsWidth : Ui.PageWidth;

    public void ShowSection(string id)
    {
        if (!Visible().Contains(id, StringComparer.OrdinalIgnoreCase) || string.Equals(_view.Current, id, StringComparison.OrdinalIgnoreCase)) return;
        _view.Show(Visible().First(v => string.Equals(v, id, StringComparison.OrdinalIgnoreCase)), _shown);
        FitFrame();
        _app.Config.Ui.PageSections[PageKey] = _view.Current!;
        _app.Save();
        SectionsChanged?.Invoke();
    }

    public bool ShowSectionOf(DependencyObject target)
    {
        if (_view.SectionOf(target) is not { } id) return false;
        ShowSection(id);
        return true;
    }

    public void MoveSections(IReadOnlyList<string> order)
    {
        var slots = order.Select(id => S.PanelLayout.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new PanelSlot { Id = id }).ToList();
        foreach (var id in Ordered().Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            var slot = S.PanelLayout.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new PanelSlot { Id = id };
            int at = Array.IndexOf(DefaultPanelOrder, id);
            slots.Insert(Math.Min(slots.Count, Math.Max(0, at)), slot);
        }
        S.PanelLayout.Clear();
        S.PanelLayout.AddRange(slots);
        _app.Save();
        SectionsChanged?.Invoke();
    }

    private void DefaultOrder()
    {
        S.PanelLayout.Clear();
        S.PanelLayout.AddRange(DefaultPanelOrder.Select(id => new PanelSlot { Id = id }));
        _app.Save();
        SectionsChanged?.Invoke();
    }

    public object? HistoryState => _view.Current;

    public void RestoreHistory(object? state)
    {
        if (state is string id) ShowSection(id);
    }

    public bool Reveal(string anchor)
    {
        bool built = false;
        foreach (var id in Visible().Where(id => !_view.Built(id)))
        {
            if (HitchWatch.On) HitchWatch.Note = id;
            _view.Build(id);
            built = true;
        }
        return built || _controller?.ShowTabFor(anchor) == true;
    }

    public void OnShown()
    {
        Layout(animate: false);
        _shown = true;
        _view.SetActive(true);
        _view.BuildLater(Visible());
        Motion.AfterPageSettles(Dispatcher, WarmSections);
    }

    private void WarmSections()
    {
        if (!_shown) return;
        var visible = Visible();
        if (_controller is null && visible.Contains("iracing")) IracingControllerSection.Preload(_app);
    }

    public void OnHidden()
    {
        _shown = false;
        _view.SetActive(false);
    }

    internal ScrollViewer Scroll => _scroll;

    internal SetupManagerSection? Setups => _setups;

    private void Only(string id)
    {
        ShowSection(id);
        _body.UpdateLayout();
        _scroll.ScrollToTop();
    }

    private void WheelCheck()
    {
        Only("setups");
        var setups = _view.Host("setups")!;
        setups.UpdateLayout();
        var inner = Descendants(setups).OfType<ScrollViewer>().Where(v => v.IsVisible && v.ActualHeight > 40).ToList();
        if (inner.Count == 0) throw new InvalidOperationException("wheel check: no scroll areas in the Setup Manager");
        foreach (var v in inner)
        {
            _scroll.ScrollToTop();
            v.ScrollToTop();
            _scroll.UpdateLayout();
            bool own = v.ScrollableHeight > 0.5;
            double page = _scroll.VerticalOffset, before = v.VerticalOffset;
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = PreviewMouseWheelEvent, Source = v };
            v.RaiseEvent(args);
            if (!args.Handled) v.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = MouseWheelEvent, Source = v });
            _scroll.UpdateLayout();
            bool pageMoved = _scroll.VerticalOffset > page + 1, innerMoved = v.VerticalOffset > before + 1;
            if (own ? pageMoved || !innerMoved : !pageMoved && _scroll.ScrollableHeight > 1)
                throw new InvalidOperationException($"wheel check: {(own ? "scrollable" : "non-scrollable")} area {v.ActualWidth:0}x{v.ActualHeight:0}: page moved {pageMoved}, area moved {innerMoved}");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        string? start = _view.Current;
        Only("iracing");
        foreach (var (name, enter) in _controller?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return ("controller-" + name, async () => { await enter(); _scroll.ScrollToTop(); });
        yield return ("controller-narrow", () => { _body.MaxWidth = 616; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("controller-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        ComboBox? dropdown = null;
        yield return ("dropdown-open", async () =>
        {
            dropdown = Descendants(_view.Host("iracing")!).OfType<ComboBox>().FirstOrDefault(c => c.IsVisible);
            if (dropdown is not null) { dropdown.Focus(); dropdown.IsDropDownOpen = true; }
            await Task.Delay(400);
        });
        yield return ("menus-closed", () => { if (dropdown is not null) dropdown.IsDropDownOpen = false; return Task.CompletedTask; });
        Only("fov");
        foreach (var state in _fov?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        Only("setups");
        foreach (var state in _setups?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        yield return ("setups-narrow", () => { _body.MaxWidth = 616; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("setups-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("shakers", () => { Only("shakers"); return Task.CompletedTask; });
        yield return ("companions", () => { Only("companions"); return Task.CompletedTask; });
        yield return ("reordered", () =>
        {
            MoveSections(DefaultPanelOrder.Reverse().ToList());
            if (Sections[0].Id != DefaultPanelOrder[^1]) throw new InvalidOperationException("Moving the tools didn't change their order.");
            return Task.CompletedTask;
        });
        yield return ("default-order", () =>
        {
            DefaultOrder();
            if (!Sections.Select(s => s.Id).SequenceEqual(DefaultPanelOrder)) throw new InvalidOperationException("Default order didn't put the tools back.");
            return Task.CompletedTask;
        });
        yield return ("wheel", () => { WheelCheck(); return Task.CompletedTask; });
        yield return ("back-to-start", () => { if (start is not null) ShowSection(start); _scroll.ScrollToTop(); return Task.CompletedTask; });
    }
}
