using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PitBox.Views;

internal static class WindowScale
{
    public const double Fill = 0.5, HairlineBand = 0.05;
    public const string Key = "WindowScaleTransform";

    public static readonly ScaleTransform Transform = new(1, 1);

    public static double Current => Transform.ScaleX;

    public static event Action? Changed;

    internal static double For(double width, double height)
    {
        if (!(width > 0) || !(height > 0)) return 1;
        double most = Math.Max(1, Math.Min(width / MainWindow.MinWindowWidth, height / MainWindow.MinWindowHeight));
        return Math.Round(Math.Clamp(Fill * width / Ui.PageWidth, 1, most), 4);
    }

    public static void Set(double scale)
    {
        if (Math.Abs(Transform.ScaleX - scale) < 1e-6) return;
        Transform.ScaleX = Transform.ScaleY = scale;
        Changed?.Invoke();
    }

    public static void Publish()
    {
        if (Application.Current?.Resources is not { } res || res[Key] is ScaleTransform { ScaleX: var now } && Math.Abs(now - Current) < 1e-6) return;
        var copy = new ScaleTransform(Current, Current);
        copy.Freeze();
        res[Key] = copy;
    }

    public static void ClosePopups(Window owner)
    {
        var open = new List<Popup>();
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is FrameworkElement { Parent: Popup { IsOpen: true } popup }
                && Window.GetWindow(popup.PlacementTarget ?? (DependencyObject)popup) == owner)
                open.Add(popup);
        foreach (var popup in open)
        {
            if (popup.TemplatedParent is System.Windows.Controls.ComboBox combo) combo.IsDropDownOpen = false;
            else if (popup.TemplatedParent is System.Windows.Controls.MenuItem item) item.IsSubmenuOpen = false;
            else if (popup.Child is System.Windows.Controls.ContextMenu menu) menu.IsOpen = false;
            else if (popup.Child is System.Windows.Controls.ToolTip tip) tip.IsOpen = false;
            else if (popup.TemplatedParent is null && !popup.StaysOpen) popup.IsOpen = false;
        }
    }

    public static void Follow(Popup popup)
    {
        if (VisualTreeHelper.GetParent(popup) is null && popup.Child is FrameworkElement child)
            child.SetResourceReference(FrameworkElement.LayoutTransformProperty, Key);
    }

    public static double Banded => Math.Floor(Current / HairlineBand + 1e-9) * HairlineBand;

    public static double Of(Visual visual) =>
        Window.GetWindow(visual) is { } window && window is not MainWindow ? 1 : Current;
}
