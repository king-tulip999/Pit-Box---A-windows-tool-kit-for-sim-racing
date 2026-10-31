using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PitBox.Views;

internal sealed record PageSection(string Id, string Title, string Icon);

internal sealed record SectionList(IReadOnlyList<PageSection> Sections, string? Current, bool Movable = true);

internal interface ISectionedPage
{
    IReadOnlyList<PageSection> Sections { get; }

    string? CurrentSection { get; }

    void ShowSection(string id);

    void MoveSections(IReadOnlyList<string> order);

    bool ShowSectionOf(DependencyObject target);

    bool SectionsMovable => true;

    event Action? SectionsChanged;
}

internal interface IStagedBuild
{
    bool Done { get; }

    bool BuildStep();
}

internal sealed record SectionDef(string Id, string Title, string Subtitle, string Icon, Func<UIElement> Build,
    string? Anchor = null, Action? Shown = null, Action? Hidden = null, FrameworkElement? Tools = null, bool Beta = false);

internal sealed class SectionView
{
    private sealed class Entry
    {
        public required SectionDef Def;
        public required Border Host;
        public bool Built;
        public double Offset;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid _content = new();
    private readonly TextBlock _title = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
    private readonly Border _beta = Ui.BetaChip();
    private readonly TrimRow _name;
    private readonly TextBlock _subtitle = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
    private readonly ContentControl _tools = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _heading = new();
    private readonly Func<ScrollViewer?> _scroll;
    private bool _active;

    public SectionView(Func<ScrollViewer?> scroll)
    {
        _scroll = scroll;
        _title.Style = Ui.Res<Style>("H1");
        _subtitle.Style = Ui.Res<Style>("Muted");
        _name = new TrimRow { VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Children = { _title, _beta } };
        _right.Children.Add(_tools);
        _heading.Children.Add(Ui.SplitRow(_name, _right));
        _heading.Children.Add(_subtitle);
    }

    public FrameworkElement Heading => _heading;

    public FrameworkElement Content => _content;

    public string? Current { get; private set; }

    public void AddRight(FrameworkElement element) => _right.Children.Add(element);

    public void Add(SectionDef def)
    {
        var host = new Border { Visibility = Visibility.Collapsed };
        if (def.Anchor is not null) Ui.Anchor(host, def.Anchor);
        _entries[def.Id] = new Entry { Def = def, Host = host };
        _content.Children.Add(host);
    }

    public bool Has(string id) => _entries.ContainsKey(id);

    public SectionDef? Def(string id) => _entries.TryGetValue(id, out var e) ? e.Def : null;

    public FrameworkElement? Host(string id) => _entries.TryGetValue(id, out var e) ? e.Host : null;

    public bool Built(string id) => _entries.TryGetValue(id, out var e) && Done(e);

    private static bool Done(Entry entry) => entry.Built && entry.Host.Child is not IStagedBuild { Done: false };

    private void Build(Entry entry)
    {
        if (!entry.Built)
        {
            entry.Built = true;
            entry.Host.Child = entry.Def.Build();
        }
        if (entry.Host.Child is IStagedBuild staged)
            while (staged.BuildStep()) { }
    }

    public SectionDef? Empty { get; set; }

    public void Reset(string id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.Def.Id == Current) return;
        entry.Host.Child = null;
        entry.Built = false;
        entry.Offset = 0;
    }

    public void Build(string id)
    {
        if (_entries.TryGetValue(id, out var entry)) Build(entry);
    }

    public void BuildLater(IEnumerable<string> ids)
    {
        var queue = new Queue<Entry>();
        foreach (var id in ids)
            if (_entries.TryGetValue(id, out var entry) && !Done(entry))
                queue.Enqueue(entry);
        if (queue.Count > 0) Motion.WhenUserIdle(_content.Dispatcher, () => BuildNext(queue));
    }

    private void BuildNext(Queue<Entry> queue)
    {
        if (!_content.IsLoaded) return;
        while (queue.TryPeek(out var entry))
        {
            if (Services.HitchWatch.On) Services.HitchWatch.Note = "prebuild " + entry.Def.Id;
            if (!entry.Built)
            {
                entry.Built = true;
                entry.Host.Child = entry.Def.Build();
                if (entry.Host.Child is not IStagedBuild) queue.Dequeue();
                break;
            }
            if (entry.Host.Child is IStagedBuild staged && staged.BuildStep()) break;
            queue.Dequeue();
        }
        if (queue.Count > 0) Motion.WhenUserIdle(_content.Dispatcher, () => BuildNext(queue));
    }

    public void Show(string? id, bool animate = true)
    {
        Entry? next = id is not null && _entries.TryGetValue(id, out var found) ? found : null;
        if (next is not null && next.Def.Id == Current && next.Host.Visibility == Visibility.Visible) return;
        var scroll = _scroll();
        if (Current is not null && _entries.TryGetValue(Current, out var old))
        {
            old.Offset = scroll?.VerticalOffset ?? 0;
            old.Host.Visibility = Visibility.Collapsed;
            if (_active) old.Def.Hidden?.Invoke();
        }
        Current = next?.Def.Id;
        _tools.Content = null;
        var def = next?.Def ?? Empty;
        _heading.Visibility = def is null ? Visibility.Collapsed : Visibility.Visible;
        if (def is null) return;
        if (next is not null) Build(next);
        Ui.SetTitle(_title, def.Title);
        _beta.Visibility = def.Beta ? Visibility.Visible : Visibility.Collapsed;
        _subtitle.Text = def.Subtitle;
        _subtitle.Visibility = next is null && def.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _name.ToolTip = def.Subtitle.Length > 0 ? def.Subtitle : null;
        _tools.Content = def.Tools;
        _tools.Visibility = def.Tools is null ? Visibility.Collapsed : Visibility.Visible;
        if (next is null) return;
        next.Host.Visibility = Visibility.Visible;
        if (_active) def.Shown?.Invoke();
        if (scroll is not null)
        {
            double offset = next.Offset;
            scroll.ScrollToVerticalOffset(offset);
            if (offset > 0) _content.Dispatcher.BeginInvoke(() => scroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        if (animate && _active)
        {
            Motion.FadeIn(next.Host);
            Motion.FadeIn(_heading);
        }
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (Current is null || !_entries.TryGetValue(Current, out var entry)) return;
        if (active) entry.Def.Shown?.Invoke();
        else entry.Def.Hidden?.Invoke();
    }

    public string? SectionOf(DependencyObject target)
    {
        for (DependencyObject? cur = target; cur is not null; cur = LogicalTreeHelper.GetParent(cur) ?? (cur is Visual ? VisualTreeHelper.GetParent(cur) : null))
            foreach (var entry in _entries.Values)
                if (ReferenceEquals(entry.Host, cur)) return entry.Def.Id;
        return null;
    }
}
