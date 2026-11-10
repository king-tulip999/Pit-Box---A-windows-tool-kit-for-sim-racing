using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.Services;
using PitBox.Theme;
using PitBox.Views;

namespace PitBox.Grr.Views;

internal sealed class GrrDashboardSection : StackPanel
{
    private const string Consumer = "GRR League dashboard";
    private const double TwoColumnsAt = 729, Gap = Ui.PanelSpacing;
    private const int TopRows = 10;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly ContentControl _leaguePicker = new() { Focusable = false };
    private readonly TextBlock _freshness = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _refresh;
    private readonly ContentControl _content = new() { Focusable = false };
    private TextBlock? _heroClock;
    private bool _heroLive;
    private string? _freshnessText;
    private GrrRace? _heroRace;
    private string _shape = "";
    private bool _held, _allStandings, _fullSeason, _allResults;
    private string? _eventKey;
    private int _session = -1;
    private string _builtLeague = "";
    private FrameworkElement? _standingsCard, _resultsCard, _scheduleCard;

    internal enum Part { Top, Standings, Results, Schedule }

    internal FrameworkElement? CardFor(Part part)
    {
        if (_held && _builtLeague != (League ?? "")) { _shape = ""; Build(); }
        return part switch { Part.Standings => _standingsCard, Part.Results => _resultsCard, Part.Schedule => _scheduleCard, _ => null };
    }

    public GrrDashboardSection(AppServices app, MainWindow window, bool leagueTabs = true)
    {
        _app = app;
        _window = window;
        _refresh = GrrParts.RefreshButton(app);
        _refresh.Margin = new Thickness(0);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_freshness);
        right.Children.Add(_refresh);
        var edit = GrrParts.Compact("I.Pencil", "Driver and leagues", () => _ = GrrParts.EditAsync(_app, _window));
        edit.Margin = new Thickness(6, 0, 0, 0);
        edit.ToolTip = "Your driver name on the GRR website and the leagues you follow";
        right.Children.Add(edit);
        if (_app.Config.Ui.Theme != GrrTheme.Id)
        {
            var theme = GrrParts.Compact("I.Palette", "GRR theme", () => { });
            theme.Margin = new Thickness(6, 0, 0, 0);
            theme.ToolTip = "Show this page in the GRR theme; the rest of the app keeps yours";
            Ui.SetToggled(theme, _app.Config.Grr.PageTheme);
            theme.Click += (_, _) =>
            {
                _app.Config.Grr.PageTheme = !_app.Config.Grr.PageTheme;
                _app.Save();
                Ui.SetToggled(theme, _app.Config.Grr.PageTheme);
                ThemeToggled?.Invoke();
            };
            right.Children.Add(theme);
        }
        LeagueTabs = leagueTabs;
        if (leagueTabs)
        {
            var toolbar = Ui.SplitRow(_leaguePicker, right);
            toolbar.Margin = new Thickness(0, 0, 0, Gap);
            Children.Add(toolbar);
        }
        else
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Tools = right;
        }
        Children.Add(_content);
        SizeChanged += (_, e) => { if (e.WidthChanged && Shape() != _shape) Build(); };
    }

    internal FrameworkElement? Tools { get; }

    private string? League => _app.Config.Grr.DashboardLeagueOrNull();

    internal bool LeagueTabs { get; }

    internal void ShowLeague()
    {
        _shape = "";
        Build();
    }
    private string? Driver => _app.Config.Grr.DriverName;

    public void OnShown()
    {
        if (_held) return;
        _held = true;
        _app.Grr.Changed += OnChanged;
        _app.Tick += OnTick;
        _app.Grr.Acquire(Consumer);
        if (_content.Content is null || _shape != Shape() || !Inputs().Equals(_built))
        {
            _shape = "";
            Build();
        }
        else OnTick();
    }

    private (string? League, object? Data, bool Busy, object? Theme, string? Driver, string Leagues) _built;

    private (string? League, object? Data, bool Busy, object? Theme, string? Driver, string Leagues) Inputs() => InputsFor(League);

    private (string? League, object? Data, bool Busy, object? Theme, string? Driver, string Leagues) InputsFor(string? league) =>
        (league, league is null ? null : _app.Grr.Data(league), _app.Grr.Busy, PageTheme, Driver, string.Join("|", _app.Config.Grr.Leagues));

    public void OnHidden()
    {
        if (!_held) return;
        _held = false;
        _app.Grr.Changed -= OnChanged;
        _app.Tick -= OnTick;
        _app.Grr.Release(Consumer);
    }

    private void OnChanged()
    {
        _kept.Clear();
        _shape = "";
        Build();
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        if (_heroRace is { } race && _heroClock is not null)
        {
            if (race.Start + GrrService.RaceLength <= now || race.Start <= now && !_heroLive)
            {
                _shape = "";
                Build();
                return;
            }
            if (!_heroLive) _heroClock.Text = GrrLeagues.Countdown(GrrLeagues.Until(race.Start, now));
        }
        UpdateFreshness();
    }

    private double Width0 => ActualWidth > 0 ? ActualWidth : 1100;
    private bool TwoColumns => Width0 >= TwoColumnsAt;
    private double CardWidth => TwoColumns ? (Width0 - Gap) / 2 : Width0;

    private static int Level(double width) => width < 430 ? 1 : width < 600 ? 2 : width < 800 ? 3 : 4;

    private string Shape() => $"{League}|{TwoColumns}|{Level(CardWidth - 40)}|{Stacked}";

    internal ResourceDictionary? PageTheme { get; set; }

    internal Action? ThemeToggled { get; set; }

    internal void Rebuild()
    {
        _kept.Clear();
        _shape = "";
        Build();
    }

    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(2);

    private sealed record Kept(FrameworkElement Content, object Inputs, string Shape, bool FullSeason, DateTime At,
        FrameworkElement? Standings, FrameworkElement? Results, FrameworkElement? Schedule, TextBlock? HeroClock, GrrRace? HeroRace, bool HeroLive);

    private readonly Dictionary<string, Kept> _kept = new(StringComparer.OrdinalIgnoreCase);
    private (string? EventKey, int Session, bool AllStandings, bool AllResults, bool FullSeason) _builtState;
    private string _builtShape = "";
    private DateTime _builtAt;

    private object KeptInputs(string league) => (InputsFor(league), _app.Grr.ProblemOf(league));

    private void Keep()
    {
        var now = DateTime.Now;
        foreach (var old in _kept.Where(k => now - k.Value.At > KeepFor).Select(k => k.Key).ToList()) _kept.Remove(old);
        if (_builtLeague.Length == 0 || _content.Content is not FrameworkElement content || _builtState is not (null, < 0, false, false, _)
            || !_built.Equals(InputsFor(_builtLeague))) return;
        _kept[_builtLeague] = new Kept(content, KeptInputs(_builtLeague), _builtShape, _builtState.FullSeason, _builtAt,
            _standingsCard, _resultsCard, _scheduleCard, _heroClock, _heroRace, _heroLive);
    }

    private bool Reuse(string league, string shape)
    {
        if (!_kept.Remove(league, out var kept)) return false;
        var now = DateTime.Now;
        if (now - kept.At > KeepFor || kept.Shape != shape || kept.FullSeason != _fullSeason || !kept.Inputs.Equals(KeptInputs(league))) return false;
        if (kept.HeroRace is { } race && (race.Start + GrrService.RaceLength <= now || race.Start <= now && !kept.HeroLive)) return false;
        _standingsCard = kept.Standings;
        _resultsCard = kept.Results;
        _scheduleCard = kept.Schedule;
        _heroClock = kept.HeroClock;
        _heroRace = kept.HeroRace;
        _heroLive = kept.HeroLive;
        _builtState = (null, -1, false, false, kept.FullSeason);
        _builtShape = kept.Shape;
        _builtAt = kept.At;
        _content.Content = kept.Content;
        if (!_heroLive && _heroClock is not null && _heroRace is { } next) _heroClock.Text = GrrLeagues.Countdown(GrrLeagues.Until(next.Start, now));
        return true;
    }

    private void Build()
    {
        if (!_held) return;
        using var scope = Ui.Scope(PageTheme);
        var league = League;
        bool switched = _builtLeague != (league ?? "");
        if (switched)
        {
            Keep();
            _eventKey = null; _session = -1; _allStandings = _allResults = false; _builtLeague = league ?? "";
        }
        _built = Inputs();
        var shape = Shape();
        _shape = shape;
        _heroClock = null;
        _heroRace = null;
        _leaguePicker.Content = LeagueTabs ? LeaguePicker(league) : null;
        if (Tools is not null) Tools.Visibility = league is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateFreshness();
        if (league is null)
        {
            _content.Content = NoLeague();
            return;
        }
        if (switched && Reuse(league, shape)) return;
        _builtState = (_eventKey, _session, _allStandings, _allResults, _fullSeason);
        _builtShape = shape;
        _builtAt = DateTime.Now;

        var grr = _app.Grr;
        var data = grr.Data(league);
        var now = DateTime.Now;
        FrameworkElement next, driver, standings, results;
        if (data is null)
        {
            next = Card("Next race", Empty(grr));
            driver = Card("Driver profile", GrrParts.Placeholder(140, 90));
            standings = Card("Championship", grr.Busy ? GrrParts.Placeholder(220, 260, 240, 260) : Note("Shows once the league has been read."));
            results = Card("Results", grr.Busy ? GrrParts.Placeholder(200, 240, 220) : Note("Shows once the league has been read."));
            if (!grr.Busy) driver = Card("Driver profile", Note("Shows once the league has been read."));
        }
        else
        {
            next = NextRaceCard(data, now);
            driver = DriverCard(data, now);
            standings = StandingsCard(data);
            results = ResultsCard(data);
        }
        _standingsCard = standings;
        _resultsCard = results;
        _scheduleCard = next;

        if (TwoColumns)
        {
            var top = Pair(next, driver);
            var bottom = Pair(standings, results);
            bottom.Margin = new Thickness(0, Gap, 0, 0);
            _content.Content = new StackPanel { Children = { top, bottom } };
        }
        else _content.Content = Column(next, driver, standings, results);
    }

    private const double NextShare = 0.5;

    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NextShare, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - NextShare, GridUnitType.Star) });
        left.Margin = right.Margin = new Thickness(0);
        left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Stretch;
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private FrameworkElement NoLeague()
    {
        var pick = Ui.LabeledButton("I.Plus", "Choose leagues", () => _ = GrrParts.EditAsync(_app, _window), "PrimaryButton");
        var empty = Ui.EmptyState("I.Flag", "No league picked",
            "Pick the GRR series you race in. Each one gets its own entry in the GRR menu with its next race, standings, results and schedule.", pick);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = empty };
    }

    private static StackPanel Column(params FrameworkElement[] cards)
    {
        var stack = new StackPanel();
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].Margin = new Thickness(0, i == 0 ? 0 : Gap, 0, 0);
            stack.Children.Add(cards[i]);
        }
        return stack;
    }

    private FrameworkElement LeaguePicker(string? league)
    {
        var followed = _app.Config.Grr.Leagues;
        bool Follows(GrrLeague l) => followed.Contains(l.Id, StringComparer.OrdinalIgnoreCase);
        int selected = league is null ? -1 : GrrLeagues.All.ToList().FindIndex(l => l.Id == league);
        var tips = GrrLeagues.All.Select(l => (string?)(Follows(l) ? l.Name : $"{l.Name}: tick it under Driver and leagues to see it here")).ToList();
        var picker = Ui.Segmented(GrrLeagues.All.Select(l => l.Name).ToList(), selected, i =>
        {
            if (!Follows(GrrLeagues.All[i])) return;
            _app.Config.Grr.DashboardLeague = GrrLeagues.All[i].Id;
            _app.Save();
            _shape = "";
            Dispatcher.InvokeAsync(Build);
        }, "grrleague", tips);
        if (picker.Child is Panel tabs)
            for (int i = 0; i < tabs.Children.Count && i < GrrLeagues.All.Count; i++)
                if (tabs.Children[i] is RadioButton tab)
                {
                    tab.IsEnabled = Follows(GrrLeagues.All[i]);
                    ToolTipService.SetShowOnDisabled(tab, true);
                    System.Windows.Automation.AutomationProperties.SetName(tab, GrrLeagues.All[i].Name + (tab.IsEnabled ? "" : " (not followed)"));
                }
        picker.VerticalAlignment = VerticalAlignment.Center;
        return picker;
    }

    private void UpdateFreshness()
    {
        using var scope = Ui.Scope(PageTheme);
        var grr = _app.Grr;
        GrrParts.SyncRefresh(_refresh, _app);
        string text; string brush = "TextMuted"; string? tip = null;
        if (League is null) text = "";
        else if (grr.Busy) text = "Updating…";
        else if (grr.ProblemOf(League!) is { } problem)
        {
            text = grr.FetchedAt(League!) is { } t ? $"Couldn't update · from {GrrParts.Ago(t)}" : "Couldn't reach the GRR website";
            brush = "WarnDeep";
            tip = problem;
        }
        else text = grr.FetchedAt(League!) is { } t ? $"Updated {GrrParts.Ago(t)}" : "Not read yet";
        if (_freshnessText != text) { _freshnessText = text; _freshness.Text = text; }
        _freshness.Foreground = Ui.Res<Brush>(brush);
        _freshness.FontSize = 12;
        _freshness.ToolTip = tip;
    }

    private static Tile Card(string? title, UIElement body, params FrameworkElement[] tools) => Card(title, body, null, 0, tools);

    private static Tile Card(string? title, UIElement body, string? place, int shape, params FrameworkElement[] tools)
    {
        var stack = new DockPanel { LastChildFill = true };
        if (title is not null)
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 11), LastChildFill = true };
            var toolRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var t in tools)
            {
                t.Margin = new Thickness(6, 0, 0, 0);
                toolRow.Children.Add(t);
            }
            DockPanel.SetDock(toolRow, Dock.Right);
            head.Children.Add(toolRow);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(Ui.Text(title, "H3"));
            head.Children.Add(name);
            DockPanel.SetDock(head, Dock.Top);
            stack.Children.Add(head);
        }
        stack.Children.Add(body);
        if (place is null) return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
        stack.Margin = new Thickness(CardPadding);
        var backdrop = new Grid { ClipToBounds = true };
        backdrop.Children.Add(new Backdrop(shape, place, place + "Wash") { Opacity = BackdropStrength });
        backdrop.Children.Add(stack);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = backdrop, ClipToBounds = true };
    }

    private const double CardPadding = 15, BackdropStrength = 0.4;

    private static TextBlock Note(string text) => GrrParts.Wrapped(text);

    private FrameworkElement Empty(GrrService grr) =>
        grr.Busy ? GrrParts.Placeholder(160, 210, 240, 180)
        : grr.ProblemOf(League!) is { } problem ? GrrParts.Problem(_app, "Couldn't reach the GRR website.", problem)
        : Note("Not read from the GRR website yet.");

    private FrameworkElement Footer(params FrameworkElement[] items)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var i in items) row.Children.Add(i);
        return row;
    }

    private Button Toggle(string label, Action flip)
    {
        var b = new Button { Content = label, Style = Ui.Res<Style>("GhostButton"), Margin = new Thickness(0, 0, Ui.ButtonGap, Ui.ButtonGap) };
        b.Click += (_, _) => { flip(); _shape = ""; Build(); };
        return b;
    }

    private GrrLeague LeagueInfo => GrrLeagues.Find(League!)!;

    private const double SplitAt = 540, SideGap = 18;

    private bool Stacked => CardWidth < SplitAt;

    private FrameworkElement Split(FrameworkElement left, FrameworkElement? right)
    {
        if (right is null) return left;
        if (Stacked)
        {
            var side = Hairline.Frame(new Border { BorderBrush = Ui.Res<Brush>("Line"), Padding = new Thickness(0, 14, 0, 0), Margin = new Thickness(0, 14, 0, 0), Child = right }, Sides.Top);
            var stack = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(left, Dock.Top);
            stack.Children.Add(left);
            stack.Children.Add(side);
            return stack;
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left.Margin = new Thickness(0, 0, SideGap, 0);
        grid.Children.Add(left);
        var edge = Hairline.Frame(new Border { BorderBrush = Ui.Res<Brush>("Line"), Padding = new Thickness(SideGap, 0, 0, 0), Child = right }, Sides.Left);
        Grid.SetColumn(edge, 1);
        grid.Children.Add(edge);
        return grid;
    }

    private static TextBlock SideLabel(string text)
    {
        var label = Label(text);
        label.Margin = new Thickness(0, 0, 0, 8);
        return label;
    }

    private Tile NextRaceCard(GrrLeagueData data, DateTime now)
    {
        var left = new DockPanel { LastChildFill = true };
        var round = data.Schedule?.FirstOrDefault(r => r.Start + GrrService.RaceLength > now);
        if (round is null)
        {
            var over = new StackPanel();
            over.Children.Add(Note(data.Schedule is null ? "The league's feed has no schedule." : "No more races on this season's schedule."));
            if (data.Season is { } name) over.Children.Add(GrrParts.Caption(name, 4));
            DockPanel.SetDock(over, Dock.Top);
            left.Children.Add(over);
            left.Children.Add(new Border());
        }
        else
        {
            var race = new GrrRace(LeagueInfo.Id, round.Start, round.Track, round.Number is { } n ? $"Round {n}" : null);
            var hero = GrrParts.Hero(race, data, now, out var clock, 31, league: false, wrap: true);
            DockPanel.SetDock(hero, Dock.Top);
            left.Children.Add(hero);
            _heroClock = clock;
            _heroRace = race;
            _heroLive = race.Start <= now;
            var outline = new TrackOutline { MinHeight = OutlineHeight, Margin = new Thickness(0, 14, 0, 0) };
            left.Children.Add(outline);
            _ = ShowOutlineAsync(outline, round.Track);
        }
        return Card(null, Split(left, ScheduleList(data, now)), RaceLook.Key(null), 1);
    }

    private const double OutlineHeight = 96;

    private static async Task ShowOutlineAsync(TrackOutline outline, string track)
    {
        SimRacing.TrackMaps.TrackMap? map = null;
        try { map = await SimRacing.TrackMaps.TrackMapLibrary.ForLooseName(track); }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException) { }
        if (map is null) outline.Visibility = Visibility.Collapsed;
        else outline.Map = map;
    }

    private FrameworkElement ScheduleList(GrrLeagueData data, DateTime now)
    {
        var link = GrrParts.Link("Full schedule", LeagueInfo.Url(LeagueInfo.SchedulePage));
        if (data.Schedule is not { Count: > 0 } rounds)
            return new StackPanel { Children = { Note(data.Schedule is null ? "Couldn't read the schedule from the league's feed." : "No rounds on the schedule yet."), Footer(link) } };

        int next = rounds.ToList().FindIndex(r => r.Start + GrrService.RaceLength > now);
        var form = Driver is { } driver ? data.SeasonForm(driver, now) : null;
        var list = new GrrFitList { Anchor = next < 0 ? rounds.Count - 1 : next, ShowAll = _fullSeason };
        for (int i = 0; i < rounds.Count; i++)
        {
            var r = rounds[i];
            bool past = r.Start + GrrService.RaceLength <= now;
            var mark = i == next ? GrrParts.TimelineMark.Next : past ? GrrParts.TimelineMark.Done : GrrParts.TimelineMark.Later;
            var eyebrow = (r.Number is { } n ? $"Round {n}" : "Round") + (r.Format is { } f ? " · " + f : "");
            FrameworkElement? right = null;
            Action? click = null;
            object? tipContent = null;
            if (past)
            {
                var outcome = data.ClassResults(r);
                if (form?[i].Finish is { } finish) right = GrrParts.Position(finish, false, 12, prefix: true, plain: "Heading");
                else
                {
                    var winners = outcome.Where(o => o.Winner is not null).ToList();
                    right = winners.Count switch
                    {
                        0 => new TextBlock { Text = r.Done ? "Run" : "No result", Style = Ui.Res<Style>("Caption") },
                        1 => Winner(winners[0].Winner!, winners[0].FastestLap is { } fl && GrrFeed.SameDriver(fl, winners[0].Winner!)),
                        _ => Winner($"{winners[0].Winner} +{winners.Count - 1}", winners[0].FastestLap is { } fl2 && GrrFeed.SameDriver(fl2, winners[0].Winner!)),
                    };
                }
                var e = data.EventFor(r);
                if (e is not null)
                    click = () => { _eventKey = Key(e); _session = -1; _allResults = false; _shape = ""; Build(); ShowResults(); };
                tipContent = GrrParts.RoundTip(outcome, e is null ? null : "Click to see the results");
            }
            string when = i == next ? GrrParts.ShortWhen(r.Start) : Dates.Weekday(r.Start) + " " + Dates.Short(r.Start);
            list.Children.Add(GrrParts.TimelineRow(eyebrow, r.Track, when, right, mark, true, true, click, tipContent, wrap: true));
        }
        var body = new DockPanel { LastChildFill = true };
        var label = SideLabel("Schedule");
        DockPanel.SetDock(label, Dock.Top);
        body.Children.Add(label);
        var footer = new List<FrameworkElement>();
        if (rounds.Count > 3 || _fullSeason) footer.Add(Toggle(_fullSeason ? "Show fewer" : $"Show all {rounds.Count} rounds", () => _fullSeason = !_fullSeason));
        footer.Add(link);
        var foot = Footer(footer.ToArray());
        DockPanel.SetDock(foot, Dock.Bottom);
        body.Children.Add(foot);
        body.Children.Add(list);
        return body;
    }

    private Tile DriverCard(GrrLeagueData data, DateTime now)
    {
        var driver = Driver;
        if (driver is null)
        {
            var empty = new StackPanel();
            empty.Children.Add(Note("Add your driver name as it appears on the GRR website to see your championship here and be highlighted in the standings and results."));
            var add = GrrParts.Compact("I.Plus", "Add driver name", () => _ = GrrParts.EditAsync(_app, _window));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 8, 0, 0);
            empty.Children.Add(add);
            return Card(null, empty);
        }
        var body = new StackPanel();
        var mine = data.StandingOf(driver);
        string tier = RaceLook.Key(mine?.Row.Rank);
        body.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = mine?.Row.Driver ?? driver, Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis }));
        body.Children.Add(GrrParts.Caption(mine is { } m
            ? string.Join(" · ", new[] { data.ClassLabel(m.Class), m.Row.Car, m.Row.Team }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } sub ? sub : LeagueInfo.Name
            : $"Not in the {LeagueInfo.Name} standings"));

        var form = data.SeasonForm(driver, now);
        if (mine is { } me)
        {
            var table = data.Standings![me.Class];
            var main = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
            var place = new StackPanel { Orientation = Orientation.Horizontal };
            place.Children.Add(Figure($"P{me.Row.Rank}", 44, tier + "Text"));
            var of = GrrParts.Caption($"of {table.Count}");
            of.VerticalAlignment = VerticalAlignment.Bottom;
            of.Margin = new Thickness(7, 0, 0, GrrParts.BaselineBottom(44, 12, figureLines: true));
            place.Children.Add(of);
            main.Children.Add(Stat("Championship", place, null, big: true));
            main.Children.Add(Stat("Points", GrrParts.Points(me.Row.Points), null, big: true));
            body.Children.Add(main);

            int at = table.ToList().IndexOf(me.Row);
            var gaps = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            void Gap(string text)
            {
                var chip = Ui.Chip(text);
                chip.Margin = new Thickness(0, 0, 6, 4);
                gaps.Children.Add(chip);
            }
            if (at > 0) Gap(table[0].Points == me.Row.Points ? "Level with P1" : $"{GrrParts.Points(table[0].Points - me.Row.Points)} behind P1");
            if (at >= 0 && at + 1 < table.Count) Gap($"{GrrParts.Points(me.Row.Points - table[at + 1].Points)} ahead of P{table[at + 1].Rank}");
            if (gaps.Children.Count > 0) body.Children.Add(gaps);

            var record = new List<(string, string)>();
            if (me.Row.Wins is { } w) record.Add(("Wins", $"{w}"));
            if (me.Row.Podiums is { } p) record.Add(("Podiums", $"{p}"));
            else if (me.Row.Top5 is { } t5) record.Add(("Top 5", $"{t5}"));
            if (me.Row.Top10 is { } t10) record.Add(("Top 10", $"{t10}"));
            if (me.Row.Poles is { } poles) record.Add(("Poles", $"{poles}"));
            if (me.Row.Starts is { } st) record.Add(("Starts", $"{st}"));
            if (GrrLeagueData.AverageFinish(form) is { } avg) record.Add(("Average", "P" + avg.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)));
            if (record.Count > 0) body.Children.Add(Record(record));
        }
        body.Children.Add(Footer(GrrParts.Link("Driver history", GrrLeagues.DriverUrl, GrrParts.DriverHistoryTip(driver), () => GrrParts.OpenDriverHistory(driver))));

        FrameworkElement? side = null;
        if (form.Count > 0 && (mine is not null || form.Any(f => f.Finish is not null)))
        {
            var stack = new StackPanel();
            stack.Children.Add(SideLabel("Season form"));
            stack.Children.Add(FormTiles(form));
            side = stack;
        }
        return Card(null, Split(body, side), tier, 3);
    }

    private static UniformGrid Record(List<(string Label, string Value)> items)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -14, 0) };
        foreach (var (label, value) in items)
        {
            var row = new DockPanel { LastChildFill = true };
            var number = RaceParts.LiveFigure(value, 17, "Heading");
            number.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(number, Dock.Right);
            row.Children.Add(number);
            var name = Label(label);
            name.VerticalAlignment = VerticalAlignment.Bottom;
            name.Margin = new Thickness(0, 0, 6, 1);
            row.Children.Add(name);
            grid.Children.Add(Hairline.Frame(new Border { BorderBrush = Ui.Res<Brush>("Line"), Padding = new Thickness(0, 0, 0, 5), Margin = new Thickness(0, 0, 14, 8), Child = row }, Sides.Bottom));
        }
        return grid;
    }

    private const int FormWideColumns = 6, FormDenseColumns = 8, FormWideUpTo = 18;

    internal static int FormColumns(int rounds) => rounds <= FormWideUpTo ? FormWideColumns : FormDenseColumns;

    private static UniformGrid FormTiles(IReadOnlyList<GrrFormRound> form)
    {
        int columns = FormColumns(form.Count);
        double size = columns == FormWideColumns ? 19 : 16;
        var grid = new UniformGrid { Columns = columns, Margin = new Thickness(-2) };
        for (int i = 0; i < form.Count; i++)
        {
            var f = form[i];
            string round = $"R{f.Number ?? i + 1}";
            string name = f.Number is { } n ? $"Round {n}" : $"Round {i + 1}";
            int? podium = f.Finish is >= 1 and <= 3 ? f.Finish : null;
            var tile = Hairline.Frame(new Border
            {
                Margin = new Thickness(2), Padding = new Thickness(2, 4, 2, 4),
                Background = Ui.Res<Brush>(podium is { } pb ? $"Podium{pb}Bg" : f.State == GrrFormState.Next ? "AccentSoft" : "SurfaceAlt"),
                BorderBrush = Ui.Res<Brush>(podium is { } pe ? $"Podium{pe}Edge" : f.State == GrrFormState.Next ? "Accent" : "Line"),
            });
            tile.SetResourceReference(Border.CornerRadiusProperty, "R.Small");
            var stack = new StackPanel();
            var label = new TextBlock { Text = round, FontSize = 10, Foreground = Ui.Res<Brush>("TextFaint"), HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new Viewbox { Child = label, StretchDirection = StretchDirection.DownOnly, Height = 13 });
            FrameworkElement value;
            if (f.State == GrrFormState.Next)
                value = new System.Windows.Shapes.Ellipse { Width = 11, Height = 11, StrokeThickness = 2, Stroke = Ui.Res<Brush>("AccentText"), Margin = new Thickness(0, (size * 1.2 - 11) / 2, 0, (size * 1.2 - 11) / 2) };
            else
            {
                var text = RaceParts.LiveFigure(f.Finish is { } p ? $"P{p}" : PitBox.SimRacing.Race.RaceFormat.Missing, size,
                    podium is { } pf ? $"Podium{pf}Fg" : f.State == GrrFormState.Finished ? "Heading" : f.State == GrrFormState.Later ? "LineStrong" : "TextFaint");
                text.HorizontalAlignment = HorizontalAlignment.Center;
                value = new Viewbox { Child = text, StretchDirection = StretchDirection.DownOnly, Height = size * 1.2 };
            }
            value.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(value);
            tile.Child = stack;
            tile.ToolTip = f.State switch
            {
                GrrFormState.Finished => $"{name} · {f.Track} · P{f.Finish}",
                GrrFormState.NoResult => $"{name} · {f.Track} · no result for you",
                GrrFormState.Next => $"{name} · {f.Track} · next race",
                _ => $"{name} · {f.Track} · {GrrParts.LongDate(f.Start)}",
            };
            grid.Children.Add(tile);
        }
        return grid;
    }

    private static StackPanel Stat(string label, string value, string? note = null, bool big = false) =>
        Stat(label, Figure(value, big ? 44 : 24, "Heading"), note, big);

    private static TextBlock Figure(string value, double size, string brush)
    {
        var number = RaceParts.LiveFigure(value, size, brush);
        number.HorizontalAlignment = HorizontalAlignment.Left;
        return number;
    }

    private static StackPanel Stat(string label, FrameworkElement v, string? note = null, bool big = false)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, big ? 29 : 19, 10), MinWidth = big ? 73 : 39 };
        s.Children.Add(Label(label));
        v.Margin = new Thickness(0, 2, 0, 0);
        v.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(v);
        if (note is not null) s.Children.Add(GrrParts.Caption(note));
        return s;
    }

    private const int FormSlots = 5, PodiumPlaces = 3, ListMin = TopRows - PodiumPlaces;
    private const double PosWidth = 32, MoveWidth = 30, FormWidth = 116, GapWidth = 64, PointsWidth = 80, StartWidth = 72, ResultGapWidth = 74, ResultPointsWidth = 48;

    private Tile StandingsCard(GrrLeagueData data)
    {
        if (data.Standings is null || data.Standings.Count == 0)
            return Card("Championship", Note(data.Standings is null ? "Couldn't read the standings from the league's feed." : "No standings posted yet."),
                GrrParts.Link("Standings", LeagueInfo.Url(LeagueInfo.StandingsPage)));
        var keys = data.StandingKeys.ToList();
        var picked = _app.Config.Grr.DashboardClass;
        string key = picked is not null && keys.Contains(picked) ? picked : data.StandingOf(Driver)?.Class ?? keys[0];
        var table = data.Standings[key];
        var now = DateTime.Now;
        int level = Level(CardWidth - 40);
        double leaderPoints = table.Count > 0 ? table[0].Points : 0;
        bool Mine(GrrStanding r) => GrrFeed.SameDriver(r.Driver, Driver);
        string GapOf(GrrStanding r) => r == table[0] ? "Leader" : table[0].Points == r.Points ? "Tied" : "+" + GrrParts.Points(table[0].Points - r.Points);

        var top = new StackPanel();
        var moves = GrrChampionship.Moves(data, key);
        if (table.Count > 1 && GrrChampionship.Title(data, key, now, moves is not null) is { } fight)
        {
            var strip = GrrBoard.TitleFight(fight, data.Schedule ?? Array.Empty<GrrRound>(), now);
            strip.Margin = new Thickness(0, 0, 0, 12);
            top.Children.Add(strip);
        }
        var steps = table.Take(PodiumPlaces).Select((r, i) => new GrrBoard.Step(i + 1, r.Driver, r.Team ?? r.Car, GrrParts.Points(r.Points), GapOf(r), StandingTags(r), Mine(r))).ToList();
        top.Children.Add(GrrBoard.Podium(steps));

        var rest = table.Skip(PodiumPlaces).ToList();
        bool hasPast = data.Schedule?.Any(r => r.Start + GrrService.RaceLength <= now) == true;
        bool showMove = moves is not null && level >= 2, showForm = hasPast && level >= 2, showGap = level >= 2;
        var widths = new List<double> { PosWidth };
        if (showMove) widths.Add(MoveWidth);
        widths.Add(0);
        if (showForm) widths.Add(FormWidth);
        if (showGap) widths.Add(GapWidth);
        widths.Add(PointsWidth);

        FrameworkElement Row(GrrStanding r)
        {
            bool mine = Mine(r);
            var grid = RowGrid(widths);
            int c = 0;
            Put(grid, PositionCell(r.Rank, mine), c++);
            if (showMove)
            {
                int? move = GrrChampionship.MoveOf(moves, r.Driver);
                Put(grid, GrrBoard.Move(move, move is { } m ? GrrBoard.MoveTip(m, null) : null), c++);
            }
            Put(grid, DriverCell(r.Driver, Second(level >= 3 ? r.Car : null, r.Team), mine, null), c++);
            if (showForm) Put(grid, GrrBoard.Form(GrrChampionship.Recent(data.SeasonForm(r.Driver, now), FormSlots), FormSlots), c++);
            if (showGap) Put(grid, Figure12(GapOf(r), "TextMuted", mine), c++);
            var points = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) };
            points.Children.Add(Figure12(GrrParts.Points(r.Points), "Heading", true, 0));
            points.Children.Add(GrrBoard.Bar(leaderPoints > 0 ? r.Points / leaderPoints : 0));
            Put(grid, points, c);
            return RowFrame(grid, mine);
        }

        var heads = new List<(string, string?, bool)> { ("Pos", null, false) };
        if (showMove) heads.Add(("", null, false));
        heads.Add(("Driver", null, false));
        if (showForm) heads.Add(("Last 5", "Finishing positions in the last five rounds", false));
        if (showGap) heads.Add(("Gap", "Points behind the leader", true));
        heads.Add(("Pts", null, true));

        var (list, pinned) = FitRows(rest, Mine, _allStandings, Row);
        var footer = new List<FrameworkElement>();
        if (table.Count > TopRows) footer.Add(Toggle(_allStandings ? $"Show top {TopRows}" : $"Show all {table.Count}", () => _allStandings = !_allStandings));
        footer.Add(GrrParts.Link("Full standings", LeagueInfo.Url(LeagueInfo.StandingsPage)));

        var tools = new List<FrameworkElement>();
        if (keys.Count > 1)
        {
            var classes = Ui.Segmented(keys.Select(data.ClassLabel).ToList(), keys.IndexOf(key), i =>
            {
                _app.Config.Grr.DashboardClass = keys[i];
                _app.Save();
                _shape = "";
                Dispatcher.InvokeAsync(Build);
            }, "grrclass");
            tools.Add(classes);
        }
        return Card("Championship", Board(top, rest.Count > 0 ? HeaderRow(widths, heads) : null, list, pinned, Footer(footer.ToArray())), tools.ToArray());
    }

    private static List<FrameworkElement> StandingTags(GrrStanding r)
    {
        var tags = new List<FrameworkElement>();
        if (r.Wins is int w and > 0) tags.Add(GrrBoard.Tag(PitBox.SimRacing.Race.RaceFormat.Count(w, "win")));
        if (r.Podiums is int p and > 0) tags.Add(GrrBoard.Tag(PitBox.SimRacing.Race.RaceFormat.Count(p, "podium")));
        if (r.Poles is int pole and > 0) tags.Add(GrrBoard.Tag(PitBox.SimRacing.Race.RaceFormat.Count(pole, "pole")));
        return tags;
    }

    private static string? Second(string? car, string? team)
    {
        var parts = new[] { car, team }.Where(x => !string.IsNullOrWhiteSpace(x) && x != "–").ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static TextBlock Label(string text)
    {
        var t = TextFit.TipWhenTrimmed(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return t;
    }

    private static Grid RowGrid(IReadOnlyList<double> widths)
    {
        var grid = new Grid { Margin = new Thickness(6, 6, 8, 6) };
        foreach (var w in widths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        return grid;
    }

    private static void Put(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static Border RowFrame(Grid grid, bool mine)
    {
        var back = new Border { CornerRadius = ThemeManager.Round(4), Child = grid };
        if (mine) back.Background = Ui.Res<Brush>("AccentSoft");
        else Ui.HoverHighlight(back);
        return Hairline.Frame(new Border { BorderBrush = Ui.Res<Brush>(mine ? "AccentSoft" : "Line"), Child = back }, Sides.Bottom);
    }

    private static Border HeaderRow(IReadOnlyList<double> widths, IReadOnlyList<(string Text, string? Tip, bool Right)> heads)
    {
        var grid = RowGrid(widths);
        grid.Margin = new Thickness(6, 12, 8, 6);
        for (int c = 0; c < heads.Count; c++)
        {
            if (heads[c].Text.Length == 0) continue;
            var head = Label(heads[c].Text);
            head.ToolTip = heads[c].Tip;
            bool right = heads[c].Right;
            head.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
            head.Margin = new Thickness(0, 0, right ? 2 : 0, 0);
            Put(grid, head, c);
        }
        return Hairline.Frame(new Border { BorderBrush = Ui.Res<Brush>("Line"), Child = grid }, Sides.Bottom);
    }

    private static FrameworkElement PositionCell(int position, bool mine)
    {
        var cell = GrrParts.Position(position, mine);
        cell.IsHitTestVisible = position <= 3;
        return cell;
    }

    private static FrameworkElement DriverCell(string name, string? second, bool mine, IReadOnlyList<FrameworkElement>? marks)
    {
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var line = new TrimRow();
        line.Children.Add(new TextBlock
        {
            Text = name, FontSize = 12, FontWeight = mine ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.Res<Brush>(mine ? "AccentText" : "Text"),
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name, VerticalAlignment = VerticalAlignment.Center,
        });
        foreach (var m in marks ?? Array.Empty<FrameworkElement>())
        {
            m.Margin = new Thickness(5, 0, 0, 0);
            m.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(m);
        }
        who.Children.Add(line);
        if (second is not null) who.Children.Add(GrrParts.Caption(second));
        return who;
    }

    private static TextBlock Figure12(string text, string brush, bool strong, double right = 2) => TextFit.TipWhenTrimmed(new TextBlock
    {
        Text = text, FontSize = 12, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ui.Res<Brush>(brush),
        TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, right, 0),
        TextTrimming = TextTrimming.CharacterEllipsis, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
    });

    private static (GrrFitList List, FrameworkElement? Pinned) FitRows<T>(List<T> rows, Func<T, bool> mine, bool all, Func<T, FrameworkElement> build, Func<T, FrameworkElement?>? before = null)
    {
        int me = rows.FindIndex(r => mine(r));
        var shown = !all && me >= ListMin ? rows.Take(me).ToList() : rows;
        var children = new List<FrameworkElement>();
        int minimum = 0;
        for (int i = 0; i < shown.Count; i++)
        {
            int counted = i < ListMin ? 1 : 0;
            if (before?.Invoke(shown[i]) is { } divider)
            {
                children.Add(divider);
                minimum += counted;
            }
            children.Add(build(shown[i]));
            minimum += counted;
        }
        var list = new GrrFitList { MinRows = minimum, ShowAll = all, Rail = null };
        foreach (var child in children) list.Children.Add(child);
        return (list, !all && me >= ListMin ? build(rows[me]) : null);
    }

    private static DockPanel Board(FrameworkElement top, FrameworkElement? header, GrrFitList list, FrameworkElement? pinned, FrameworkElement footer)
    {
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        if (header is not null)
        {
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
        }
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        if (pinned is not null)
        {
            DockPanel.SetDock(pinned, Dock.Bottom);
            dock.Children.Add(pinned);
        }
        list.VerticalAlignment = VerticalAlignment.Stretch;
        dock.Children.Add(list);
        return dock;
    }

    private Tile ResultsCard(GrrLeagueData data)
    {
        var link = GrrParts.Link("Latest results", LeagueInfo.Url(LeagueInfo.ResultsPage), "The latest race's results on the GRR website");
        var events = (data.Events ?? Array.Empty<GrrEvent>()).Where(e => e.Sessions.Any(s => s.Rows.Count > 0)).Reverse().ToList();
        if (events.Count == 0)
            return Card("Results", Note(data.Events is null ? "Couldn't read the results from the league's feed." : "No results posted yet this season."), link);

        var ev = events.FirstOrDefault(e => Key(e) == _eventKey) ?? events[0];
        var round = data.RoundFor(ev);
        var picker = new ComboBox { MinWidth = 162, MaxWidth = 243 };
        foreach (var e in events)
        {
            var r = data.RoundFor(e);
            picker.Items.Add(new ComboBoxItem { Content = $"{(r?.Number is { } n ? $"Round {n} · " : "")}{e.Track}", Tag = Key(e), IsSelected = e == ev, ToolTip = GrrParts.LongDate(e.Date) });
        }
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: string k } && k != Key(ev))
            {
                _eventKey = k;
                _session = -1;
                _allResults = false;
                _shape = "";
                Dispatcher.InvokeAsync(Build);
            }
        };
        picker.ToolTip = "Results of another round";

        var sessions = ev.Sessions.Where(s => s.Rows.Count > 0).ToList();
        int si = _session >= 0 ? Math.Clamp(_session, 0, sessions.Count - 1) : MySession(data, sessions);
        var session = sessions[si];
        var rows = session.Rows;
        int level = Level(CardWidth - 40);

        var top = new StackPanel();
        var details = new List<string> { GrrParts.LongDate(ev.Date) };
        if (round?.Format is { } f) details.Add(char.ToUpperInvariant(f[0]) + f[1..]);
        if (round?.Laps is { } laps) details.Add(PitBox.SimRacing.Race.RaceFormat.Count(laps, "lap"));
        details.Add(PitBox.SimRacing.Race.RaceFormat.Count(rows.Count, "starter"));
        var outline = new TrackOutline();
        _ = ShowOutlineAsync(outline, ev.Track);
        top.Children.Add(GrrBoard.EventHeader(round?.Number, ev.Track, string.Join(" · ", details), level >= 2 ? outline : null));

        if (sessions.Count > 1)
        {
            var tabs = Ui.Segmented(sessions.Select(s => data.ClassNamed(s.Label) ?? Services.TrayService.SentenceCase(s.Label)).ToList(), si, i =>
            {
                _session = i;
                _allResults = false;
                _shape = "";
                Dispatcher.InvokeAsync(Build);
            }, "grrsession");
            tabs.HorizontalAlignment = HorizontalAlignment.Left;
            tabs.Margin = new Thickness(0, 12, 0, 0);
            top.Children.Add(tabs);
        }

        bool mixed = rows.Select(r => r.Class).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        bool classed = data.Classes.Count > 0;
        string? tableClass = data.ClassNamed(session.Label);
        var grid = GrrChampionship.GridPlaces(rows);
        bool Mine(GrrResult r) => GrrFeed.SameDriver(r.Driver, Driver);
        string? Sub(GrrResult r) => Second(level >= 2 || mixed ? r.Car : null, mixed ? r.Class : null);
        string Pts(GrrResult r) => r.Points is { } p ? GrrParts.Points(p) : PitBox.SimRacing.Race.RaceFormat.Missing;

        var steps = rows.Take(PodiumPlaces).Select((r, i) =>
        {
            var tags = Marks(r);
            if (grid.TryGetValue(r, out var g) && g != r.Position)
            {
                var from = GrrBoard.Tag($"From P{g}");
                from.ToolTip = $"Started P{g}, finished P{r.Position}";
                tags.Add(from);
            }
            return new GrrBoard.Step(i + 1, r.Driver, Sub(r), Pts(r), r.Position == 1 ? "Winner" : r.Interval, tags, Mine(r));
        }).ToList();
        var podium = GrrBoard.Podium(steps);
        podium.Margin = new Thickness(0, 12, 0, 0);
        top.Children.Add(podium);

        var awards = GrrChampionship.Awards(rows);
        if (awards.Count > 0)
        {
            var row = GrrBoard.Awards(awards, level >= 2 ? 4 : 2);
            row.Margin = new Thickness(-GrrBoard.StepGap / 2, 12 - GrrBoard.StepGap / 2, -GrrBoard.StepGap / 2, -GrrBoard.StepGap / 2);
            top.Children.Add(row);
        }

        bool showStart = level >= 2 && grid.Count > 0, showPoints = rows.Any(r => r.Points is not null);
        var widths = new List<double> { PosWidth, 0 };
        var heads = new List<(string, string?, bool)> { ("Pos", null, false), ("Driver", null, false) };
        if (showStart) { widths.Add(StartWidth); heads.Add(("Start", "Starting position and places gained", false)); }
        widths.Add(ResultGapWidth);
        heads.Add(("Gap", "Gap to the car ahead or the leader, as posted", true));
        if (showPoints) { widths.Add(ResultPointsWidth); heads.Add(("Pts", null, true)); }

        FrameworkElement Row(GrrResult r)
        {
            bool mine = Mine(r);
            bool lapped = GrrChampionship.Lapped(r);
            var cells = RowGrid(widths);
            int c = 0;
            Put(cells, PositionCell(r.Position, mine), c++);
            Put(cells, DriverCell(r.Driver, Sub(r), mine, Marks(r)), c++);
            if (showStart) Put(cells, GrrBoard.Gain(grid.TryGetValue(r, out var g) ? g : null, r.Position), c++);
            Put(cells, Figure12(r.Interval ?? PitBox.SimRacing.Race.RaceFormat.Missing, lapped ? "TextFaint" : "TextMuted", mine), c++);
            if (showPoints) Put(cells, Figure12(Pts(r), "Heading", true), c);
            return RowFrame(cells, mine);
        }

        var rest = rows.Skip(PodiumPlaces).ToList();
        var firstLapped = rest.FirstOrDefault(GrrChampionship.Lapped);
        var (list, pinned) = FitRows(rest, Mine, _allResults, Row, r => ReferenceEquals(r, firstLapped) && !ReferenceEquals(r, rest[0]) ? GrrBoard.Divider("Lapped") : null);
        var footer = new List<FrameworkElement>();
        if (rows.Count > TopRows) footer.Add(Toggle(_allResults ? $"Show top {TopRows}" : $"Show all {rows.Count}", () => _allResults = !_allResults));
        footer.Add(link);
        return Card("Results", Board(top, rest.Count > 0 ? HeaderRow(widths, heads) : null, list, pinned, Footer(footer.ToArray())), picker);

        List<FrameworkElement> Marks(GrrResult r)
        {
            var marks = new List<FrameworkElement>();
            if (r.Pole) marks.Add(GrrParts.Badge("Pole", "Slate", "Pole position: started first"));
            if (r.FastestLap)
            {
                string? cls = classed ? r.Class ?? tableClass : null;
                marks.Add(GrrParts.Badge(GrrParts.FastestLapLabel(cls), "Plum", cls is null ? "Fastest lap of the race" : $"Fastest lap in {cls}"));
            }
            if (r.Penalty is double pen && pen > 0) marks.Add(GrrParts.Badge("Pen", "Danger", $"Penalty: {GrrParts.Points(pen)} points taken off"));
            if (r.Status is { } status && !status.Equals("Running", StringComparison.OrdinalIgnoreCase))
                marks.Add(GrrParts.Badge(status.Length > 12 ? status[..12] : status, "Warn", $"Finished as: {status}"));
            return marks;
        }
    }

    private int MySession(GrrLeagueData data, List<GrrSession> sessions)
    {
        if (Driver is not { } driver || data.StandingOf(driver)?.Class is not { } key) return 0;
        string mine = data.ClassLabel(key);
        int i = sessions.FindIndex(s => string.Equals(s.Label, mine, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : 0;
    }

    private static string Key(GrrEvent e) => e.Key ?? e.Date.ToString("yyyy-MM-dd");

    private void ShowResults() => Dispatcher.InvokeAsync(() =>
    {
        if (_resultsCard is { } card) card.BringIntoView(new Rect(0, -12, card.ActualWidth, 1));
    }, System.Windows.Threading.DispatcherPriority.Loaded);

    private static StackPanel Winner(string name, bool fastestLap)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, MaxWidth = 154 };
        row.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Trophy"), Width = 10, Height = 10, Foreground = Ui.Res<Brush>("AccentText"), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        if (fastestLap)
        {
            var fl = new Icon { Data = Ui.Res<Geometry>("I.Clock"), Width = 10, Height = 10, Foreground = Ui.Res<Brush>("PlumDeep"), Margin = new Thickness(-1, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(fl, "Fastest lap");
            row.Children.Add(fl);
        }
        row.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
        return row;
    }

    internal IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        var followed = _app.Config.Grr.Leagues.ToList();
        Task Pick(string league, Action? also = null)
        {
            _app.Config.Grr.DashboardLeague = league;
            also?.Invoke();
            _shape = "";
            Build();
            return Task.CompletedTask;
        }
        yield return ("grr-gt", () => Pick("gt"));
        yield return ("grr-gt-results", () => { _resultsCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("grr-gt-schedule", () => { _scheduleCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("grr-cup", () => Pick("cup"));
        yield return ("grr-all", () => Pick("cup", () => { _allStandings = _fullSeason = _allResults = true; }));
        yield return ("grr-indycar", () => Pick("indycar", () => { _allStandings = _fullSeason = _allResults = false; }));
        yield return ("grr-cup-only", () => Pick("gt", () => _app.Config.Grr.Leagues = new() { "cup" }));
        yield return ("grr-none", () => Pick("gt", () => _app.Config.Grr.Leagues = new()));
        yield return ("grr-followed-again", () => Pick("gt", () => _app.Config.Grr.Leagues = followed));
    }
}
