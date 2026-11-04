using System.Windows;
using System.Windows.Controls;
using PitBox.SimRacing.Hud.Editors;
using PitBox.Theme;

namespace PitBox.Views.HudSettings;

internal sealed class PresetRow : WrapPanel
{
    private readonly List<(string Name, RadioButton Button)> _buttons = new();
    private readonly Border _custom;
    private readonly TextBlock _changed = new() { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button _back;
    private readonly Border _pending;
    private readonly IReadOnlyList<HudPreset> _presets;
    private HudPreset? _nearest;
    private bool _quiet;

    public PresetRow(IReadOnlyList<HudPreset> presets, Action<HudPreset> apply)
    {
        _presets = presets;
        Margin = new Thickness(0, 0, 0, 10);
        var label = new TextBlock { Text = "Preset", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        Children.Add(label);
        var segmented = Ui.Segmented(presets.Select(p => p.Name).ToList(), -1, i => { if (!_quiet) apply(presets[i]); }, "preset",
            presets.Select(p => (string?)Tip(p.Name)).ToList());
        var buttons = ((Panel)segmented.Child).Children.OfType<RadioButton>().ToList();
        for (int i = 0; i < presets.Count; i++) _buttons.Add((presets[i].Name, buttons[i]));
        Children.Add(segmented);
        _custom = new Border
        {
            Child = new TextBlock { Text = "Modified", Style = Ui.Res<Style>("Caption"),VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Your own settings: they don't match a preset",
        };
        Children.Add(_custom);
        _changed.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        _back = Ui.LabeledButton("I.Undo", "Back", () => { if (_nearest is { } p) apply(p); }, "GhostButton");
        _back.Margin = new Thickness(10, 0, 0, 0);
        _changed.Margin = new Thickness(0);
        _pending = Ui.PendingBar(new Border
        {
            Margin = new Thickness(10, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _changed, _back } },
        });
        _pending.Padding = new Thickness(11, 5, 5, 5);
        Children.Add(_pending);
    }

    private static string Tip(string name) => name switch
    {
        HudPreset.Minimal => "The fewest items: content and layout only, place and visibility stay",
        HudPreset.Everything => "Every item it can show: content and layout only, place and visibility stay",
        _ => "As the widget starts: content and layout only, place and visibility stay",
    };

    public void Show(string? current, (string Name, IReadOnlyList<string> Changed)? nearest = null)
    {
        string? shown = current ?? nearest?.Name;
        int changed = current is null ? nearest?.Changed.Count ?? 0 : 0;
        _quiet = true;
        foreach (var (name, button) in _buttons) button.IsChecked = name == shown;
        _quiet = false;
        _nearest = changed > 0 ? _presets.FirstOrDefault(p => p.Name == shown) : null;
        _custom.Visibility = shown is null ? Visibility.Visible : Visibility.Collapsed;
        _pending.Visibility = _nearest is null ? Visibility.Collapsed : Visibility.Visible;
        if (_nearest is null) return;
        TextCase.Set(_changed, SimRacing.Race.RaceFormat.Count(changed, "setting") + " changed");
        _back.ToolTip = $"Back to {_nearest.Name}: content and layout only, place and visibility stay";
        _back.Content = Ui.IconLabel("I.Undo", "Back to " + _nearest.Name);
    }
}
