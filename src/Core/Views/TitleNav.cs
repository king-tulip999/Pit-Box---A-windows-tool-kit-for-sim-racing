using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Core.Controls;
using Core.Theme;

namespace Core.Views;

internal sealed record NavItem(string Key, string Page, string? Section, string Title);

internal sealed record NavGroup(string Key, string Title, IReadOnlyList<NavItem> Items, bool Movable)
{
    public bool IsMenu => Items.Count > 1;
}

internal sealed class TitleNav : Grid
{
    public const double Lead = 11;

    private const double TextSize = 21, PartGap = 4, ChevronSize = 11, RowHeight = 31, MenuMinWidth = 220;

    public event Action<NavItem>? Picked;
    public event Action<IReadOnlyList<string>>? GroupsMoved;
    public event Action<string, IReadOnlyList<string>>? ItemsMoved;

    private readonly GroupRow _groups = new("Accent", leadAt: -3, leadOpacity: 0.7, between: true);
    private readonly GroupRow _end = new("LineStrong", leadAt: 0, leadOpacity: 1, between: false);
    private readonly DragList _drag;
    private readonly List<Entry> _entries = new();
    private string _signature = "";
    private string? _page, _section;
    private Popup? _menu;
    private Entry? _menuEntry, _closedEntry;
    private long _closedAt;

    private sealed class GroupRow : StackPanel
    {
        private const double MarkWidth = 2, MarkHeight = 22, MarkLeanDegrees = 18;

        public static readonly DependencyProperty LeadBrushProperty = DependencyProperty.Register(nameof(LeadBrush), typeof(Brush), typeof(GroupRow),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(GroupRow),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush? LeadBrush { get => (Brush?)GetValue(LeadBrushProperty); set => SetValue(LeadBrushProperty, value); }
        public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

        private static readonly double Lean = MarkHeight / 2 * Math.Tan(MarkLeanDegrees * Math.PI / 180);

        private readonly double _leadAt, _leadOpacity;
        private readonly bool _between;
        private readonly List<double> _marks = new();

        public GroupRow(string leadKey, double leadAt, double leadOpacity, bool between)
        {
            Orientation = Orientation.Horizontal;
            _leadAt = leadAt;
            _leadOpacity = leadOpacity;
            _between = between;
            SetResourceReference(LeadBrushProperty, leadKey);
            SetResourceReference(LineBrushProperty, "LineStrong");
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var size = base.ArrangeOverride(arrangeSize);
            if (!_between) return size;
            bool changed = false, first = true;
            int count = 0;
            double x = 0;
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility != Visibility.Visible) continue;
                if (!first)
                {
                    if (count < _marks.Count)
                    {
                        if (_marks[count] != x) { _marks[count] = x; changed = true; }
                    }
                    else { _marks.Add(x); changed = true; }
                    count++;
                }
                first = false;
                x += child.DesiredSize.Width;
            }
            if (_marks.Count > count) { _marks.RemoveRange(count, _marks.Count - count); changed = true; }
            if (changed) InvalidateVisual();
            return size;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (InternalChildren.Count == 0) return;
            Mark(dc, LeadBrush, _leadAt, _leadOpacity);
            foreach (double x in _marks) Mark(dc, LineBrush, x, 1);
        }

        private void Mark(DrawingContext dc, Brush? brush, double x, double opacity)
        {
            if (brush is null) return;
            double mid = Math.Round(ActualHeight / 2), top = mid - MarkHeight / 2, bottom = mid + MarkHeight / 2, half = MarkWidth / 2;
            x = Math.Round(x);
            var shape = new StreamGeometry();
            using (var c = shape.Open())
            {
                c.BeginFigure(new Point(x - half - Lean, bottom), true, true);
                c.LineTo(new Point(x + half - Lean, bottom), true, false);
                c.LineTo(new Point(x + half + Lean, top), true, false);
                c.LineTo(new Point(x - half + Lean, top), true, false);
            }
            shape.Freeze();
            if (opacity < 1) dc.PushOpacity(opacity);
            dc.DrawGeometry(brush, null, shape);
            if (opacity < 1) dc.Pop();
        }
    }

    private sealed class Entry
    {
        public required NavGroup Group;
        public required Border Box;
        public TextBlock? Text;
        public Icon? Glyph;
        public Icon? Chevron;
        public bool Selected, Hover;
    }

    public TitleNav()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(_groups);
        SetColumn(_end, 1);
        Children.Add(_end);
        AutomationProperties.SetName(this, "Tools");
        _drag = new DragList(_groups, keys => GroupsMoved?.Invoke(keys), across: true);
    }

    public const string SettingsIcon = "I.Settings";

    private const double GlyphSize = 19;

    public void Build(IReadOnlyList<NavGroup> groups, NavGroup settings, FrameworkElement updateButton)
    {
        string signature = ThemeManager.Current.Id + "|" + string.Join("|", groups.Append(settings).Select(g => g.Key + ":" + g.Title + ":" + string.Join(",", g.Items.Select(i => i.Key + "=" + i.Title))));
        if (signature == _signature) return;
        _signature = signature;
        _menu?.SetCurrentValue(Popup.IsOpenProperty, false);
        _groups.Children.Clear();
        _end.Children.Clear();
        _entries.Clear();
        _drag.Clear();
        foreach (var group in groups)
        {
            var entry = MakeEntry(group);
            _groups.Children.Add(entry.Box);
            _drag.Add(entry.Box, entry.Box, group.Key);
        }
        _end.Children.Add(MakeEntry(settings, SettingsIcon).Box);
        if (updateButton.Parent is Panel old) old.Children.Remove(updateButton);
        updateButton.VerticalAlignment = VerticalAlignment.Center;
        updateButton.Margin = new Thickness(PartGap, 0, 0, 0);
        WindowChrome.SetIsHitTestVisibleInChrome(updateButton, true);
        _end.Children.Add(updateButton);
        Paint();
    }

    private Entry MakeEntry(NavGroup group, string? glyphKey = null)
    {
        var face = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        TextBlock? text = null;
        Icon? glyph = null;
        if (glyphKey is null)
        {
            text = new TextBlock { Text = group.Title, VerticalAlignment = VerticalAlignment.Center, FontSize = TextSize, FontWeight = FontWeights.ExtraBold };
            text.SetResourceReference(TextBlock.FontStyleProperty, "HeadingStyle");
            text.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
            TextCase.SetTight(text, true);
            TextCase.SetUpper(text, true);
            face.Children.Add(text);
        }
        else
        {
            glyph = new Icon { Data = Ui.Res<Geometry>(glyphKey), Width = GlyphSize, Height = GlyphSize, VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(glyph);
        }
        Icon? chevron = null;
        if (group.IsMenu)
        {
            chevron = new Icon { Data = Ui.Res<Geometry>("I.ChevronDown"), Width = ChevronSize, Height = ChevronSize, Margin = new Thickness(6, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(chevron);
        }
        var box = new Tile
        {
            Child = face, Padding = new Thickness(Lead, 0, Lead, 0), Cursor = Cursors.Hand, Focusable = true, Background = Brushes.Transparent,
            FocusVisualStyle = Ui.Res<Style>("FocusRing"),
        };
        AutomationProperties.SetName(box, group.Title);
        if (glyph is not null) box.ToolTip = group.Title;
        KeyboardNavigation.SetIsTabStop(box, true);
        WindowChrome.SetIsHitTestVisibleInChrome(box, true);
        var entry = new Entry { Group = group, Box = box, Text = text, Glyph = glyph, Chevron = chevron };
        box.MouseEnter += (_, _) => { entry.Hover = true; Paint(entry); };
        box.MouseLeave += (_, _) => { entry.Hover = false; Paint(entry); };
        box.GotKeyboardFocus += (_, _) => { entry.Hover = true; Paint(entry); };
        box.LostKeyboardFocus += (_, _) => { entry.Hover = box.IsMouseOver; Paint(entry); };
        box.MouseLeftButtonUp += (_, e) => { if (_drag.Dragging) return; e.Handled = true; Activate(entry, keyboard: false); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space or Key.Down) || e.OriginalSource != box) return;
            if (e.Key == Key.Down && !group.IsMenu) return;
            e.Handled = true;
            Activate(entry, keyboard: true);
        };
        _entries.Add(entry);
        Paint(entry);
        return entry;
    }

    private void Activate(Entry entry, bool keyboard)
    {
        if (!entry.Group.IsMenu)
        {
            if (entry.Group.Items.Count == 1) Picked?.Invoke(entry.Group.Items[0]);
            return;
        }
        if (!keyboard && ReferenceEquals(_closedEntry, entry) && Environment.TickCount64 - _closedAt < 250) return;
        OpenMenu(entry, keyboard);
    }

    private void OpenMenu(Entry entry, bool keyboard)
    {
        var list = new StackPanel();
        var drag = new DragList(list, keys => ItemsMoved?.Invoke(entry.Group.Key, keys));
        var popup = new Popup { PlacementTarget = entry.Box, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, UseLayoutRounding = true };
        Ui.DropBelow(popup);
        FrameworkElement? first = null;
        foreach (var item in entry.Group.Items)
        {
            var row = MenuRow(item, IsCurrent(item), () => drag.Dragging, () =>
            {
                popup.IsOpen = false;
                Picked?.Invoke(item);
            });
            first ??= row;
            list.Children.Add(row);
            if (entry.Group.Movable) drag.Add(row, row, item.Key);
        }
        var frame = new Tile
        {
            Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(4), BorderThickness = new Thickness(1, 2, 1, 1),
            MinWidth = Math.Max(MenuMinWidth, entry.Box.ActualWidth), Child = list,
        };
        frame.SetResourceReference(BackgroundProperty, "TileRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var accentEdge = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        accentEdge.SetResourceReference(BackgroundProperty, "Accent");
        var face = new Grid();
        face.Children.Add(frame);
        face.Children.Add(accentEdge);
        popup.Child = face;
        popup.Closed += (_, _) =>
        {
            _closedEntry = entry;
            _closedAt = Environment.TickCount64;
            if (ReferenceEquals(_menu, popup)) { _menu = null; _menuEntry = null; Paint(entry); }
        };
        _menu = popup;
        _menuEntry = entry;
        WindowScale.Follow(popup);
        popup.IsOpen = true;
        Paint(entry);
        if (keyboard && list.Children.OfType<FrameworkElement>().FirstOrDefault(r => r.Tag is true) is { } current) current.Focus();
        else if (keyboard) first?.Focus();
    }

    private bool IsCurrent(NavItem item) =>
        string.Equals(item.Page, _page, StringComparison.OrdinalIgnoreCase)
        && (item.Section is null || string.Equals(item.Section, _section, StringComparison.OrdinalIgnoreCase));

    private static Border MenuRow(NavItem item, bool current, Func<bool> dragging, Action pick)
    {
        var text = TextFit.TipWhenTrimmed(new TextBlock { Text = item.Title, Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        text.SetResourceReference(TextBlock.ForegroundProperty, current ? "Heading" : "Text");
        var row = new Tile
        {
            Height = RowHeight, Child = text, Padding = new Thickness(11, 0, 16, 0), Cursor = Cursors.Hand, Focusable = true,
            FocusVisualStyle = Ui.Res<Style>("FocusRing"), Tag = current,
        };
        AutomationProperties.SetName(row, item.Title);
        KeyboardNavigation.SetIsTabStop(row, true);
        row.SetResourceReference(Border.BorderBrushProperty, "Accent");
        void Paint(bool hover)
        {
            bool striped = current || hover;
            row.BorderThickness = new Thickness(striped ? 3 : 0, 0, 0, 0);
            text.Margin = new Thickness(striped ? -3 : 0, 0, 0, 0);
            if (current) row.SetResourceReference(BackgroundProperty, "AccentSoft");
            else if (hover) row.SetResourceReference(BackgroundProperty, "HoverRaised");
            else row.Background = Brushes.Transparent;
            if (!current) text.SetResourceReference(TextBlock.ForegroundProperty, hover ? "Heading" : "Text");
        }
        Paint(false);
        row.MouseEnter += (_, _) => Paint(true);
        row.MouseLeave += (_, _) => Paint(false);
        row.GotKeyboardFocus += (_, _) => Paint(true);
        row.LostKeyboardFocus += (_, _) => Paint(row.IsMouseOver);
        row.MouseLeftButtonUp += (_, e) => { if (dragging()) return; e.Handled = true; pick(); };
        row.KeyDown += (_, e) =>
        {
            if (e.OriginalSource != row) return;
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; pick(); }
            else if (e.Key is Key.Down or Key.Up && row.Parent is Panel list)
            {
                int at = list.Children.IndexOf(row) + (e.Key == Key.Down ? 1 : -1);
                if (at >= 0 && at < list.Children.Count) ((UIElement)list.Children[at]).Focus();
                e.Handled = true;
            }
        };
        return row;
    }

    public void Select(string? page, string? section)
    {
        _page = page;
        _section = section;
        Paint();
    }

    private void Paint()
    {
        foreach (var entry in _entries) Paint(entry);
    }

    private void Paint(Entry entry)
    {
        entry.Selected = entry.Group.Items.Any(i => string.Equals(i.Page, _page, StringComparison.OrdinalIgnoreCase));
        string tone = entry.Selected ? "AccentText" : entry.Hover || ReferenceEquals(_menuEntry, entry) ? "Heading" : "TextMuted";
        entry.Text?.SetResourceReference(TextBlock.ForegroundProperty, tone);
        entry.Glyph?.SetResourceReference(Control.ForegroundProperty, tone);
        entry.Chevron?.SetResourceReference(Control.ForegroundProperty, tone);
    }

    private static readonly string[] AccentKeys = ["Accent", "AccentSoft", "AccentText"];

    public void UseAccent(ResourceDictionary? theme)
    {
        CopyAccent(Resources, theme);
        Paint();
    }

    public static void CopyAccent(ResourceDictionary into, ResourceDictionary? theme)
    {
        foreach (var key in AccentKeys)
        {
            if (theme?[key] is { } value) into[key] = value;
            else into.Remove(key);
        }
    }
}
