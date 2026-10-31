using System.Windows;
using System.Windows.Controls;
using PitBox.Controls;
using PitBox.SimRacing.Hud.Editors;
using PitBox.Theme;

namespace PitBox.Views.HudSettings;

internal sealed class PresetRow : WrapPanel
{
    private readonly List<(string Name, RadioButton Button)> _buttons = new();
    private readonly Border _custom;
    private readonly TextBlock _changed = new() { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button _back;
    private readonly IReadOnlyList<HudPreset> _presets;
    private HudPreset? _nearest;
    private bool _quiet;

    public PresetRow(IReadOnlyList<HudPreset> presets, Action<HudPreset> apply)
    {
        _presets = presets;
        Margin = new Thickness(0, 0, 0, 10);
        var label = new TextBlock { Text = "Preset", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        Children.Add(label);
        var panel = new WrapPanel();
        string group = "preset-" + Guid.NewGuid().ToString("N");
        foreach (var preset in presets)
        {
            var p = preset;
            var rb = new RadioButton { Content = preset.Name, Style = Ui.Res<Style>("Segment"), GroupName = group, ToolTip = Tip(preset.Name) };
            rb.Checked += (_, _) => { if (!_quiet) apply(p); };
            _buttons.Add((preset.Name, rb));
            panel.Children.Add(rb);
        }
        Children.Add(new Tile { Style = Ui.Res<Style>("Segmented"), Child = panel });
        _custom = new Border
        {
            Child = new TextBlock { Text = "Modified", Style = Ui.Res<Style>("Caption"),VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Your own settings: they don't match a preset",
        };
        Children.Add(_custom);
        _changed.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        _back = Ui.LabeledButton("I.Undo", "Back", () => { if (_nearest is { } p) apply(p); }, "GhostButton");
        _back.Margin = new Thickness(8, 0, 0, 0);
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _changed, _back } });
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
        _changed.Visibility = _back.Visibility = _nearest is null ? Visibility.Collapsed : Visibility.Visible;
        if (_nearest is null) return;
        TextCase.Set(_changed, SimRacing.Race.RaceFormat.Count(changed, "setting") + " changed");
        _back.ToolTip = $"Back to {_nearest.Name}: content and layout only, place and visibility stay";
        _back.Content = Ui.IconLabel("I.Undo", "Back to " + _nearest.Name);
    }
}
