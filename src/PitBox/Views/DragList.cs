using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PitBox.Views;

internal sealed class DragList
{
    private readonly Panel _panel;
    private readonly Action<IReadOnlyList<string>> _moved;
    private readonly bool _across;
    private readonly List<(FrameworkElement Item, FrameworkElement Handle, string Key)> _items = new();
    private FrameworkElement? _pressed;
    private Point _start;
    private List<string>? _before;

    public bool Dragging { get; private set; }

    public DragList(Panel panel, Action<IReadOnlyList<string>> moved, bool across = false)
    {
        _panel = panel;
        _moved = moved;
        _across = across;
        panel.PreviewMouseLeftButtonDown += OnDown;
        panel.PreviewMouseMove += OnMove;
        panel.PreviewMouseLeftButtonUp += OnUp;
        panel.LostMouseCapture += (_, _) => { if (Dragging) Finish(commit: true); };
        panel.PreviewKeyDown += OnKey;
    }

    public void Clear() => _items.Clear();

    public void Add(FrameworkElement item, FrameworkElement handle, string key) => _items.Add((item, handle, key));

    private double Along(Point p) => _across ? p.X : p.Y;

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
            if (Math.Abs(Along(at) - Along(_start)) < 6) return;
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
            double mid = Along(child.TranslatePoint(new Point(0, 0), _panel)) + (_across ? child.ActualWidth : child.ActualHeight) / 2;
            if (Along(at) > mid) to++;
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
        var back = _across ? Key.Left : Key.Up;
        var on = _across ? Key.Right : Key.Down;
        if (Keyboard.Modifiers != ModifierKeys.Alt || (e.SystemKey != back && e.SystemKey != on) || Find(e.OriginalSource) is not { } hit || hit.Handle != e.OriginalSource) return;
        int from = _panel.Children.IndexOf(hit.Item), to = from + (e.SystemKey == back ? -1 : 1);
        e.Handled = true;
        if (from < 0 || to < 0 || to >= _panel.Children.Count) return;
        _panel.Children.RemoveAt(from);
        _panel.Children.Insert(to, hit.Item);
        hit.Handle.Focus();
        _moved(Keys());
    }
}
