using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Core.Controls;

namespace Core.Views.HudSettings;

internal static class HelpTip
{
    public static FrameworkElement For(string text, string? name = null)
    {
        var icon = new Icon { Data = Ui.Res<Geometry>("I.Info"), Width = 13, Height = 13 };
        icon.SetResourceReference(Control.ForegroundProperty, "Text");
        var box = new Border
        {
            Child = icon, Padding = new Thickness(2), Margin = new Thickness(3, 0, 0, 0), Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Help, Focusable = true, ToolTip = Content(text),
            FocusVisualStyle = Ui.Res<Style>("FocusRing"),
        };
        KeyboardNavigation.SetIsTabStop(box, true);
        ToolTipService.SetShowsToolTipOnKeyboardFocus(box, true);
        ToolTipService.SetInitialShowDelay(box, 150);
        ToolTipService.SetShowDuration(box, 60000);
        System.Windows.Automation.AutomationProperties.SetName(box, name is null ? "More about this" : "More about " + name);
        System.Windows.Automation.AutomationProperties.SetHelpText(box, text);
        box.GotKeyboardFocus += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, "AccentText");
        box.LostKeyboardFocus += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, "Text");
        box.MouseEnter += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, "AccentText");
        box.MouseLeave += (_, _) => { if (!box.IsKeyboardFocused) icon.SetResourceReference(Control.ForegroundProperty, "Text"); };
        return box;
    }

    private const int TermLength = 24;

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int TermEnd(string line)
    {
        int colon = line.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 && colon <= TermLength ? colon : -1;
    }

    public static bool IsList(string text) => Lines(text).Length >= 2;

    private static object Content(string text)
    {
        var lines = Lines(text);
        if (lines.Length < 2) return text;
        var stack = new StackPanel { MaxWidth = 360 };
        for (int i = 0; i < lines.Length; i++)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, i == 0 ? 0 : 7, 0, 0) };
            string line = lines[i];
            int colon = TermEnd(line);
            if (colon > 0)
            {
                var term = new Run(line[..(colon + 1)]) { FontWeight = FontWeights.SemiBold };
                term.SetResourceReference(TextElement.ForegroundProperty, "Heading");
                block.Inlines.Add(term);
                block.Inlines.Add(new Run(line[(colon + 1)..]));
            }
            else block.Text = line;
            stack.Children.Add(block);
        }
        return stack;
    }

    public static FrameworkElement List(string text)
    {
        var lines = Lines(text);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 150 });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < lines.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string line = lines[i];
            int colon = TermEnd(line);
            var top = new Thickness(0, i == 0 ? 0 : 9, 0, 0);
            var body = new TextBlock { Text = colon > 0 ? line[(colon + 2)..] : line, Style = Ui.Res<Style>("Body"), TextWrapping = TextWrapping.Wrap, Margin = top };
            Grid.SetRow(body, i);
            if (colon > 0)
            {
                var term = new TextBlock { Text = line[..colon], Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top.Top, 14, 0) };
                term.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                Grid.SetRow(term, i);
                grid.Children.Add(term);
                Grid.SetColumn(body, 1);
            }
            else
            {
                body.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
                Grid.SetColumnSpan(body, 2);
            }
            grid.Children.Add(body);
        }
        return grid;
    }
}
