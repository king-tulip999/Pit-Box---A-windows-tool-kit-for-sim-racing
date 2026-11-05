using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Services;
using PitBox.SimRacing.Setups;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed class SetupManagerSection : StackPanel
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly SetupStore _store;
    private readonly Border _intro = new(), _inbox = new(), _explorer = new();
    private readonly Button _downloads = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _class = new() { MinWidth = 150, Margin = Ui.AfterButton };
    public FrameworkElement TitleTools { get; }
    private readonly Button _clearFilters = new() { Content = "Clear filters", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private Button _show = null!;
    private Tile _split = null!;
    private ScrollViewer? _page;
    private readonly StackPanel _carList = new();
    private readonly ScrollViewer _carScroll;
    private readonly TextBox _search;
    private readonly Button _sort;
    private readonly List<(Button Button, SetupNode Folder)> _crumbs = new();
    private ScrollViewer? _treeScroll;

    private int _view;
    private bool _hasSetups, _newOnly;
    private readonly HashSet<string> _classFilter;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _target = new(StringComparer.OrdinalIgnoreCase);
    private string? _anchor, _lastClick;
    private DateTime _lastClickAt;
    private bool _loaded;

    public SetupManagerSection(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _classFilter = new HashSet<string>(S.ClassFilter, StringComparer.OrdinalIgnoreCase);
        _store = new SetupStore(S, app.Demo);
        _store.Changed += () => { Prune(); RenderAll(); };

        Children.Add(_intro);
        TitleTools = BuildTitleTools();

        var card = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(13) };
        var stack = new StackPanel();
        card.Child = stack;
        Children.Add(card);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search = Ui.Search(new TextBox { Tag = "Search cars, folders or setups…", MinWidth = 178 });
        _search.TextChanged += (_, _) => { RenderFilters(); RenderCars(); RenderExplorer(); };
        top.Children.Add(_search);
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sortRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _class.Style = Ui.Res<Style>("MenuButton");
        _class.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _class.Click += (_, _) => OpenClassMenu();
        sortRow.Children.Add(_class);
        _show = new Button { Style = Ui.Res<Style>("MenuButton"), MinWidth = 162, Margin = Ui.AfterButton, Padding = new Thickness(10, 2, 9, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _show.Click += (_, _) => OpenShowMenu();
        sortRow.Children.Add(_show);
        _sort = new Button { Style = Ui.Res<Style>("MenuButton"), MinWidth = 162 };
        _sort.Click += (_, _) => OpenSortMenu();
        Ui.Anchor(_sort, "simracing.setups.sort");
        UpdateSortButton();
        sortRow.Children.Add(_sort);
        _clearFilters.Click += (_, _) => { _search.Text = ""; _view = 0; _hasSetups = _newOnly = false; SetClassFilter(() => _classFilter.Clear()); RenderAll(); };
        sortRow.Children.Add(_clearFilters);
        Grid.SetColumn(sortRow, 1);
        top.Children.Add(sortRow);
        top.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged || e.NewSize.Width <= 0) return;
            sortRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bool below = e.NewSize.Width - sortRow.DesiredSize.Width < SearchMinWidth;
            if (Grid.GetRow(sortRow) == (below ? 1 : 0)) return;
            Grid.SetRow(sortRow, below ? 1 : 0);
            Grid.SetColumn(sortRow, below ? 0 : 1);
            Grid.SetColumnSpan(sortRow, below ? 2 : 1);
            sortRow.Margin = below ? new Thickness(0, 8, 0, 0) : new Thickness(10, 0, 0, 0);
            Grid.SetColumnSpan(_search, below ? 2 : 1);
        };
        stack.Children.Add(top);

        _inbox.Margin = new Thickness(0, 10, 0, 0);
        _inbox.Visibility = Visibility.Collapsed;
        stack.Children.Add(_inbox);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CarListWidth) });
        split.ColumnDefinitions.Add(new ColumnDefinition());
        _carScroll = new ScrollViewer { Content = _carList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        var carColumn = Hairline.Frame(new Border { Child = _carScroll, Padding = new Thickness(0, 0, 10, 0), BorderBrush = Ui.Res<Brush>("Line") }, Sides.Right);
        split.Children.Add(carColumn);
        _explorer.Margin = new Thickness(13, 0, 0, 0);
        Grid.SetColumn(_explorer, 1);
        split.Children.Add(_explorer);
        _split = Pane(split);
        _split.Margin = new Thickness(0, 11, 0, 0);
        _split.Height = 616;
        stack.Children.Add(_split);
        _split.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(FitSplit, System.Windows.Threading.DispatcherPriority.Loaded);
        Loaded += (_, _) => AttachPage();
        _inbox.SizeChanged += (_, e) => { if (e.HeightChanged) FitSplit(); };
        _intro.SizeChanged += (_, e) => { if (e.HeightChanged) FitSplit(); };
        _inbox.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(FitSplit, System.Windows.Threading.DispatcherPriority.Loaded);
        _intro.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(FitSplit, System.Windows.Threading.DispatcherPriority.Loaded);
        top.SizeChanged += (_, e) => { if (e.HeightChanged) Dispatcher.BeginInvoke(FitSplit, System.Windows.Threading.DispatcherPriority.Loaded); };
        Unloaded += (_, _) => { if (_page is not null) _page.SizeChanged -= OnPageSize; _page = null; };

        Focusable = true;
        FocusVisualStyle = null;
        PreviewKeyDown += OnKey;
    }

    private SetupManagerSettings S => _app.Config.SimRacing.Setups;

    private const double SplitMin = 380, SplitMax = 3000, PageBottom = 23, CarListWidth = 300, SearchMinWidth = 210, TreeMinHeight = 150;

    private void AttachPage()
    {
        DependencyObject? p = this;
        while (p is not null && p is not ScrollViewer) p = VisualTreeHelper.GetParent(p);
        if (p is not ScrollViewer page || page == _page) return;
        if (_page is not null) _page.SizeChanged -= OnPageSize;
        _page = page;
        _page.SizeChanged += OnPageSize;
        Dispatcher.BeginInvoke(FitSplit, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnPageSize(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged) FitSplit();
    }

    private void FitSplit()
    {
        if (_page is null || !_split.IsVisible || _page.Content is not UIElement content) return;
        double top = _split.TranslatePoint(new Point(0, 0), content).Y;
        double cardBottom = 13 + PageBottom;
        double want = Math.Clamp(_page.ViewportHeight - top - cardBottom, SplitMin, SplitMax);
        if (Math.Abs(want - _split.Height) > 1) _split.Height = Math.Round(want);
    }

    private static Tile Pane(UIElement child)
    {
        var pane = Hairline.Frame(new Tile { Style = Ui.Res<Style>("Inset"), Child = child });
        pane.SetResourceReference(Border.BorderBrushProperty, "InputEdge");
        return pane;
    }

    public void OnShown()
    {
        _shown = true;
        if (_loaded) _store.Watch(true);
        _ = LoadAsync();
        if (!_loaded) RenderAll();
    }

    public void OnHidden()
    {
        _shown = false;
        _store.Watch(false);
    }

    private bool _shown;

    private async Task LoadAsync()
    {
        bool changed = await _store.LoadAsync();
        if (!_shown) return;
        bool first = !_loaded;
        _loaded = true;
        if (first) _store.Watch(true);
        if (!changed && !first) return;
        Prune();
        RenderAll();
    }

    private void Prune()
    {
        _selected.RemoveWhere(k => _store.Find(k) is null);
        _open.RemoveWhere(k => _store.Find(k) is null);
        foreach (var dir in _target.Keys.ToList())
            if (_store.Find(_target[dir]) is not { IsFolder: true }) _target.Remove(dir);
    }

    private void Save() => _app.Save();

    private void RenderAll()
    {
        RenderIntro();
        RenderInbox();
        _classFilter.RemoveWhere(cls => !_store.Cars.Any(c => c.Class == cls));
        RenderFilters();
        UpdateClassButton();
        RenderCars();
        RenderExplorer();
    }

    private void RenderIntro()
    {
        if (S.IntroSeen) { _intro.Child = null; _intro.Visibility = Visibility.Collapsed; return; }
        _intro.Visibility = Visibility.Visible;
        _intro.Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);
        string detail = string.Join("\n", new[]
        {
            "• Pick a car on the left to see its setup folder. Search finds cars, folders and setup names.",
            "• Rename, move, import and share here: the changes happen to the real files in Documents\\iRacing\\setups, so iRacing sees them right away.",
            "• Deletes go to the Recycle Bin. Names, classes and orders you change here are only how Pit-Box shows things; iRacing's folders keep their names.",
        });
        _intro.Child = Ui.Notice("Accent", "I.Info", "Your iRacing setups, without File Explorer", detail, ("Got it", () => { S.IntroSeen = true; Save(); RenderIntro(); }));
    }

    private FrameworkElement BuildTitleTools()
    {
        _downloads.Click += (_, _) =>
        {
            S.InboxFolded = !S.InboxFolded;
            Save();
            RenderInbox();
        };
        System.Windows.Automation.AutomationProperties.SetName(_downloads, "Downloads");
        var scan = Ui.IconButton("I.Refresh", "Scan Downloads again", () =>
        {
            _store.ScanInbox();
            S.InboxFolded = false;
            Save();
            RenderInbox();
            _app.Notify("Downloads scanned", _store.Inbox.Count == 0 ? "No new setups or share packs found." : $"{SetupTree.Plural(_store.Inbox.Count, "setup file")} ready to file.", NoticeKind.Info);
        });
        scan.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
        scan.VerticalAlignment = VerticalAlignment.Center;
        scan.ToolTipOpening += (_, _) => scan.ToolTip = TextCase.PathTip("Scan " + (_store.Downloads ?? "Downloads") + " again");
        RenderDownloadsButton();
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _downloads, scan } };
    }

    private void RenderDownloadsButton()
    {
        int count = _loaded ? _store.Inbox.Count : 0;
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        label.Inlines.Add(new System.Windows.Documents.Run("Downloads"));
        if (count > 0)
        {
            var fresh = new System.Windows.Documents.Run($" {count} new");
            fresh.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentText");
            label.Inlines.Add(fresh);
        }
        _downloads.Content = label;
        _downloads.IsEnabled = _loaded;
        Ui.SetToggled(_downloads, _loaded && !S.InboxFolded);
        _downloads.ToolTip = !_loaded ? null
            : count > 0 ? $"{SetupTree.Plural(count, "new setup file")} found, ready to move into iRacing"
            : "No new setups or share packs found";
    }

    private void RenderInbox()
    {
        RenderDownloadsButton();
        var items = _store.Inbox;
        if (!_loaded || S.InboxFolded) { _inbox.Child = null; _inbox.Visibility = Visibility.Collapsed; return; }
        _inbox.Visibility = Visibility.Visible;
        var stack = new StackPanel();
        var head = new DockPanel();
        var close = Ui.IconButton("I.X", "Hide the files found", () => { S.InboxFolded = true; Save(); RenderInbox(); }, "QuietIconButton");
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock
        {
            Text = "Pit-Box watches your Downloads folder for iRacing setups (.sto) and share packs (.zip) from league mates or setup shops. Each one is listed with the car it looks like it's for. File it moves the file into that car's iRacing setups folder, so it shows up in the iRacing garage. Change the car if the guess is wrong, or press × to leave the file where it is.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 5), VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(head);
        if (items.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Text = _store.Downloads is { } folder && Directory.Exists(folder)
                    ? $"Nothing from the last {SetupStore.InboxDays} days in {folder}. New downloads show up here on their own."
                    : "Your Downloads folder wasn't found.",
                Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            });
        var choices = _store.Cars.OrderBy(c => c.Name, Natural.Comparer).Select(c => new Choice(c.Dir, c.Name)).Prepend(new Choice(null, "Choose the car…")).ToList();
        foreach (var item in items.ToList())
            stack.Children.Add(InboxRow(item, choices));
        int ready = items.Count(i => i.Car is not null);
        if (ready > 1)
        {
            var all = Ui.LabeledButton("I.Check", $"File all {ready} matched", () => FileDownloads(items.Where(i => i.Car is not null).ToList()));
            all.HorizontalAlignment = HorizontalAlignment.Right;
            all.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(all);
        }
        var pane = Pane(stack);
        pane.Padding = new Thickness(11, 8, 6, 8);
        _inbox.Child = pane;
    }

    private FrameworkElement InboxRow(DownloadItem item, List<Choice> choices)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(211) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Icon { Data = Ui.Res<Geometry>(item.IsPack ? "I.Layers" : "I.FileText"), Width = 13, Height = 13, Foreground = Ui.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        var name = new StackPanel { Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(TextCase.Verbatim(new TextBlock { Text = item.Name, Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = TextCase.PathTip(item.Path) }));
        string what = item.IsPack ? $"Share pack · {SetupTree.Plural(item.Entries.Count, "setup")}" : SetupTree.Size(item.Size);
        name.Children.Add(new TextBlock { Text = $"{what} · {Ago(item.Modified)}", Style = Ui.Res<Style>("Caption") });
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        FrameworkElement car;
        if (item.PackCars.Count > 0)
            car = new TextBlock
            {
                Text = "→ " + string.Join(", ", item.PackCars.Select(d => _store.Car(d)?.Name ?? d)), Style = Ui.Res<Style>("Muted"),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = "The pack says which car each setup is for",
            };
        else
        {
            car = Ui.Combo(choices, item.Car, c => { item.Car = c.Id; RenderInbox(); }, 194);
            car.ToolTip = item.Car is null
                ? "The file name doesn't say clearly which car this is for. Choose it here."
                : "Picked from the file name when it says clearly which car. Change it if it isn't right.";
        }
        Grid.SetColumn(car, 2);
        grid.Children.Add(car);
        var file = Ui.LabeledButton("I.Check", "File it", () => FileDownloads(new List<DownloadItem> { item }));
        file.IsEnabled = item.Car is not null;
        file.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(file, 3);
        grid.Children.Add(file);
        var ignore = Ui.IconButton("I.X", "Leave it in Downloads (not offered again)", () => { _store.Ignore(item); Save(); RenderInbox(); }, "QuietIconButton");
        ignore.Margin = new Thickness(3, 0, 0, 0);
        Grid.SetColumn(ignore, 4);
        grid.Children.Add(ignore);
        return grid;
    }

    private void FileDownloads(List<DownloadItem> items)
    {
        var added = new List<SetupNode>();
        foreach (var item in items)
        {
            var (nodes, error) = _store.FileDownload(item);
            added.AddRange(nodes);
            if (error is not null) _app.Notify("Couldn't file " + item.Name, error, NoticeKind.Warning);
        }
        Save();
        if (added.Count > 0)
        {
            Reveal(added);
            _app.Notify("Setups filed", $"{SetupTree.Plural(added.Count, "setup")} moved into iRacing's setups folder.", NoticeKind.Success);
        }
        RenderAll();
    }

    private static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        return span.TotalHours < 1 ? "just now" : span.TotalHours < 24 ? $"{(int)span.TotalHours} h ago" : $"{(int)span.TotalDays} d ago";
    }

    private void RenderFilters()
    {
        string cars = _view switch { 1 => "Favorites", 2 => "Hidden cars", _ => "All cars" };
        string setups = _newOnly ? " · New setups" : _hasSetups ? " · Has setups" : "";
        var row = Ui.MenuFace(TextFit.TipWhenTrimmed(new TextBlock { Text = $"Show: {cars}{setups}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
        _show.Content = row;
        _show.ToolTip = "Which cars are listed: all, favorites or hidden ones, and whether they have setups";
        _show.Style = Ui.Res<Style>(_view != 0 || _hasSetups || _newOnly ? "SelectedButton" : "MenuButton");
        _clearFilters.Style = Ui.Res<Style>("GhostButton");
        _clearFilters.Visibility = Filtering ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenShowMenu()
    {
        var listed = _store.Cars.Where(c => !S.IsClassHidden(c.Class)).ToList();
        int all = listed.Count(c => !Hidden(c)), fav = listed.Count(c => Fav(c) && !Hidden(c)), hidden = listed.Count(Hidden);
        var menu = new ContextMenu { PlacementTarget = _show, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 211 };
        Ui.DropBelow(menu);
        void Choice(string header, string tip, bool on, Action pick)
        {
            var item = new MenuItem { Header = header, ToolTip = tip, IsCheckable = true, IsChecked = on };
            item.Click += (_, _) => { pick(); RenderFilters(); RenderCars(); RenderExplorer(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuItem { Header = "Cars", IsEnabled = false });
        Choice($"All cars ({all})", "Every car except hidden ones", _view == 0, () => _view = 0);
        Choice($"Favorites ({fav})", "Cars you starred", _view == 1, () => _view = 1);
        Choice($"Hidden cars ({hidden})", "Cars you hid from this list. Their files are untouched.", _view == 2, () => _view = 2);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Setups", IsEnabled = false });
        Choice("Any car", "Cars with or without setups", !_hasSetups && !_newOnly, () => _hasSetups = _newOnly = false);
        Choice("Has setups", "Only cars with at least one setup", _hasSetups && !_newOnly, () => { _hasSetups = true; _newOnly = false; });
        Choice("New setups", "Cars with setups changed in the last 3 days", _newOnly, () => { _hasSetups = false; _newOnly = true; });
        menu.IsOpen = true;
    }

    private void SetClassFilter(Action change)
    {
        change();
        S.ClassFilter = _classFilter.ToList();
        Save();
    }

    private bool Filtering => _search.Text.Trim().Length > 0 || _view != 0 || _hasSetups || _newOnly || _classFilter.Count > 0;

    private List<string> ClassOrder => SetupCars.Order(S.ClassOrder);

    private void UpdateClassButton()
    {
        UpdateSortButton();
        var picked = ClassOrder.Where(_classFilter.Contains).ToList();
        string label = picked.Count == 0 ? "All" : picked.Count <= 2 ? string.Join(", ", picked) : PitBox.SimRacing.Race.RaceFormat.Count(picked.Count, "class");
        _class.Content = Ui.MenuFace(TextFit.TipWhenTrimmed(new TextBlock { Text = "Class: " + label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
        _class.Style = Ui.Res<Style>(picked.Count > 0 ? "SelectedButton" : "MenuButton");
        _class.ToolTip = "Which classes are listed. Pick more than one to add them; drag a class to change the class order.";
        System.Windows.Automation.AutomationProperties.SetName(_class, "Class filter");
    }

    private void OpenClassMenu()
    {
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = _class, StaysOpen = false, AllowsTransparency = true, UseLayoutRounding = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
        };
        Ui.DropBelow(popup);
        Ui.FollowTarget(popup);
        var host = new StackPanel { MinWidth = Math.Max(211, _class.ActualWidth) };
        FillClassMenu(host);
        popup.Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(4), Margin = new Thickness(0, 3, 6, 13), Child = host };
        popup.IsOpen = true;
    }

    private void ClassFilterChanged()
    {
        UpdateClassButton();
        RenderFilters(); RenderCars(); RenderExplorer();
    }

    private FrameworkElement ClassMenuRow(UIElement content, string? tip, Action click)
    {
        var row = new Tile { Height = 28, Padding = new Thickness(8, 0, 10, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = content };
        Ui.HoverHighlight(row);
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }

    private void FillClassMenu(StackPanel host)
    {
        host.Children.Clear();
        var present = ClassOrder.Where(k => _store.Cars.Any(c => c.Class == k) && !S.IsClassHidden(k)).ToList();
        int hiddenClasses = ClassOrder.Count(k => S.IsClassHidden(k) && _store.Cars.Any(c => c.Class == k));
        bool everything = _classFilter.Count == 0 && hiddenClasses == 0;
        var allText = new TextBlock { Text = hiddenClasses > 0 ? $"All classes · {hiddenClasses} hidden" : "All classes", VerticalAlignment = VerticalAlignment.Center };
        var allRow = ClassMenuRow(ToggleCards.WithLamp(new PitBox.Controls.Lamp { On = everything }, allText),
            hiddenClasses > 0 ? $"{PitBox.SimRacing.Race.RaceFormat.Count(hiddenClasses, "class")} hidden in the Sort menu. Click to show every class again."
                : everything ? "Every class is shown" : "Every class again (clears the pick)",
            () =>
            {
                if (everything) return;
                S.HiddenClasses.Clear();
                SetClassFilter(() => _classFilter.Clear());
                ClassFilterChanged();
                FillClassMenu(host);
            });
        System.Windows.Automation.AutomationProperties.SetName(allRow, "All classes");
        host.Children.Add(allRow);
        if (present.Count == 0) return;
        var rule = Hairline.Rule(new Border { Margin = new Thickness(6, 4, 6, 4) });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        host.Children.Add(rule);
        var grid = new ReorderGrid { MinCellWidth = 180, MaxColumns = 1, Gap = 0 };
        foreach (var cls in present)
        {
            bool on = _classFilter.Contains(cls);
            var name = new TextBlock { Text = cls, VerticalAlignment = VerticalAlignment.Center };
            var row = ClassMenuRow(ToggleCards.WithLamp(new PitBox.Controls.Lamp { On = on }, name),
                on ? $"Showing {cls}. Click to stop filtering by it; drag to change the class order."
                    : $"Show only {cls} (click more classes to add them). Drag to change the class order. Hide a class in the Sort menu.",
                () =>
                {
                    SetClassFilter(() => { if (!_classFilter.Remove(cls)) _classFilter.Add(cls); });
                    ClassFilterChanged();
                    FillClassMenu(host);
                });
            row.Tag = cls;
            grid.Children.Add(row);
        }
        grid.Moved += (_, _) =>
        {
            var shown = grid.Children.OfType<FrameworkElement>().Select(e => (string)e.Tag).ToList();
            S.ClassOrder = SetupCars.Reorder(ClassOrder, shown);
            S.Sort = "class";
            Save();
            UpdateClassButton();
            RenderCars();
        };
        host.Children.Add(grid);
    }

    private static readonly (string Id, string Label)[] SortChoices =
    {
        ("class", "Class"), ("name", "Name A → Z"), ("name-desc", "Name Z → A"), ("count", "Most setups"), ("recent", "Recently changed"),
    };

    private void UpdateSortButton()
    {
        string label = SortChoices.FirstOrDefault(c => c.Id == S.Sort).Label ?? "Class";
        int hidden = S.HiddenClasses.Count;
        var row = Ui.MenuFace(TextFit.TipWhenTrimmed(new TextBlock { Text = $"Sort: {label}" + (hidden > 0 ? $" · {hidden} class{(hidden == 1 ? "" : "es")} hidden" : ""), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
        _sort.Content = row;
        _sort.Style = Ui.Res<Style>(hidden > 0 ? "SelectedButton" : "MenuButton");
        _sort.ToolTip = "How the cars are ordered, and which classes are listed (a switch per class)";
    }

    private void OpenSortMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _sort, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 211 };
        Ui.DropBelow(menu);
        menu.Items.Add(new MenuItem { Header = "Sort by", IsEnabled = false });
        foreach (var (id, label) in SortChoices)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = S.Sort == id };
            item.Click += (_, _) =>
            {
                S.Sort = id;
                Save();
                UpdateSortButton();
                RenderCars();
            };
            menu.Items.Add(item);
        }
        var reset = new MenuItem { Header = "Reset the class order", IsEnabled = S.ClassOrder.Count > 0, ToolTip = "Back to the default order of the classes (after dragging them)" };
        reset.Click += (_, _) =>
        {
            S.ClassOrder.Clear();
            S.Sort = "class";
            Save();
            UpdateSortButton();
            UpdateClassButton();
            RenderCars();
            _app.Notify("Class order", "The classes are back in their default order.", NoticeKind.Info);
        };
        menu.Items.Add(reset);

        var present = ClassOrder.Where(k => _store.Cars.Any(c => c.Class == k)).ToList();
        if (present.Count > 0)
        {
            menu.Items.Add(new Separator());
            var classes = new MenuItem { Header = "Classes in the list" };
            menu.Items.Add(classes);
            MenuItem? all = null;
            foreach (var cls in present)
            {
                bool shown = !S.IsClassHidden(cls);
                var lamp = new PitBox.Controls.Lamp { On = shown };
                var label = new TextBlock { Text = cls, VerticalAlignment = VerticalAlignment.Center, Opacity = shown ? 1 : ToggleCards.OffOpacity };
                var line = ToggleCards.WithLamp(lamp, label);
                line.MinWidth = 162;
                var item = new MenuItem { Header = line, StaysOpenOnClick = true, ToolTip = shown ? $"Hide {cls} and its cars from the list (files untouched)" : $"List {cls} again" };
                item.Click += (_, _) =>
                {
                    bool hide = !S.IsClassHidden(cls);
                    S.SetClassHidden(cls, hide);
                    if (hide) _classFilter.Remove(cls);
                    S.ClassFilter = _classFilter.ToList();
                    Save();
                    lamp.On = !hide;
                    label.Opacity = hide ? ToggleCards.OffOpacity : 1;
                    item.ToolTip = hide ? $"List {cls} again" : $"Hide {cls} and its cars from the list (files untouched)";
                    UpdateSortButton();
                    if (all is not null) all.IsEnabled = S.HiddenClasses.Count > 0;
                    RenderFilters(); UpdateClassButton(); RenderCars(); RenderExplorer();
                };
                classes.Items.Add(item);
            }
            classes.Items.Add(new Separator());
            all = new MenuItem { Header = "Show every class", IsEnabled = S.HiddenClasses.Count > 0 };
            all.Click += (_, _) =>
            {
                S.HiddenClasses.Clear();
                Save();
                UpdateSortButton();
                RenderFilters(); UpdateClassButton(); RenderCars(); RenderExplorer();
                menu.IsOpen = false;
            };
            classes.Items.Add(all);
        }
        menu.IsOpen = true;
    }

    private bool Fav(SetupCar car) => SetupManagerSettings.Has(S.Favorites, car.Dir);
    private bool Hidden(SetupCar car) => SetupManagerSettings.Has(S.HiddenCars, car.Dir);

    private SetupSearch? Search(SetupCar car) => SetupSearch.For(car, _search.Text);

    private List<SetupCar> VisibleCars()
    {
        var since = _store.NewSince;
        var cars = _store.Cars.Where(c =>
        {
            if (S.IsClassHidden(c.Class)) return false;
            if (_view == 2 ? !Hidden(c) : Hidden(c)) return false;
            if (_view == 1 && !Fav(c)) return false;
            if (_classFilter.Count > 0 && !_classFilter.Contains(c.Class)) return false;
            if (_hasSetups && c.Root.Files == 0) return false;
            if (_newOnly && c.Root.NewCount(since) == 0) return false;
            return Search(c) is not { } m || m.Matches;
        });
        return SetupCars.Sorted(cars, S.Sort, ClassOrder).ToList();
    }

    private SetupCar? SelectedCar(List<SetupCar> visible)
    {
        var car = _store.Car(S.SelectedCar);
        if (car is null || !visible.Contains(car)) car = visible.FirstOrDefault();
        return car;
    }

    private void RenderCars()
    {
        double offset = _carScroll.VerticalOffset;
        _carList.Children.Clear();
        var visible = VisibleCars();
        var selected = SelectedCar(visible);
        if (!_loaded)
        {
            _carList.Children.Add(Empty("Reading your setups…", "Documents\\iRacing\\setups"));
            return;
        }
        if (_store.Root is null && !_store.Demo)
        {
            _carList.Children.Add(Empty("iRacing's setups folder wasn't found", "Pit-Box looks in Documents\\iRacing\\setups. Start iRacing once and save a setup in the garage, then come back."));
            return;
        }
        if (visible.Count == 0)
        {
            _carList.Children.Add(Empty(_store.Cars.Count == 0 ? "No cars yet" : "No cars match", _store.Cars.Count == 0
                ? "iRacing makes a folder for a car the first time you save a setup for it in the garage."
                : _view == 2 ? "No hidden cars." : S.HiddenClasses.Count > 0 && _store.Cars.All(c => S.IsClassHidden(c.Class))
                    ? "Every class is hidden. All classes in the Class menu brings them back."
                    : "Try another search or class, or clear the filters."));
            return;
        }
        var since = _store.NewSince;
        if (S.Sort == "class")
            foreach (var group in visible.GroupBy(c => c.Class))
                _carList.Children.Add(ClassGroup(group.Key, group.ToList(), selected, since));
        else
            _carList.Children.Add(Rows(visible, selected, since, chips: true));
        _carScroll.ScrollToVerticalOffset(offset);
    }

    private FrameworkElement ClassGroup(string cls, List<SetupCar> cars, SetupCar? selected, DateTime since)
    {
        var r = ThemeManager.Round(7);
        var head = Hairline.Frame(new Border
        {
            Background = Ui.Res<Brush>("SurfaceAlt"), Padding = new Thickness(8, 4, 8, 4), BorderBrush = Ui.Res<Brush>("Line"),
            CornerRadius = new CornerRadius(r.TopLeft, r.TopRight, 0, 0),
        }, Sides.Bottom);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = cls, Style = Ui.Res<Style>("Eyebrow"), Foreground = Ui.Res<Brush>("Heading"), FontWeight = FontWeights.Bold });
        title.Children.Add(new TextBlock { Text = "  " + SetupTree.Plural(cars.Count, "car"), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        head.Child = title;
        var body = new StackPanel();
        body.Children.Add(head);
        var rows = Rows(cars, selected, since, chips: false);
        body.Children.Add(rows);
        return Hairline.Frame(new Tile
        {
            Child = body, CornerRadius = r,
            Background = Ui.Res<Brush>("Surface"), BorderBrush = Ui.Res<Brush>("Line"),
            Margin = new Thickness(0, _carList.Children.Count == 0 ? 0 : 8, 0, 0),
        });
    }

    private StackPanel Rows(List<SetupCar> cars, SetupCar? selected, DateTime since, bool chips)
    {
        var stack = new StackPanel();
        for (int i = 0; i < cars.Count; i++)
        {
            if (i > 0)
                stack.Children.Add(Hairline.Rule(new Border
                {
                    Margin = new Thickness(29, 0, 6, 0), Background = Ui.Res<Brush>("Line"),
                    Opacity = cars[i] == selected || cars[i - 1] == selected ? 0 : 0.7,
                }));
            stack.Children.Add(CarRow(cars[i], cars[i] == selected, since, chips));
        }
        return stack;
    }

    private const double CarRowHeight = 30;

    private FrameworkElement CarRow(SetupCar car, bool selected, DateTime since, bool chip)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool fav = Fav(car), hidden = Hidden(car);
        var star = Ui.IconButton("I.Star", fav ? "Remove from favorites" : "Add to favorites", () =>
        {
            SetupManagerSettings.Set(S.Favorites, car.Dir, !fav);
            Save();
            RenderFilters();
            RenderCars();
        }, "QuietIconButton");
        if (fav && star.Content is Icon starIcon) { starIcon.Foreground = Ui.Res<Brush>("Accent"); starIcon.Fill = Ui.Res<Brush>("Accent"); }
        star.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(star);

        bool dim = car.Root.Files == 0 && !selected;
        var name = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = car.Name, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 6, 0), Opacity = dim ? 0.55 : 1,
        });
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var tail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!car.Known) tail.Children.Add(Small("folder name", "TextFaint"));
        if (Search(car) is { Hits.Count: > 0 } m) tail.Children.Add(Small(SetupTree.Plural(m.Hits.Count, "match"), "AccentText"));
        int fresh = car.Root.NewCount(since);
        if (fresh > 0) tail.Children.Add(Small($"{fresh} new", "AccentText"));
        if (chip)
        {
            var tag = Ui.Chip(car.Class);
            tag.Padding = new Thickness(5, 0, 5, 0);
            tag.Height = 20;
            tag.Margin = new Thickness(6, 0, 0, 0);
            tail.Children.Add(tag);
        }
        var count = Small(car.Root.Files.ToString(System.Globalization.CultureInfo.CurrentCulture));
        count.Margin = new Thickness(8, 0, 0, 0);
        count.ToolTip = SetupTree.Plural(car.Root.Files, "setup");
        if (dim) count.Opacity = 0.55;
        tail.Children.Add(count);
        Grid.SetColumn(tail, 2);
        grid.Children.Add(tail);

        var hide = Ui.IconButton(hidden ? "I.Eye" : "I.EyeOff", hidden ? "Show this car again" : "Hide this car from the list", () =>
        {
            SetupManagerSettings.Set(S.HiddenCars, car.Dir, !hidden);
            Save();
            RenderFilters();
            RenderCars();
            RenderExplorer();
        }, "QuietIconButton");
        hide.VerticalAlignment = VerticalAlignment.Center;
        hide.Opacity = hidden ? 1 : 0;
        Grid.SetColumn(hide, 3);
        grid.Children.Add(hide);

        var row = new Tile
        {
            Child = grid, CornerRadius = ThemeManager.Round(6), Cursor = Cursors.Hand, Padding = new Thickness(2, 0, 2, 0), Height = CarRowHeight,
            Background = selected ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = selected ? Ui.Res<Brush>("Accent") : Brushes.Transparent,
        };
        row.MouseEnter += (_, _) => { hide.Opacity = 1; if (!selected) row.Background = Ui.Res<Brush>("Hover"); };
        row.MouseLeave += (_, _) => { hide.Opacity = hidden ? 1 : 0; if (!selected) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInButton(d)) return;
            if (S.SelectedCar != car.Dir) { S.SelectedCar = car.Dir; _selected.Clear(); Save(); }
            RenderCars();
            RenderExplorer();
        };
        return row;
    }

    private static bool IsInButton(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private static TextBlock Small(string text, string brush = "TextMuted") =>
        new() { Text = text, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>(brush), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private static FrameworkElement Empty(string title, string text)
    {
        var stack = new StackPanel { Margin = new Thickness(13, 23, 13, 23) };
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        return stack;
    }

    private SetupNode Target(SetupCar car) =>
        _target.TryGetValue(car.Dir, out var key) && car.Root.Find(key) is { IsFolder: true } folder ? folder : car.Root;

    private List<SetupNode> SelectedNodes(SetupCar car) =>
        _selected.Select(k => car.Root.Find(k)).OfType<SetupNode>().ToList();

    private void RenderExplorer()
    {
        _crumbs.Clear();
        double offset = _treeScroll?.VerticalOffset ?? 0;
        var car = _loaded ? SelectedCar(VisibleCars()) : null;
        if (car is null)
        {
            _explorer.Child = Pane(Empty(!_loaded ? "Reading your setups…" : _store.Cars.Count == 0 ? "Nothing to show yet" : "Pick a car", "Its setups and folders show here."));
            return;
        }
        var target = Target(car);
        var sel = SelectedNodes(car);
        var search = Search(car);
        var stack = new DockPanel();

        var head = CarHead(car, target, sel);
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);

        if (!target.IsRoot)
        {
            var path = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var chain = new List<SetupNode>();
            for (var n = target; n is not null; n = n.Parent) chain.Insert(0, n);
            for (int i = 0; i < chain.Count; i++)
            {
                var folder = chain[i];
                if (i > 0) path.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.ChevronRight"), Width = 10, Height = 10, Foreground = Ui.Res<Brush>("TextFaint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
                var crumb = new Button
                {
                    Style = Ui.Res<Style>("QuietButton"), Padding = new Thickness(5, 2, 5, 2), BorderThickness = Hairline.Edges(),
                    Content = i == 0 ? new Icon { Data = Ui.Res<Geometry>("I.Folder"), Width = 11, Height = 11, Foreground = Ui.Res<Brush>("GoldDeep") } : (object)folder.Name,
                    ToolTip = i == 0 ? "Back to the top of " + car.Name : null,
                };
                if (i == chain.Count - 1 && i > 0) crumb.Foreground = Ui.Res<Brush>("AccentText");
                crumb.Click += (_, _) => { _target[car.Dir] = folder.Key; _selected.Clear(); RenderExplorer(); };
                _crumbs.Add((crumb, folder));
                path.Children.Add(crumb);
            }
            DockPanel.SetDock(path, Dock.Top);
            stack.Children.Add(path);
        }

        if (sel.Count == 0)
        {
            var summary = FolderSummary(target);
            DockPanel.SetDock(summary, Dock.Top);
            stack.Children.Add(summary);
        }

        var body = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(202) });
        _treeScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(3, 3, 3, 10) };
        var tree = new SetupTree(_treeScroll) { Focusable = true, FocusVisualStyle = null };
        _treeScroll.Content = tree;
        var items = TreeItems(car, search, target, new HashSet<SetupNode>(sel), _store.NewSince).ToList();
        if (items.Count == 0)
            _treeScroll.Content = Empty(search is { CarHit: false } ? "No matching setups" : "No setups for this car yet",
                search is { CarHit: false } ? "Try another search." : "Import .sto files or a share pack, or save a setup in the iRacing garage.");
        else
            tree.Show(items);
        tree.RowClicked += (node, e) => Click(car, node);
        tree.Toggled += node => { if (!_open.Remove(node.Key)) _open.Add(node.Key); RenderExplorer(); };
        tree.Checked += node => { if (!_selected.Remove(node.Key)) _selected.Add(node.Key); _anchor = node.Key; RenderExplorer(); };
        tree.DragSet = node => _selected.Contains(node.Key) ? SelectedNodes(car) : new List<SetupNode> { node };
        tree.OutsideTarget = e =>
        {
            foreach (var (button, folder) in _crumbs)
            {
                var p = e.GetPosition(button);
                if (p.X >= 0 && p.Y >= 0 && p.X <= button.ActualWidth && p.Y <= button.ActualHeight) return (folder, button);
            }
            return null;
        };
        tree.Dropped += (nodes, into, parent, order) => Dropped(nodes, into, parent, order);
        var treePane = Pane(_treeScroll);
        body.Children.Add(treePane);
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (sel.Count == 0)
        {
            Grid.SetColumnSpan(treePane, 2);
            body.ColumnDefinitions[1].Width = new GridLength(0);
            stack.Children.Add(body);
            _explorer.Child = stack;
            _treeScroll.ScrollToVerticalOffset(offset);
            return;
        }
        var details = new ScrollViewer { Content = Details(car, sel, target), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        body.Children.Add(details);
        void Place()
        {
            bool under = body.ActualWidth is > 0 and < 620;
            Grid.SetRow(details, under ? 1 : 0);
            Grid.SetColumn(details, under ? 0 : 1);
            Grid.SetColumnSpan(details, under ? 2 : 1);
            body.ColumnDefinitions[1].Width = under ? new GridLength(0) : new GridLength(202);
            details.Margin = under ? new Thickness(0, 10, 0, 0) : new Thickness(11, 0, 0, 0);
            details.VerticalScrollBarVisibility = under ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            body.RowDefinitions[0].MinHeight = under ? TreeMinHeight : 0;
            if (under && body.ActualHeight > 0) details.MaxHeight = Math.Max(0, body.ActualHeight - TreeMinHeight - details.Margin.Top);
            else details.ClearValue(MaxHeightProperty);
        }
        Place();
        body.SizeChanged += (_, e) => Place();
        stack.Children.Add(body);

        _explorer.Child = stack;
        _treeScroll.ScrollToVerticalOffset(offset);
    }

    private const double CarHeadHeight = 48, CarHeadPicture = 34, CarHeadMinName = 200, ActionLineGap = 6;

    private FrameworkElement CarHead(SetupCar car, SetupNode target, List<SetupNode> sel)
    {
        var name = RaceParts.Big(car.Name, 21, "Heading", heavy: false);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        TextFit.TipWhenTrimmed(name);
        var tag = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 1, 0, 0) };
        tag.Children.Add(new Slant("Accent", 0.12, 0, 1, 0, 0.88, 1, 0, 1));
        var tagText = RaceParts.Big(car.Class, 12, "OnAccent", heavy: false);
        tagText.Margin = new Thickness(11, 1, 11, 2);
        tag.Children.Add(tagText);
        var counts = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 1, 0, 0) };
        counts.Inlines.Add(new System.Windows.Documents.Run(SetupTree.Plural(car.Root.Files, "setup") + " · " + SetupTree.Plural(car.Root.Folders, "folder")));
        int fresh = car.Root.NewCount(_store.NewSince);
        if (fresh > 0)
        {
            var news = new System.Windows.Documents.Run($" · {fresh} new in 3 days");
            news.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentText");
            counts.Inlines.Add(news);
        }
        var rename = Ui.IconButton("I.Pencil", "Change the name or class Pit-Box shows", () => EditCarName(car), "QuietIconButton");
        rename.VerticalAlignment = VerticalAlignment.Center;
        rename.ToolTip = $"Change the name or class Pit-Box shows (the folder on disk stays {car.Dir})";
        rename.Margin = new Thickness(2, 0, 0, 0);
        var identity = new TrimRow { VerticalAlignment = VerticalAlignment.Center, Children = { name, tag, counts, rename } };

        var lead = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
        var picture = CarPictures.Slotted(car.Dir, CarHeadPicture);
        if (picture is not null)
        {
            picture.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(picture, Dock.Left);
            lead.Children.Add(picture);
        }
        lead.Children.Add(identity);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Act(Button button, string? tip)
        {
            if (actions.Children.Count > 0) button.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
            if (tip is not null) button.ToolTip = tip;
            actions.Children.Add(button);
        }
        Button Plain(string label, Action click, string style = "GhostButton")
        {
            var b = new Button { Content = label, Style = Ui.Res<Style>(style) };
            b.Click += (_, _) => click();
            return b;
        }
        if (sel.Count == 0)
        {
            Act(Ui.LabeledButton("I.Download", "Import", () => PickImport(target), "PrimaryButton"), "Copy .sto files or a .zip share pack into " + Label(target));
            Act(Plain("Share / export", () => Export(car, new List<SetupNode> { target })), "Save a share pack for league mates, or plain copies");
            Act(Plain("New folder", () => NewFolder(target)), "Make a folder inside " + Label(target));
            Act(Plain("Open in Explorer", () => OpenFolder(target)), "Open " + Label(target) + " in File Explorer");
        }
        else
        {
            var one = sel.Count == 1 ? sel[0] : null;
            var selected = new TextBlock { Text = $"{sel.Count} selected", ToolTip = Describe(sel) + " selected", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            selected.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            actions.Children.Add(selected);
            if (one is not null) Act(Plain("Rename", () => Rename(one)), "Rename (F2)");
            Act(Plain("Move", () => PickMove(car, sel)), "Move to another folder of this car");
            var share = Ui.IconButton("I.External", $"Share / export ({sel.Count}): save a share pack for league mates, or plain copies", () => Export(car, sel));
            share.VerticalAlignment = VerticalAlignment.Center;
            Act(share, null);
            if (one is not null) Act(Plain(one.IsFolder ? "Open in Explorer" : "Show in Explorer", () => { if (one.IsFolder) OpenFolder(one); else Reveal(one); }), null);
            var delete = Plain("Delete", () => Delete(sel), "DangerButton");
            Act(delete, "Move to the Recycle Bin");
            delete.Margin = new Thickness(Ui.GroupGap, 0, 0, 0);
            var clear = Ui.IconButton("I.X", "Clear the selection (Esc)", () => { _selected.Clear(); RenderExplorer(); }, "QuietIconButton");
            clear.VerticalAlignment = VerticalAlignment.Center;
            Act(clear, null);
            clear.Margin = new Thickness(4, 0, 0, 0);
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.Children.Add(lead);
        Grid.SetColumn(actions, 1);
        actions.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(actions);
        void Fit(double width)
        {
            if (width <= 0) return;
            actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double room = width - actions.DesiredSize.Width + actions.Margin.Left - 12;
            bool under = room < CarHeadMinName;
            Grid.SetRow(actions, under ? 1 : 0);
            Grid.SetColumn(actions, under ? 0 : 1);
            actions.Margin = under ? new Thickness(0, 8, 0, -ActionLineGap) : new Thickness(12, 0, 0, 0);
            foreach (FrameworkElement child in actions.Children)
                child.Margin = new Thickness(child.Margin.Left, 0, child.Margin.Right, under ? ActionLineGap : 0);
            double avail = under ? width : room;
            if (counts.Visibility != Visibility.Visible) counts.Visibility = Visibility.Visible;
            identity.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double need = identity.DesiredSize.Width;
            if (picture is not null)
            {
                double free = avail - picture.Width - picture.Margin.Right;
                var shown = free >= need ? Visibility.Visible : Visibility.Collapsed;
                if (picture.Visibility != shown) picture.Visibility = shown;
            }
            if (need > avail) counts.Visibility = Visibility.Collapsed;
        }
        row.SizeChanged += (_, e) => { if (e.WidthChanged) Fit(e.NewSize.Width); };
        var tile = new Tile { Style = Ui.Res<Style>("Inset"), MinHeight = CarHeadHeight, Padding = new Thickness(4, 6, 8, 6), Child = row };
        tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        return tile;
    }

    private IEnumerable<(SetupNode, int, bool, bool, bool, int)> TreeItems(SetupCar car, SetupSearch? search, SetupNode target, HashSet<SetupNode> sel, DateTime since)
    {
        bool filtered = search is { CarHit: false };
        IEnumerable<(SetupNode, int, bool, bool, bool, int)> Walk(SetupNode folder, int depth)
        {
            foreach (var node in SetupOrder.Of(folder, S.Orders))
            {
                if (filtered && !search!.Show.Contains(node)) continue;
                bool open = node.IsFolder && (_open.Contains(node.Key) || search?.Open.Contains(node) == true);
                yield return (node, depth, open, sel.Contains(node), node == target, node.NewCount(since));
                if (open)
                    foreach (var child in Walk(node, depth + 1)) yield return child;
            }
        }
        return Walk(car.Root, 0);
    }

    private void Click(SetupCar car, SetupNode node)
    {
        var mods = Keyboard.Modifiers;
        string key = node.Key;
        bool dbl = _lastClick == key && (DateTime.Now - _lastClickAt).TotalMilliseconds < 450;
        _lastClick = dbl ? null : key;
        _lastClickAt = DateTime.Now;
        if (dbl && mods == ModifierKeys.None)
        {
            if (node.IsFolder) { if (!_open.Remove(key)) _open.Add(key); RenderExplorer(); }
            return;
        }
        if (mods.HasFlag(ModifierKeys.Shift) && _anchor is not null && _treeScroll?.Content is SetupTree tree)
        {
            var order = tree.Visible.Select(n => n.Key).ToList();
            int a = order.IndexOf(_anchor), b = order.IndexOf(key);
            if (a >= 0 && b >= 0)
                foreach (var k in order.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) _selected.Add(k);
        }
        else if (mods.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(key)) _selected.Add(key);
            _anchor = key;
        }
        else
        {
            _selected.Clear();
            _selected.Add(key);
            _anchor = key;
        }
        _target[car.Dir] = (node.IsFolder ? node : node.Parent ?? car.Root).Key;
        RenderExplorer();
        Focus();
    }

    private void Dropped(List<SetupNode> nodes, SetupNode? into, SetupNode? parent, List<SetupNode>? order)
    {
        if (into is not null)
        {
            var (moved, error) = _store.Move(nodes, into);
            if (error is not null) _app.Notify("Couldn't move everything", error, NoticeKind.Warning);
            if (moved > 0) _open.Add(into.Key);
            Save();
            AfterMove();
            return;
        }
        if (parent is not null && order is not null)
        {
            var moving = nodes.Where(n => n.Parent != parent).ToList();
            if (moving.Count > 0)
            {
                var (_, error) = _store.Move(moving, parent);
                if (error is not null) _app.Notify("Couldn't move everything", error, NoticeKind.Warning);
            }
            SetupOrder.Save(parent, order.Where(n => n.Parent == parent).Select(n => n.Name).ToList(), S.Orders);
            Save();
            AfterMove();
            return;
        }
        RenderExplorer();
    }

    private void AfterMove()
    {
        _selected.Clear();
        _anchor = null;
        Prune();
        RenderCars();
        RenderExplorer();
    }

    private static string Describe(List<SetupNode> nodes)
    {
        int folders = nodes.Count(n => n.IsFolder), files = nodes.Count - folders;
        return string.Join(", ", new[] { folders > 0 ? SetupTree.Plural(folders, "folder") : null, files > 0 ? SetupTree.Plural(files, "setup") : null }.OfType<string>());
    }

    private string Label(SetupNode folder) => folder.IsRoot ? folder.Car.Name : folder.Name;

    private FrameworkElement FolderSummary(SetupNode folder)
    {
        var bits = new List<string>();
        if (!folder.IsRoot)
        {
            bits.Add(SetupTree.Plural(folder.Files, "setup"));
            if (folder.Folders > 0) bits.Add(SetupTree.Plural(folder.Folders, "folder"));
        }
        if (folder.Files > 0) bits.Add((bits.Count == 0 ? "Changed " : "changed ") + SetupTree.Date(folder.Latest));
        var text = new TextBlock { Text = string.Join(" · ", bits), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        Ui.AddInfo(text, "Click a folder to make it where Import and New folder add. Double-click a folder to open it. Drag rows up or down to reorder, onto a folder to move them into it, or onto the gap under a folder to move them out of it.");
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0), Children = { text } };
    }

    private FrameworkElement Details(SetupCar car, List<SetupNode> sel, SetupNode target)
    {
        var stack = new StackPanel();
        Grid.SetIsSharedSizeScope(stack, true);
        void Kicker(string text) => stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Eyebrow") });
        void Title(string text) => stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
        void Fact(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 73, SharedSizeGroup = "Fact" });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint"), Margin = new Thickness(0, 0, 8, 0) });
            var v = new TextBlock { Text = value, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            stack.Children.Add(g);
        }
        void PathBox(SetupNode node)
        {
            string path = _store.PathOf(node);
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var shown = TextCase.Verbatim(TextFit.TipWhenTrimmed(new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = Ui.Res<FontFamily>("MonoFont"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }));
            var field = Hairline.Frame(new Border { Height = 28, Padding = new Thickness(8, 0, 8, 0), Child = shown });
            field.SetResourceReference(Border.BorderBrushProperty, "Line");
            field.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            g.Children.Add(field);
            var copy = Ui.IconButton("I.Copy", "Copy the path", () =>
            {
                try { Clipboard.SetText(path); _app.Notify("Path copied", path, NoticeKind.Success); }
                catch { }
            }, "QuietIconButton");
            copy.VerticalAlignment = VerticalAlignment.Center;
            copy.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(copy, 1);
            g.Children.Add(copy);
            stack.Children.Add(g);
        }
        void Note(string text) => stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });

        if (sel.Count == 1 && !sel[0].IsFolder)
        {
            var n = sel[0];
            Kicker(n.Modified >= _store.NewSince ? "Setup file · new" : "Setup file");
            Title(n.Name);
            Fact("Size", SetupTree.Size(n.Size));
            Fact("Changed", Dates.Day(n.Modified) + " · " + Dates.Time(n.Modified));
            Fact("Folder", n.Parent is { IsRoot: false } p ? p.Name : car.Name);
            PathBox(n);
            Note("Pit-Box manages the file. Setup values are loaded and changed in the iRacing garage.");
        }
        else if (sel.Count > 1)
        {
            Kicker("Selection");
            Title(Describe(sel));
            Fact("Setups", sel.Sum(n => n.Files).ToString());
            Fact("Total size", SetupTree.Size(sel.Sum(n => n.TotalSize)));
            Note("Ctrl-click or Shift-click rows to change the selection. Drag any selected row onto a folder to move them all.");
        }
        else
        {
            var folder = sel.Count == 1 ? sel[0] : target;
            Kicker(folder.IsRoot ? "Car setup folder" : "Folder");
            Title(Label(folder));
            Fact("Setups", folder.Files.ToString());
            Fact("Subfolders", folder.Folders.ToString());
            if (folder.Files > 0) Fact("Last change", SetupTree.Date(folder.Latest));
            PathBox(folder);
            Note("Import and New folder add to this folder. Click a folder to make it the target.");
        }
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void PickImport(SetupNode folder)
    {
        if (!PitBox.Services.External.Allowed("open dialog")) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import setups into " + Label(folder), Multiselect = true, CheckFileExists = true,
            Filter = "iRacing setups and share packs (*.sto;*.zip)|*.sto;*.zip|iRacing setups (*.sto)|*.sto|Share packs (*.zip)|*.zip",
        };
        if (dialog.ShowDialog() == true) ImportFiles(dialog.FileNames, folder);
    }

    private void ImportFiles(IEnumerable<string> files, SetupNode folder)
    {
        var (added, skipped) = _store.Import(files, folder);
        if (added.Count > 0)
        {
            Reveal(added);
            _app.Notify("Setups imported", $"{SetupTree.Plural(added.Count, "setup")} copied into iRacing's setups folder.", NoticeKind.Success);
        }
        if (skipped.Count > 0)
            _app.Notify("Some files were skipped", "Only .sto setups and .zip share packs with readable setups inside can be imported. Skipped: " + string.Join(", ", skipped), NoticeKind.Warning);
        RenderAll();
    }

    private void Reveal(List<SetupNode> added)
    {
        var car = added[0].Car;
        S.SelectedCar = car.Dir;
        _selected.Clear();
        foreach (var n in added.Where(n => n.Car == car))
        {
            _selected.Add(n.Key);
            for (var p = n.Parent; p is { IsRoot: false }; p = p.Parent) _open.Add(p.Key);
        }
        _target[car.Dir] = (added[0].Parent ?? car.Root).Key;
        if (Hidden(car)) _view = 2;
    }

    private async void NewFolder(SetupNode parent)
    {
        string suggestion = SetupNames.Unique(parent.Children.Select(c => c.Name), "New folder", isFile: false);
        var name = await _window.Prompt("New folder", "Inside " + Label(parent) + ". Folders show up in the iRacing garage's setup list too.", suggestion,
            text => SetupNames.Check(text, false, parent).Error, maxLength: 0);
        if (name is null) return;
        var (folder, error) = _store.CreateFolder(parent, name);
        if (error is not null) { _app.Notify("Couldn't make the folder", error, NoticeKind.Warning); return; }
        if (!parent.IsRoot) _open.Add(parent.Key);
        _target[parent.Car.Dir] = folder!.Key;
        _selected.Clear();
        RenderExplorer();
    }

    private async void Rename(SetupNode node)
    {
        if (node.Parent is not { } parent) return;
        string current = node.IsFolder ? node.Name : Path.GetFileNameWithoutExtension(node.Name);
        var name = await _window.Prompt(node.IsFolder ? "Rename folder" : "Rename setup", node.IsFolder ? "The folder is renamed on disk too." : "The file is renamed on disk; .sto is kept.", current,
            text => SetupNames.Check(text, !node.IsFolder, parent, node).Error, maxLength: 0);
        if (name is null) return;
        bool wasOpen = _open.Remove(node.Key), wasTarget = _target.TryGetValue(node.Car.Dir, out var t) && t == node.Key;
        var error = _store.Rename(node, name);
        if (error is not null) { _app.Notify("Couldn't rename", error, NoticeKind.Warning); return; }
        Save();
        _selected.Clear();
        _selected.Add(node.Key);
        if (wasOpen) _open.Add(node.Key);
        if (wasTarget) _target[node.Car.Dir] = node.Key;
        Prune();
        RenderExplorer();
    }

    private async void Delete(List<SetupNode> nodes)
    {
        int files = nodes.Sum(n => n.Files), folders = nodes.Count(n => n.IsFolder);
        string what = nodes.Count == 1 ? $"“{nodes[0].Name}”" : Describe(nodes);
        string inside = folders > 0 && files > 0 ? $" {SetupTree.Plural(files, "setup")} inside {(folders == 1 ? "it" : "them")} go too." : "";
        if (!await _window.Confirm("Delete " + what + "?", "It goes to the Windows Recycle Bin, so it can be restored from there." + inside, "Move to Recycle Bin", danger: true)) return;
        var (deleted, error) = _store.Delete(nodes);
        if (error is not null) _app.Notify("Couldn't delete everything", error, NoticeKind.Warning);
        else if (deleted > 0) _app.Notify("Moved to the Recycle Bin", deleted == 1 ? nodes[0].Name : $"{deleted} items", NoticeKind.Success);
        _selected.Clear();
        Prune();
        RenderAll();
    }

    private void PickMove(SetupCar car, List<SetupNode> nodes)
    {
        var list = new StackPanel();
        void Add(SetupNode folder, int depth)
        {
            bool ok = SetupStore.Movable(nodes, folder).Count > 0;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Folder"), Width = 12, Height = 12, Foreground = Ui.Res<Brush>("GoldDeep") });
            content.Children.Add(new TextBlock { Text = folder.IsRoot ? car.Name : folder.Name, Margin = new Thickness(6, 0, 0, 0) });
            var row = new Button { Style = Ui.Res<Style>("QuietButton"), Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(depth * 20, 1, 0, 1), IsEnabled = ok };
            row.Click += (_, _) =>
            {
                _window.CloseModal(true);
                var (moved, error) = _store.Move(nodes, folder);
                if (error is not null) _app.Notify("Couldn't move everything", error, NoticeKind.Warning);
                else if (moved > 0) _app.Notify("Moved", $"{SetupTree.Plural(moved, "item")} moved to {Label(folder)}.", NoticeKind.Success);
                if (!folder.IsRoot) _open.Add(folder.Key);
                AfterMove();
            };
            list.Children.Add(row);
            foreach (var child in SetupOrder.Of(folder, S.Orders).Where(c => c.IsFolder)) Add(child, depth + 1);
        }
        Add(car.Root, 0);
        var buttons = new StackPanel();
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => _window.CloseModal(false);
        buttons.Children.Add(cancel);
        _ = _window.ShowModal(Ui.Dialog(_window, "Move " + (nodes.Count == 1 ? $"“{nodes[0].Name}”" : Describe(nodes)), "Pick a folder of " + car.Name + ".", list, buttons, width: 389));
    }

    private void Export(SetupCar car, List<SetupNode> nodes)
    {
        bool pack = true;
        string destination = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var content = new StackPanel();
        var where = new TextBlock { Text = destination, Style = Ui.Res<Style>("Caption"), FontFamily = Ui.Res<FontFamily>("MonoFont"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var describe = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        void Explain() => describe.Text = pack
            ? $"One file, {SetupStore.PackName(car, nodes)}, for league mates: they can import it in Pit-Box, or unzip it straight into Documents\\iRacing\\setups and each setup lands under the right car."
            : $"Plain copies of the .sto files in a “{car.Name}” folder, keeping the folder structure.";
        content.Children.Add(Ui.Segmented(new[] { "Share pack (.zip)", "Copy .sto files" }, 0, i => { pack = i == 0; Explain(); }, "setupexport"));
        Explain();
        content.Children.Add(describe);
        var row = new Grid { Margin = new Thickness(0, 11, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = "Save to", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        Grid.SetColumn(where, 1);
        row.Children.Add(where);
        var browse = Ui.LabeledButton("I.Folder", "Choose…", () =>
        {
            if (!PitBox.Services.External.Allowed("folder dialog")) return;
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Where to save", InitialDirectory = destination };
            if (dialog.ShowDialog() == true) { destination = dialog.FolderName; where.Text = destination; }
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(browse, 2);
        row.Children.Add(browse);
        content.Children.Add(row);
        int count = nodes.Sum(n => n.Files);
        var buttons = Ui.DialogButtons(_window, "Save", () =>
        {
            var (path, error) = _store.Export(nodes, destination, pack);
            if (error is not null) { _app.Notify("Nothing saved", error, NoticeKind.Warning); return false; }
            _app.Notify(pack ? "Share pack saved" : "Setups copied", path!, NoticeKind.Success);
            if (path is not null) OpenPath(pack ? $"/select,\"{path}\"" : $"\"{path}\"");
            return true;
        });
        _ = _window.ShowModal(Ui.Dialog(_window, "Share or export " + SetupTree.Plural(count, "setup"), "From " + car.Name + ". The originals stay where they are.", content, buttons, width: 454));
    }

    private void EditCarName(SetupCar car)
    {
        var content = new StackPanel();
        var box = new TextBox { Text = car.Name, Margin = new Thickness(0, 3, 0, 0), MaxLength = Ui.NameLength };
        content.Children.Add(Ui.FieldRow("Name", box, 73));
        string cls = car.Class;
        var classes = Ui.Combo(SetupCars.Classes.Select(c => new Choice(c, c)), cls, c => cls = c.Id ?? cls, 162);
        content.Children.Add(Ui.FieldRow("Class", classes, 73));
        content.Children.Add(new TextBlock { Text = "The folder on disk stays " + car.Dir + ".", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 6, 0, 0) });
        var buttons = Ui.DialogButtons(_window, "Save", () =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return false;
            S.Names[car.Dir] = new SetupCarName { Name = box.Text.Trim(), Class = cls };
            _store.Describe();
            Save();
            RenderAll();
            return true;
        });
        if (car.Custom)
        {
            string back = SetupCars.BuiltInName(car.Dir) ?? car.Dir;
            var reset = new Button { Content = "Use “" + back + "”", Margin = Ui.AfterButton };
            reset.Click += (_, _) =>
            {
                S.Names.Remove(car.Dir);
                _store.Describe();
                Save();
                _window.CloseModal(true);
                RenderAll();
            };
            buttons.Children.Insert(0, reset);
        }
        _ = _window.ShowModal(Ui.Dialog(_window, "Name shown for this car", "Only how Pit-Box shows it; iRacing finds the car by its folder name.", content, buttons, width: 421));
    }

    private void OpenFolder(SetupNode folder)
    {
        string path = _store.PathOf(folder);
        if (!_store.Demo && !Directory.Exists(path)) { _app.Notify("Folder not found", path, NoticeKind.Warning); return; }
        OpenPath($"\"{path}\"");
    }

    private void Reveal(SetupNode file) => OpenPath($"/select,\"{_store.PathOf(file)}\"");

    private void OpenPath(string arguments)
    {
        if (_store.Demo) { _app.Notify("File Explorer", "Opens File Explorer there (not available with demo data).", NoticeKind.Info); return; }
        if (!PitBox.Services.External.Allowed("open File Explorer")) return;
        try { Process.Start(new ProcessStartInfo(App.Explorer, arguments) { UseShellExecute = true })?.Dispose(); }
        catch { _app.Notify("File Explorer", "Windows couldn't open File Explorer.", NoticeKind.Warning); }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var car = _store.Car(S.SelectedCar) ?? SelectedCar(VisibleCars());
        if (car is null) return;
        var sel = SelectedNodes(car);
        switch (e.Key)
        {
            case Key.Delete when sel.Count > 0: Delete(sel); e.Handled = true; break;
            case Key.F2 when sel.Count == 1: Rename(sel[0]); e.Handled = true; break;
            case Key.Escape when sel.Count > 0: _selected.Clear(); RenderExplorer(); e.Handled = true; break;
        }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("setups", () =>
        {
            S.SelectedCar = "porsche963gtp";
            _open.Add("porsche963gtp/IMSA");
            _open.Add("porsche963gtp/IMSA/Sebring");
            _selected.Clear();
            _selected.Add("porsche963gtp/IMSA/Sebring/Race.sto");
            RenderAll();
            return Task.CompletedTask;
        });
        yield return ("setups-downloads", () =>
        {
            S.InboxFolded = false;
            RenderInbox();
            if (_inbox.Visibility != Visibility.Visible) throw new InvalidOperationException("The Downloads button didn't open the files found.");
            return Task.CompletedTask;
        });
        yield return ("setups-gtp-hidden", () =>
        {
            S.InboxFolded = true;
            S.SetClassHidden("GTP", true);
            _classFilter.Remove("GTP");
            RenderAll();
            if (VisibleCars().Any(c => c.Class == "GTP"))
                throw new InvalidOperationException("A hidden class's cars stayed in the car list.");
            return Task.CompletedTask;
        });
        yield return ("setups-classes-back", () =>
        {
            S.HiddenClasses.Clear();
            S.SelectedCar = "porsche963gtp";
            RenderAll();
            return Task.CompletedTask;
        });
    }
}
