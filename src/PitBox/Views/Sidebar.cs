using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PitBox.Controls;
using PitBox.Theme;

namespace PitBox.Views;

internal sealed record SidebarPage(string Key, string Title);

internal sealed class Sidebar : Border, IFrameStep
{
    public const double PanelWidth = 214;

    private const double RowHeight = 42, SubRowHeight = 31, Indent = 15, TextSize = 16, SubTextSize = 14.5, SubTextInset = 14;
    private const double MaxTextSize = 19, SubShare = 0.8, RowPerText = 2.05, SubRowPerText = 1.95;
    private const double DashInset = 12, DashWidth = 6, DashGap = 10, DashThickness = 2, LitWidth = 2;
    private const double TabShift = 8, TabMs = Motion.SwapMs;

    public event Action<string>? PageClicked;
    public event Action<string, string>? SectionClicked;
    public event Action<IReadOnlyList<string>>? PagesMoved;
    public event Action<string, IReadOnlyList<string>>? SectionsMoved;

    private readonly StackPanel _pages = new();
    private readonly Grid _track = new();
    private readonly Border _glider = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TranslateTransform _glide = new();
    private Row? _arriving;
    private Point _glideFrom;
    private double _glideFromHeight, _glideStart;
    private const double GlideMs = Motion.SwapMs;
    private readonly StackPanel _footer = new() { Margin = new Thickness(6, 5, 6, 6) };
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, Group> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly DragList _pageDrag;
    private string _signature = "";

    private sealed class Group
    {
        public required SidebarPage Page;
        public required Row Row;
        public required Border SubHost;
        public required StackPanel SubList;
        public DragList? SubDrag;
        public string SubSignature = "";
        public bool Open;
        public Dictionary<string, Row> SubRows = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Row
    {
        public required Border Box;
        public required Border Bar;
        public Border? Dash;
        public required TextBlock Text;
        public required TranslateTransform Tab;
        public bool Selected, Hover, HasChild, Arriving, Tabbed;
        public bool Sub;
    }

    public Sidebar()
    {
        Width = PanelWidth;
        Hairline.Frame(this);
        SetResourceReference(BackgroundProperty, "SidebarBg");
        SetResourceReference(BorderBrushProperty, "Line");
        SetResourceReference(CornerRadiusProperty, "R.Card");
        _glider.SetResourceReference(BackgroundProperty, "AccentSoft");
        _glider.RenderTransform = _glide;
        _track.Children.Add(_glider);
        _track.Children.Add(_pages);
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _track, Focusable = false, Padding = new Thickness(6), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_scroll);
        var line = Hairline.Rule(new Border { Margin = new Thickness(10, 0, 10, 0) });
        line.SetResourceReference(BackgroundProperty, "Line");
        Grid.SetRow(line, 1);
        root.Children.Add(line);
        Grid.SetRow(_footer, 2);
        root.Children.Add(_footer);
        Child = root;
        AutomationProperties.SetName(this, "Tools");
        _pageDrag = new DragList(_pages, keys => PagesMoved?.Invoke(keys));
        _pages.SizeChanged += (_, e) => { if (e.WidthChanged) FitText(); };
    }

    private double _textSize = TextSize;

    private double PageRoom => _pages.ActualWidth - (DashInset + DashWidth + DashGap) - (6 + TabShift);

    private double SubRoom => _pages.ActualWidth - Indent - 1 - SubTextInset - 6;

    private const double MeasureSize = 100;

    private static double WidthPerPoint(TextBlock text)
    {
        double size = text.FontSize;
        text.FontSize = MeasureSize;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;
        text.FontSize = size;
        return Math.Ceiling(width + 1) / MeasureSize;
    }

    private static double Fitted(IEnumerable<TextBlock> texts, double room, double max)
    {
        double widest = 0;
        foreach (var text in texts) widest = Math.Max(widest, WidthPerPoint(text));
        if (widest <= 0 || room <= 0) return max;
        return Math.Min(max, Math.Floor(room / widest * 2) / 2);
    }

    private static void SizeRow(Row row, double size, double perText)
    {
        row.Text.FontSize = size;
        row.Box.Height = Math.Round(size * perText);
    }

    private void FitText()
    {
        if (_pages.ActualWidth <= 0) return;
        _textSize = Fitted(_groups.Where(g => g.Key != _settingsKey).Select(g => g.Value.Row.Text), PageRoom, MaxTextSize);
        foreach (var group in _groups.Values)
        {
            SizeRow(group.Row, _textSize, RowPerText);
            FitSections(group);
        }
    }

    private void FitSections(Group group)
    {
        if (_pages.ActualWidth <= 0 || group.SubRows.Count == 0) return;
        double size = Fitted(group.SubRows.Values.Select(r => r.Text), SubRoom, Math.Floor(_textSize * SubShare * 2) / 2);
        foreach (var row in group.SubRows.Values) SizeRow(row, size, SubRowPerText);
    }

    private Group MakeGroup(SidebarPage p, Func<bool> dragging)
    {
        var row = MakeRow(p.Title, RowHeight, sub: false);
        row.Box.MouseLeftButtonUp += (_, e) => { if (!dragging()) { e.Handled = true; PageClicked?.Invoke(p.Key); } };
        OnActivate(row.Box, () => PageClicked?.Invoke(p.Key));
        var subList = new StackPanel();
        var guide = Hairline.Frame(new Border { Margin = new Thickness(Indent, 2, 0, 6), Child = subList }, Sides.Left);
        guide.SetResourceReference(BorderBrushProperty, "Line");
        var subHost = new Border { ClipToBounds = true, Child = guide, Visibility = Visibility.Collapsed };
        var group = new Group { Page = p, Row = row, SubHost = subHost, SubList = subList };
        _groups[p.Key] = group;
        return group;
    }

    public void Build(IReadOnlyList<SidebarPage> pages, SidebarPage settings, FrameworkElement searchButton, FrameworkElement searchHost, FrameworkElement? themeButton)
    {
        _settingsKey = settings.Key;
        string signature = ThemeManager.Current.Id + "|" + string.Join("|", pages.Select(p => $"{p.Key}:{p.Title}"));
        if (signature == _signature) return;
        _signature = signature;
        _pages.Children.Clear();
        _groups.Clear();
        _pageDrag.Clear();
        foreach (var page in pages)
        {
            var group = MakeGroup(page, () => _pageDrag.Dragging);
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            panel.Children.Add(group.Row.Box);
            panel.Children.Add(group.SubHost);
            _pages.Children.Add(panel);
            _pageDrag.Add(panel, group.Row.Box, page.Key);
        }

        _footer.Children.Clear();
        if (searchHost.Parent is Panel oldHost) oldHost.Children.Remove(searchHost);
        if (searchButton.Parent is Panel oldButton) oldButton.Children.Remove(searchButton);
        searchHost.Margin = new Thickness(0, 2, 0, 5);
        _footer.Children.Add(searchHost);
        var line = new DockPanel { LastChildFill = true };
        if (themeButton is not null)
        {
            themeButton.Margin = new Thickness(3, 0, 0, 0);
            themeButton.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(themeButton, Dock.Right);
            line.Children.Add(themeButton);
        }
        searchButton.Margin = new Thickness(5, 0, 0, 0);
        searchButton.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(searchButton, Dock.Right);
        line.Children.Add(searchButton);
        var settingsGroup = MakeGroup(settings, () => false);
        settingsGroup.Row.Box.Margin = new Thickness(0);
        line.Children.Add(settingsGroup.Row.Box);
        _footer.Children.Add(line);
        _footer.Children.Add(settingsGroup.SubHost);
        FitText();
    }

    private string? _settingsKey;

    private static readonly string[] AccentKeys = ["Accent", "AccentSoft", "AccentText"];

    public void UseAccent(ResourceDictionary? theme)
    {
        foreach (var key in AccentKeys)
        {
            if (theme?[key] is { } value) Resources[key] = value;
            else Resources.Remove(key);
        }
        foreach (var group in _groups.Values)
        {
            Paint(group.Row);
            foreach (var sub in group.SubRows.Values) Paint(sub);
        }
    }

    private bool _instant;

    public void Select(string? key, Func<string, SectionList?> sectionsOf, bool allOpen, bool animate = true)
    {
        _instant = !animate;
        try { SelectRows(key, sectionsOf, allOpen, animate); }
        finally { _instant = false; }
    }

    private void SelectRows(string? key, Func<string, SectionList?> sectionsOf, bool allOpen, bool animate)
    {
        var leaving = Filled();
        foreach (var (k, group) in _groups)
        {
            bool selected = string.Equals(k, key, StringComparison.OrdinalIgnoreCase);
            var info = selected || allOpen && k != _settingsKey ? sectionsOf(k) : null;
            group.Row.Selected = selected;
            group.Row.HasChild = selected && info is { Sections.Count: > 1 };
            Paint(group.Row);
            if (info is not null) FillSections(group, info.Sections, selected ? info.Current : null, info.Movable);
            SetOpen(group, info is { Sections.Count: > 1 }, animate);
        }
        if (key is not null && _groups.TryGetValue(key, out var shown))
        {
            var arriving = Filled() ?? shown.Row;
            if (animate && leaving is not null && leaving != arriving) Glide(leaving, arriving);
            Dispatcher.BeginInvoke(() => shown.Row.Box.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private Row? Filled()
    {
        foreach (var group in _groups.Values)
        {
            if (group.Row.Selected && !group.Row.HasChild) return group.Row;
            foreach (var sub in group.SubRows.Values)
                if (sub.Selected && group.Open) return sub;
        }
        return null;
    }

    private bool InTrack(Row row) => row.Box.IsVisible && _track.IsAncestorOf(row.Box);

    private void Glide(Row from, Row to)
    {
        if (!Motion.Flourish || !InTrack(from) || !InTrack(to) || from.Box.ActualHeight <= 0) return;
        if (_arriving is { } previous) { previous.Arriving = false; Paint(previous); }
        _glideFrom = from.Box.TranslatePoint(new Point(0, 0), _track);
        _glideFromHeight = from.Box.ActualHeight;
        _glideStart = FrameDriver.Now;
        _arriving = to;
        to.Arriving = true;
        Paint(to);
        _glider.Width = to.Box.ActualWidth;
        _glider.Visibility = Visibility.Visible;
        Step(_glideStart);
        FrameDriver.Run(this);
    }

    public bool Step(double now)
    {
        if (_arriving is not { } to) return false;
        double t = (now - _glideStart) / GlideMs;
        if (!InTrack(to)) t = 1;
        if (t >= 1)
        {
            _arriving = null;
            to.Arriving = false;
            Paint(to);
            _glider.Visibility = Visibility.Collapsed;
            return false;
        }
        var at = to.Box.TranslatePoint(new Point(0, 0), _track);
        double e = FrameDriver.Spring(t);
        _glide.X = _glideFrom.X + (at.X - _glideFrom.X) * e;
        _glide.Y = _glideFrom.Y + (at.Y - _glideFrom.Y) * e;
        _glider.Height = Math.Max(1, _glideFromHeight + (to.Box.ActualHeight - _glideFromHeight) * Math.Clamp(e, 0, 1));
        return true;
    }

    private void FillSections(Group group, IReadOnlyList<PageSection> sections, string? current, bool movable)
    {
        string signature = movable + "|" + string.Join("|", sections.Select(s => $"{s.Id}:{s.Title}"));
        if (signature != group.SubSignature)
        {
            group.SubSignature = signature;
            group.SubList.Children.Clear();
            group.SubRows.Clear();
            string page = group.Page.Key;
            var drag = group.SubDrag ??= new DragList(group.SubList, keys => SectionsMoved?.Invoke(page, keys));
            drag.Clear();
            foreach (var section in sections)
            {
                var id = section.Id;
                var row = MakeRow(section.Title, SubRowHeight, sub: true);
                row.Box.MouseLeftButtonUp += (_, e) => { if (!drag.Dragging) { e.Handled = true; SectionClicked?.Invoke(page, id); } };
                OnActivate(row.Box, () => SectionClicked?.Invoke(page, id));
                group.SubRows[id] = row;
                group.SubList.Children.Add(row.Box);
                if (movable) drag.Add(row.Box, row.Box, id);
            }
            FitSections(group);
        }
        foreach (var (id, row) in group.SubRows)
        {
            row.Selected = string.Equals(id, current, StringComparison.OrdinalIgnoreCase);
            Paint(row);
        }
    }

    private void SetOpen(Group group, bool open, bool animate)
    {
        if (group.Open == open) return;
        group.Open = open;
        var host = group.SubHost;
        host.BeginAnimation(HeightProperty, null);
        if (!animate || !Motion.Enabled)
        {
            host.ClearValue(HeightProperty);
            host.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        double from = host.Visibility == Visibility.Visible ? host.ActualHeight : 0;
        host.Visibility = Visibility.Visible;
        double to = 0;
        if (open)
        {
            host.Child.Measure(new Size(Math.Max(1, _pages.ActualWidth), double.PositiveInfinity));
            to = host.Child.DesiredSize.Height;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(open ? 240 : 180)) { EasingFunction = Motion.Ease };
        anim.Completed += (_, _) =>
        {
            if (group.Open != open) return;
            host.BeginAnimation(HeightProperty, null);
            host.ClearValue(HeightProperty);
            if (!open) host.Visibility = Visibility.Collapsed;
        };
        host.BeginAnimation(HeightProperty, anim);
    }

    private static void OnActivate(Border box, Action activate)
    {
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || e.OriginalSource != box) return;
            e.Handled = true;
            activate();
        };
    }

    private Row MakeRow(string title, double height, bool sub)
    {
        var bar = new Border { Width = LitWidth, Margin = new Thickness(-LitWidth / 2, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Hidden };
        bar.SetResourceReference(BackgroundProperty, "Accent");
        var text = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = sub ? SubTextSize : TextSize, FontWeight = FontWeights.Bold,
            Margin = new Thickness(sub ? SubTextInset : DashInset + DashWidth + DashGap, 0, 6 + (sub ? 0 : TabShift), 0),
        });
        text.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        TextCase.SetTight(text, true);
        TextCase.SetUpper(text, true);
        var face = new Grid();
        face.Children.Add(text);
        face.Children.Add(bar);
        Border? dash = null;
        if (!sub)
        {
            dash = new Border { Width = DashWidth, Height = DashThickness, Margin = new Thickness(DashInset, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
            face.Children.Add(dash);
        }
        var box = new Tile
        {
            Height = height, Child = face, Cursor = Cursors.Hand, Focusable = true, Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, sub ? 1 : 2), FocusVisualStyle = Ui.Res<Style>("FocusRing"),
        };
        box.SetResourceReference(CornerRadiusProperty, "R.Control");
        AutomationProperties.SetName(box, title);
        KeyboardNavigation.SetIsTabStop(box, true);
        var tab = new TranslateTransform();
        text.RenderTransform = tab;
        var row = new Row { Box = box, Bar = bar, Dash = dash, Text = text, Tab = tab, Sub = sub };
        box.MouseEnter += (_, _) => { row.Hover = true; Paint(row); };
        box.MouseLeave += (_, _) => { row.Hover = false; Paint(row); };
        box.GotKeyboardFocus += (_, _) => { row.Hover = true; Paint(row); };
        box.LostKeyboardFocus += (_, _) => { row.Hover = box.IsMouseOver; Paint(row); };
        Motion.Pressable(box);
        Paint(row);
        return row;
    }

    private void Paint(Row row)
    {
        bool filled = row.Selected && !row.HasChild && !row.Arriving;
        row.Bar.Visibility = row.Sub && row.Selected ? Visibility.Visible : Visibility.Hidden;
        row.Dash?.SetResourceReference(BackgroundProperty, row.Selected ? "Accent" : "LineStrong");
        if (filled) row.Box.SetResourceReference(BackgroundProperty, "AccentSoft");
        else if (row.Hover) row.Box.SetResourceReference(BackgroundProperty, "Hover");
        else row.Box.Background = Brushes.Transparent;
        string tone = row.Selected || row.Hover ? "Heading" : "TextMuted";
        row.Text.SetResourceReference(TextBlock.ForegroundProperty, tone);
        TabOver(row, row.Selected && !row.Sub);
    }

    private void TabOver(Row row, bool tabbed)
    {
        if (row.Tabbed == tabbed) return;
        row.Tabbed = tabbed;
        double to = tabbed ? TabShift : 0;
        if (_instant || !row.Box.IsLoaded || !Motion.Enabled)
        {
            row.Tab.BeginAnimation(TranslateTransform.XProperty, null);
            row.Tab.X = to;
            row.Dash?.BeginAnimation(WidthProperty, null);
            if (row.Dash is not null) row.Dash.Width = DashWidth + to;
            return;
        }
        double from = row.Tab.X;
        var duration = TimeSpan.FromMilliseconds(TabMs);
        row.Tab.X = to;
        row.Tab.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, to, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        if (row.Dash is null) return;
        row.Dash.Width = DashWidth + to;
        row.Dash.BeginAnimation(WidthProperty, new DoubleAnimation(DashWidth + from, DashWidth + to, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
    }

    private sealed class DragList
    {
        private readonly Panel _panel;
        private readonly Action<IReadOnlyList<string>> _moved;
        private readonly List<(FrameworkElement Item, FrameworkElement Handle, string Key)> _items = new();
        private FrameworkElement? _pressed;
        private Point _start;
        private List<string>? _before;

        public bool Dragging { get; private set; }

        public DragList(Panel panel, Action<IReadOnlyList<string>> moved)
        {
            _panel = panel;
            _moved = moved;
            panel.PreviewMouseLeftButtonDown += OnDown;
            panel.PreviewMouseMove += OnMove;
            panel.PreviewMouseLeftButtonUp += OnUp;
            panel.LostMouseCapture += (_, _) => { if (Dragging) Finish(commit: true); };
            panel.PreviewKeyDown += OnKey;
        }

        public void Clear() => _items.Clear();

        public void Add(FrameworkElement item, FrameworkElement handle, string key) => _items.Add((item, handle, key));

        private (FrameworkElement Item, FrameworkElement Handle, string Key)? Find(object source)
        {
            if (source is not DependencyObject d) return null;
            foreach (var entry in _items)
                if (entry.Handle == d || entry.Handle.IsAncestorOf(d)) return entry;
            return null;
        }

        private List<string> Keys() => _panel.Children.OfType<FrameworkElement>().Select(c => _items.FirstOrDefault(i => i.Item == c).Key).OfType<string>().ToList();

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _pressed = _items.Count > 1 && Find(e.OriginalSource) is { } hit ? hit.Item : null;
            _start = e.GetPosition(_panel);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
            var at = e.GetPosition(_panel);
            if (!Dragging)
            {
                if (Math.Abs(at.Y - _start.Y) < 6) return;
                Dragging = true;
                _before = Keys();
                _pressed.Opacity = 0.75;
                Panel.SetZIndex(_pressed, 1);
                _panel.CaptureMouse();
            }
            e.Handled = true;
            int from = _panel.Children.IndexOf(_pressed), to = 0;
            foreach (FrameworkElement child in _panel.Children)
            {
                if (child == _pressed || child.Visibility != Visibility.Visible) continue;
                double mid = child.TranslatePoint(new Point(0, 0), _panel).Y + child.ActualHeight / 2;
                if (at.Y > mid) to++;
            }
            if (to == from) return;
            _panel.Children.RemoveAt(from);
            _panel.Children.Insert(to, _pressed);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (Dragging)
            {
                e.Handled = true;
                Finish(commit: true);
            }
            _pressed = null;
        }

        private void Finish(bool commit)
        {
            if (!Dragging) return;
            var pressed = _pressed;
            Dragging = false;
            _pressed = null;
            if (pressed is not null)
            {
                pressed.Opacity = 1;
                Panel.SetZIndex(pressed, 0);
            }
            if (_panel.IsMouseCaptured) _panel.ReleaseMouseCapture();
            var after = Keys();
            if (commit && _before is not null && !after.SequenceEqual(_before)) _moved(after);
            _before = null;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Alt || e.SystemKey is not (Key.Up or Key.Down) || Find(e.OriginalSource) is not { } hit || hit.Handle != e.OriginalSource) return;
            int from = _panel.Children.IndexOf(hit.Item), to = from + (e.SystemKey == Key.Up ? -1 : 1);
            e.Handled = true;
            if (from < 0 || to < 0 || to >= _panel.Children.Count) return;
            _panel.Children.RemoveAt(from);
            _panel.Children.Insert(to, hit.Item);
            hit.Handle.Focus();
            _moved(Keys());
        }
    }
}
