using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Core.Controls;

internal static class SliderFace
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(SliderFace), new PropertyMetadata(null));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static void InstallDragAnywhere() =>
        EventManager.RegisterClassHandler(typeof(Slider), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(GrabAnywhere));

    private static void GrabAnywhere(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled || slider.Template?.FindName("PART_Track", slider) is not Track { Thumb: { } thumb } track) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != slider; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d == thumb) return;
        double value = track.ValueFromPoint(e.GetPosition(track));
        if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0)
            value = slider.Minimum + Math.Round((value - slider.Minimum) / slider.TickFrequency) * slider.TickFrequency;
        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        slider.Focus();
        slider.UpdateLayout();
        thumb.RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = thumb });
        e.Handled = true;
    }
}
