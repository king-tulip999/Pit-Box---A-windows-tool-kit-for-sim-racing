using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.SimRacing.IracingFiles;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal static class IracingKeybindsView
{
    public static UIElement Build(AppServices app, Workspace ws)
    {
        var host = new Border();
        void Rebuild() => host.Child = Content(app, ws, Rebuild);
        Rebuild();
        return host;
    }

    private static UIElement Content(AppServices app, Workspace ws, Action rebuild)
    {
        var stack = new StackPanel();
        var keys = app.IracingKeys;
        keys.Refresh();

        var info = new StackPanel();
        var heading = new TrimRow();
        var title = new TextBlock { Text = "iRacing keybinds", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Ui.AddInfo(title, "iRacing saves its own copy of the keys when it exits, so changes here only work while it's closed. Wheel and button-box bindings are shown but set in iRacing. To bind a number-pad key, turn Num Lock off first.");
        heading.Children.Add(title);
        var saved = Ui.Chip("Saved as you change them", "SageSoft", "SageDeep");
        saved.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(saved);
        info.Children.Add(heading);
        var about = new TextBlock
        {
            Text = "Only while iRacing is closed; a backup is made first.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        };
        if (keys.FilePath is { } path) about.ToolTip = TextCase.VerbatimTip(path);
        info.Children.Add(about);
        if (ProfileRow(app, rebuild) is { } row) info.Children.Add(row);
        if (keys.CarFiles > 0)
            info.Children.Add(Note($"{keys.CarFiles} car{(keys.CarFiles == 1 ? " uses its" : "s use their")} own controls (setups folder); those aren't changed here."));
        if (ws.SimRunning)
            info.Children.Add(Note("iRacing is running: close it to change keybinds.", warning: true));
        if (keys.Error is { } error)
            info.Children.Add(Note(error, warning: true));
        stack.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(15, 11, 15, 11), Margin = new Thickness(0, 0, 0, 10), Child = info });

        if (keys.File is not null)
            stack.Children.Add(new KeybindTree(app, e => e.Owner == HotkeyOwner.Iracing, skip: 1));
        return stack;
    }

    public static FrameworkElement? ProfileRow(AppServices app, Action changed)
    {
        var keys = app.IracingKeys;
        if (keys.Profiles.Count == 0) return null;
        var choices = keys.Profiles.Select(p => new Choice(p, p == keys.DefaultProfile ? p + " (iRacing's default)" : p)).ToList();
        choices.Add(new Choice("", "No profile (Documents\\iRacing\\controls.cfg)"));
        var pick = Ui.Combo(choices, keys.Profile ?? "", c =>
        {
            keys.SelectProfile(c.Id == keys.DefaultProfile ? null : c.Id);
            app.Save();
            changed();
        }, 211);
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var label = new TextBlock { Text = "Control profile", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        DockPanel.SetDock(pick, Dock.Left);
        row.Children.Add(pick);
        row.Children.Add(new TextBlock
        {
            Text = "Each iRacing control profile has its own keys, and cars can use different profiles. Changes go to the profile picked here.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        });
        return row;
    }

    private static TextBlock Note(string text, bool warning = false) => new()
    {
        Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0),
        Foreground = Ui.Res<Brush>(warning ? "ApricotDeep" : "TextMuted"),
    };
}
