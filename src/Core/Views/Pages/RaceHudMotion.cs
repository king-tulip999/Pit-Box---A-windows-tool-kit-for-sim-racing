using System.Windows;
using System.Windows.Controls;
using Core.Config;

namespace Core.Views.Pages;

internal sealed partial class RaceHudWidgets
{
    private bool _filledOnce;
    private string? _enteredFor;

    private void RiseIn(Panel body)
    {
        string? shownFor = _selected;
        if (_filledOnce && shownFor != _enteredFor && Motion.Flourish) Motion.FadeIn(body);
        _filledOnce = true;
        _enteredFor = shownFor;
    }

    private void ShowPreviewHidden(HudWidgetConfig w)
    {
        if (_previewHidden is { } hidden) hidden.Visibility = w.Enabled ? Visibility.Collapsed : Visibility.Visible;
    }
}
