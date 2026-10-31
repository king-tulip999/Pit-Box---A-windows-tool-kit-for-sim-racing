using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PitBox.Theme;

public static class TipPlacement
{
    public const double Gap = 6, PointerDrop = 20, TextInset = 9;
    public const double AnchorMaxWidth = 360, AnchorMaxHeight = 44;

    public static readonly CustomPopupPlacementCallback Place = Positions;

    private static FrameworkElement? _owner;
    private static Point? _pointer;

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
            new ToolTipEventHandler((sender, _) => Remember(sender as FrameworkElement)), true);
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(CloseWithOwner), true);
    }

    private static void CloseWithOwner(object sender, RoutedEventArgs e)
    {
        if (sender is not ToolTip tip || tip.PlacementTarget is not UIElement owner) return;
        DependencyPropertyChangedEventHandler? hidden = null;
        RoutedEventHandler? closed = null;
        hidden = (_, _) => { if (!owner.IsVisible) tip.IsOpen = false; };
        closed = (_, _) =>
        {
            owner.IsVisibleChanged -= hidden;
            tip.Closed -= closed;
        };
        owner.IsVisibleChanged += hidden;
        tip.Closed += closed;
    }

    private static void Remember(FrameworkElement? owner)
    {
        _owner = owner;
        _pointer = owner is not null && owner.IsMouseOver ? Mouse.GetPosition(owner) : null;
    }

    private static CustomPopupPlacement[] Positions(Size popup, Size target, Point offset)
    {
        double scale = 1;
        var owner = _owner;
        if (owner is not null)
        {
            try { scale = VisualTreeHelper.GetDpi(owner).DpiScaleY; } catch { scale = 1; }
            var expected = new Size(owner.ActualWidth * scale, owner.ActualHeight * scale);
            if (Math.Abs(expected.Width - target.Width) > 2 || Math.Abs(expected.Height - target.Height) > 2) owner = null;
        }
        var layout = Layout(popup, target, scale, owner is TextBlock, owner is null ? null : _pointer);
        return
        [
            new CustomPopupPlacement(new Point(layout.Below.X + offset.X, layout.Below.Y + offset.Y), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(layout.Above.X + offset.X, layout.Above.Y + offset.Y), PopupPrimaryAxis.Horizontal),
        ];
    }

    internal static (Point Below, Point Above) Layout(Size popup, Size target, double scale, bool text, Point? pointer)
    {
        double gap = Gap * scale;
        bool anchored = pointer is null || (target.Width <= AnchorMaxWidth * scale && target.Height <= AnchorMaxHeight * scale);
        if (!anchored && pointer is { } p)
        {
            double px = p.X * scale, py = p.Y * scale;
            double x = Math.Max(0, Math.Min(px - TextInset * scale, target.Width - popup.Width));
            return (new Point(x, py + PointerDrop * scale), new Point(x, py - gap - popup.Height));
        }
        double left = text ? -TextInset * scale : (target.Width - popup.Width) / 2;
        return (new Point(left, target.Height + gap), new Point(left, -gap - popup.Height));
    }
}
