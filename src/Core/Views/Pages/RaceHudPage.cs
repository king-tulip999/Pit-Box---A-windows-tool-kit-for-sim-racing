using System.Windows;
using System.Windows.Controls;
using Core.Services;

namespace Core.Views.Pages;

internal sealed class RaceHudPage : UserControl, IPage, IScreenshotStates, ICancelEdit, IRevealAnchor
{
    private const string SectionId = "hud";

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(23, 0, 23, 23) };
    private readonly ScrollViewer _scroll;
    private readonly SectionView _view;
    private readonly ContentControl _tools = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private RaceHudSection? _hud;

    public RaceHudPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
        Ui.ChainWheel(_scroll);
        Content = _scroll;
        _view = new SectionView(() => _scroll);
        _view.Heading.Margin = Ui.HeaderMargin;
        _body.Children.Add(_view.Heading);
        _body.Children.Add(_view.Content);
        _view.Add(new SectionDef(SectionId, "iRacing Overlay", "", "I.RaceOverlay", Build, "simracing.hud.panel",
            () => _hud?.OnShown(), () => _hud?.OnHidden(), _tools));
        _view.Show(SectionId, animate: false);
    }

    private UIElement Build()
    {
        _hud = new RaceHudSection(_app, _window);
        _tools.Content = _hud.TitleTools;
        return _hud;
    }

    public void OnShown()
    {
        _view.SetActive(true);
        _view.BuildLater(new[] { SectionId });
    }

    public void OnHidden() => _view.SetActive(false);

    public bool Reveal(string anchor)
    {
        bool built = !_view.Built(SectionId);
        if (built) _view.Build(SectionId);
        return built || _hud?.ShowTabFor(anchor) == true;
    }

    public bool CancelEdit() => _hud?.CancelEdit() == true;

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        _view.Build(SectionId);
        yield return ("hud", () => { _scroll.ScrollToTop(); return Task.CompletedTask; });
        foreach (var state in _hud?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        yield return ("hud-narrow", async () =>
        {
            _body.MaxWidth = 616;
            _body.HorizontalAlignment = HorizontalAlignment.Left;
            if (_hud?.ScreenshotStates().FirstOrDefault(s => s.Name == "hud-standings") is { Enter: { } open }) await open();
        });
        yield return ("hud-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
    }
}
