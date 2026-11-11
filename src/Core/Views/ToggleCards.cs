using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Controls;
using Core.Theme;

namespace Core.Views;

internal static class ToggleCards
{
    public const double OffOpacity = 0.72;

    private const double ChoiceOffOpacity = 0.85, LockedOpacity = 0.45;

    internal static TextBlock Strong(TextBlock text)
    {
        text.FontWeight = FontWeights.SemiBold;
        return text;
    }

    internal static Border Choice(string title, string caption, bool on, bool enabled, Action<bool> changed, FrameworkElement? below = null, string? tip = null)
    {
        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(TextFit.TipWhenTrimmed(Strong(new TextBlock
        {
            Text = title, Style = Ui.Res<Style>("H3"), FontSize = 15.5, TextTrimming = TextTrimming.CharacterEllipsis,
        })));
        face.Children.Add(new TextBlock
        {
            Text = caption, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 3, 0, 0),
        });
        var lamp = new Lamp();
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(WithLamp(lamp, face));
        if (below is not null) body.Children.Add(below);
        var tile = new Border
        {
            Child = body, Focusable = enabled, FocusVisualStyle = Ui.Res<Style>("FocusRing"), Cursor = enabled ? System.Windows.Input.Cursors.Hand : null,
            BorderThickness = Hairline.Edges(), Padding = new Thickness(10, 9, 10, 9), Background = Ui.Res<Brush>("SurfaceAlt"),
        };
        if (tip is not null) tile.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(tile, title);
        void Apply()
        {
            bool lit = on && enabled;
            tile.BorderBrush = Ui.Res<Brush>(enabled && tile.IsMouseOver ? "LineStrong" : "Line");
            lamp.On = lit;
            lamp.Hot = enabled && tile.IsMouseOver;
            face.Opacity = !enabled ? LockedOpacity : on ? 1 : ChoiceOffOpacity;
            if (below is not null) below.Opacity = lit ? 1 : LockedOpacity;
            System.Windows.Automation.AutomationProperties.SetItemStatus(tile, on ? "On" : "Off");
        }
        void Flip()
        {
            on = !on;
            Apply();
            changed(on);
        }
        Apply();
        tile.MouseEnter += (_, _) => Apply();
        tile.MouseLeave += (_, _) => Apply();
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (!enabled || e.Handled || (below is not null && e.OriginalSource is DependencyObject source && below.IsAncestorOf(source))) return;
            e.Handled = true;
            Flip();
        };
        tile.KeyDown += (_, e) =>
        {
            if (!enabled || e.Key is not (System.Windows.Input.Key.Space or System.Windows.Input.Key.Enter)) return;
            e.Handled = true;
            Flip();
        };
        return tile;
    }

    private static readonly ConditionalWeakTable<CheckBox, RoutedEventHandler> Painters = new();

    public static bool IsSwitch(UIElement control) => control is CheckBox box && box.Style == Ui.Res<Style>("Switch");

    public static TextBlock Title(string text, bool centered) => Strong(new TextBlock
    {
        Text = text, Style = Ui.Res<Style>("H3"), FontSize = 15.5, TextWrapping = TextWrapping.Wrap,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
    });

    public static TextBlock Caption(string text, bool centered) => new()
    {
        Text = text, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    public static CheckBox Card(string title, string? caption, bool on, Action<bool> changed) =>
        Dress(Ui.Switch(on, changed), Title(title, false), caption);

    public static CheckBox Chip(string label, bool on, Action<bool> changed, string? tip = null)
    {
        var chip = Compact(Ui.Switch(on, changed), label);
        chip.Margin = new Thickness(0, 0, 5, 5);
        chip.ToolTip = tip;
        return chip;
    }

    public static CheckBox Compact(CheckBox box, string label)
    {
        var title = Title(label, false);
        title.FontSize = 13.5;
        Dress(box, title, null);
        box.Padding = new Thickness(9, 2, 10, 2);
        box.MinHeight = Ui.ControlHeight;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        return box;
    }

    public static CheckBox FromBox(CheckBox box, string? caption = null)
    {
        string title = box.Content as string ?? "";
        box.Content = null;
        return Row(box, RowTitle(title), caption);
    }

    public static TextBlock RowTitle(string text) => new() { Text = text, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };

    public static CheckBox Row(CheckBox box, FrameworkElement? heading, string? caption, FrameworkElement? below = null, FrameworkElement? trailing = null)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, heading is null ? 0 : 15, 0) };
        if (heading is not null) text.Children.Add(heading);
        if (!string.IsNullOrEmpty(caption))
        {
            var line = Caption(caption, false);
            line.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(line);
        }
        if (below is not null) text.Children.Add(below);
        var lamp = new Lamp
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, heading is null ? 0 : 10, 0),
        };
        var row = new DockPanel { LastChildFill = true, Background = Brushes.Transparent };
        DockPanel.SetDock(lamp, Dock.Left);
        row.Children.Add(lamp);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }
        row.Children.Add(text);
        box.Style = Ui.Res<Style>("ToggleRow");
        box.Content = row;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (heading is TextBlock { Text.Length: > 0 } title) Ui.NameFor(box, title.Text);
        box.MouseEnter += (_, _) => lamp.Hot = true;
        box.MouseLeave += (_, _) => lamp.Hot = false;
        Watch(box, on => lamp.On = on);
        return box;
    }

    public static DockPanel WithLamp(Lamp lamp, UIElement text)
    {
        lamp.VerticalAlignment = VerticalAlignment.Center;
        lamp.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(lamp, Dock.Left);
        return new DockPanel { LastChildFill = true, Children = { lamp, text } };
    }

    public static CheckBox CompactBox(CheckBox box)
    {
        string title = box.Content as string ?? "";
        box.Content = null;
        return Compact(box, title);
    }

    public static CheckBox Dress(CheckBox box, FrameworkElement heading, string? caption, FrameworkElement? below = null, Action<TextBlock>? captionText = null, FrameworkElement? trailing = null)
    {
        bool tall = !string.IsNullOrEmpty(caption);
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lamp = new Lamp { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        top.Children.Add(lamp);
        heading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(heading, 1);
        top.Children.Add(heading);
        TextBlock? text = null;
        if (tall)
        {
            text = Caption(caption!, false);
            text.Margin = new Thickness(0, 3, 0, 0);
            captionText?.Invoke(text);
            Grid.SetColumn(text, 1);
            Grid.SetRow(text, 1);
            top.Children.Add(text);
            Grid.SetRowSpan(lamp, 2);
        }
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Top;
            trailing.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(trailing, 2);
            Grid.SetRowSpan(trailing, 2);
            top.Children.Add(trailing);
        }
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        body.Children.Add(top);
        if (below is not null) body.Children.Add(below);
        box.Style = Ui.Res<Style>("ToggleCard");
        box.Content = body;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.VerticalAlignment = VerticalAlignment.Stretch;
        box.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        box.VerticalContentAlignment = VerticalAlignment.Top;
        if (heading is TextBlock { Text.Length: > 0 } title) Ui.NameFor(box, title.Text);
        box.MouseEnter += (_, _) => lamp.Hot = true;
        box.MouseLeave += (_, _) => lamp.Hot = false;
        Watch(box, on =>
        {
            lamp.On = on;
            Fade(heading, on ? 1 : OffOpacity, on ? Lamp.RiseMs : Lamp.FallMs);
            if (text is not null) Fade(text, on ? 1 : OffOpacity, on ? Lamp.RiseMs : Lamp.FallMs);
        });
        return box;
    }

    private static void Fade(UIElement face, double to, double ms)
    {
        if (face.IsVisible)
            Motion.To(face, UIElement.OpacityProperty, to, ms, ease: Motion.Smooth);
        else
        {
            face.BeginAnimation(UIElement.OpacityProperty, null);
            face.Opacity = to;
        }
    }

    private static void Watch(CheckBox box, Action<bool> apply)
    {
        void Apply()
        {
            bool on = box.IsChecked == true;
            apply(on);
            AutomationProperties.SetItemStatus(box, on ? "On" : "Off");
        }
        if (Painters.TryGetValue(box, out var old))
        {
            box.Checked -= old;
            box.Unchecked -= old;
        }
        RoutedEventHandler paint = (_, _) => Apply();
        Painters.AddOrUpdate(box, paint);
        box.Checked += paint;
        box.Unchecked += paint;
        Apply();
    }
}
