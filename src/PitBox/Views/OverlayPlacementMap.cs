using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PitBox.Config;
using PitBox.Theme;
using WinForms = System.Windows.Forms;

namespace PitBox.Views;

internal sealed class OverlayPlacementMap : Grid
{
    internal readonly record struct Screen(string Id, Rect Bounds, bool Primary, int Number);

    private const double MapHeight = 150, Gap = 6, Inset = 6, MiniWidth = 40, MiniHeight = 20;

    private readonly Func<OverlayLayout> _layout;
    private readonly Action _changed;
    private readonly List<Screen> _screens;
    private readonly Canvas _canvas = new() { Height = MapHeight, ClipToBounds = true };
    private readonly List<Border> _monitors = new();
    private readonly Border _mini;
    private readonly TranslateTransform _glide = new();
    private readonly TextBlock _note;

    public OverlayPlacementMap(Func<OverlayLayout> layout, Action changed)
    {
        _layout = layout;
        _changed = changed;
        _screens = ReadScreens();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_canvas);
        _note = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _note.SetResourceReference(TextBlock.ForegroundProperty, "WarnDeep");
        SetRow(_note, 1);
        Children.Add(_note);

        foreach (var screen in _screens)
        {
            var monitor = Hairline.Frame(new Border { SnapsToDevicePixels = true });
            monitor.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            var face = new Grid();
            var tag = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var number = new TextBlock { Text = screen.Number.ToString(), FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
            number.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            tag.Children.Add(number);
            if (screen.Primary) tag.Children.Add(new TextBlock { Text = ThemeManager.Case("Primary"), Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center });
            face.Children.Add(tag);
            face.ColumnDefinitions.Add(new ColumnDefinition());
            face.ColumnDefinitions.Add(new ColumnDefinition());
            face.RowDefinitions.Add(new RowDefinition());
            face.RowDefinitions.Add(new RowDefinition());
            SetColumnSpan(tag, 2);
            SetRowSpan(tag, 2);
            foreach (var corner in new[] { OverlayCorner.TopLeft, OverlayCorner.TopRight, OverlayCorner.BottomLeft, OverlayCorner.BottomRight })
                face.Children.Add(Target(screen, corner));
            monitor.Child = face;
            _monitors.Add(monitor);
            _canvas.Children.Add(monitor);
        }

        var lines = new StackPanel { Margin = new Thickness(4, 4, 4, 0) };
        foreach (var (width, brush) in new[] { (0.8, "Sage"), (1.0, "TextMuted"), (0.6, "TextMuted") })
        {
            var line = new Border { Height = 2, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, Width = (MiniWidth - 11) * width };
            line.SetResourceReference(Border.BackgroundProperty, brush);
            lines.Children.Add(line);
        }
        _mini = new Border { Width = MiniWidth, Height = MiniHeight, BorderThickness = new Thickness(3, 1, 1, 1), Child = lines, IsHitTestVisible = false, RenderTransform = _glide, Background = Brushes.Transparent };
        _mini.SetResourceReference(Border.BorderBrushProperty, "Accent");
        _canvas.Children.Add(_mini);
        _canvas.SizeChanged += (_, e) => { if (e.WidthChanged) Place(animate: false); };
    }

    private static List<Screen> ReadScreens()
    {
        try
        {
            int n = 1;
            return WinForms.Screen.AllScreens.OrderBy(s => s.Bounds.X)
                .Select(s => new Screen(s.DeviceName, new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Primary, n++)).ToList();
        }
        catch
        {
            return new List<Screen> { new("primary", new Rect(0, 0, 1920, 1080), true, 1) };
        }
    }

    private Border Target(Screen screen, OverlayCorner corner)
    {
        bool right = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight, bottom = corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;
        string name = $"{Words(corner)} of monitor {screen.Number}";
        var target = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, ToolTip = ThemeManager.Case(name), FocusVisualStyle = Ui.Res<Style>("FocusRing") };
        AutomationProperties.SetName(target, name);
        SetColumn(target, right ? 1 : 0);
        SetRow(target, bottom ? 1 : 0);
        target.MouseEnter += (_, _) => target.SetResourceReference(Border.BackgroundProperty, "Hover");
        target.MouseLeave += (_, _) => target.Background = Brushes.Transparent;
        target.MouseLeftButtonUp += (_, e) => { Pick(screen, corner); e.Handled = true; };
        target.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Pick(screen, corner);
            e.Handled = true;
        };
        return target;
    }

    private static string Words(OverlayCorner corner) => corner switch
    {
        OverlayCorner.TopRight => "Top right",
        OverlayCorner.BottomLeft => "Bottom left",
        OverlayCorner.BottomRight => "Bottom right",
        _ => "Top left",
    };

    private void Pick(Screen screen, OverlayCorner corner)
    {
        var layout = _layout();
        string id = screen.Primary ? "primary" : screen.Id;
        if (layout.Corner == corner && string.Equals(layout.Monitor, id, StringComparison.OrdinalIgnoreCase) && Chosen(layout) == screen) return;
        layout.Corner = corner;
        layout.Monitor = id;
        layout.OffsetX = layout.OffsetY = 16;
        Place(animate: true);
        _changed();
    }

    private Screen? Chosen(OverlayLayout layout)
    {
        foreach (var s in _screens)
            if (layout.Monitor != "primary" && string.Equals(s.Id, layout.Monitor, StringComparison.OrdinalIgnoreCase)) return s;
        foreach (var s in _screens)
            if (s.Primary) return s;
        return _screens.Count > 0 ? _screens[0] : null;
    }

    internal static List<Rect> Fit(IReadOnlyList<Rect> screens, double width, double height, double gap)
    {
        if (screens.Count == 0 || width <= 0 || height <= 0) return new List<Rect>();
        double left = screens.Min(s => s.Left), top = screens.Min(s => s.Top);
        double spanW = screens.Max(s => s.Right) - left, spanH = screens.Max(s => s.Bottom) - top;
        double k = Math.Min(width / spanW, height / spanH);
        double ox = (width - spanW * k) / 2, oy = (height - spanH * k) / 2;
        return screens.Select(s =>
        {
            var r = new Rect(ox + (s.Left - left) * k, oy + (s.Top - top) * k, s.Width * k, s.Height * k);
            r.Inflate(-gap / 2, -gap / 2);
            return new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Max(1, Math.Round(r.Width)), Math.Max(1, Math.Round(r.Height)));
        }).ToList();
    }

    private void Place(bool animate)
    {
        var boxes = Fit(_screens.Select(s => s.Bounds).ToList(), _canvas.ActualWidth, MapHeight, Gap);
        if (boxes.Count != _screens.Count) return;
        var layout = _layout();
        var chosen = Chosen(layout);
        for (int i = 0; i < boxes.Count; i++)
        {
            var monitor = _monitors[i];
            Canvas.SetLeft(monitor, boxes[i].X);
            Canvas.SetTop(monitor, boxes[i].Y);
            monitor.Width = boxes[i].Width;
            monitor.Height = boxes[i].Height;
            monitor.SetResourceReference(Border.BorderBrushProperty, _screens[i] == chosen ? "Accent" : "LineStrong");
        }

        bool missing = layout.Monitor != "primary" && !_screens.Any(s => string.Equals(s.Id, layout.Monitor, StringComparison.OrdinalIgnoreCase));
        _note.Text = "The saved monitor isn't connected, so the overlay shows on the primary monitor.";
        _note.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;

        int index = chosen is { } c ? _screens.IndexOf(c) : 0;
        if (index < 0) return;
        var box = boxes[index];
        bool right = layout.Corner is OverlayCorner.TopRight or OverlayCorner.BottomRight, bottom = layout.Corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;
        double x = right ? box.Right - MiniWidth - Inset : box.X + Inset, y = bottom ? box.Bottom - MiniHeight - Inset : box.Y + Inset;
        var from = new Point(Canvas.GetLeft(_mini), Canvas.GetTop(_mini));
        Canvas.SetLeft(_mini, x);
        Canvas.SetTop(_mini, y);
        if (!animate || !Motion.Flourish || double.IsNaN(from.X)) return;
        var duration = TimeSpan.FromMilliseconds(380);
        _glide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X - x, 0, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        _glide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y - y, 0, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
    }
}
