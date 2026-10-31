using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PitBox.Services;
using PitBox.Theme;

namespace PitBox.Controls;

public sealed class HotkeyBox : TextBox
{
    private const string EmptyHint = "Click to set…";
    private bool _paused, _recording;
    private Window? _window;
    private readonly Popup _hint;
    private readonly TextBlock _hintText = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 227 };
    private KeyCaps? _caps;

    public string? Hotkey { get; private set; }

    public bool NumpadByPosition { get; init; }

    private string _placeholder = EmptyHint;

    public string Placeholder
    {
        get => _placeholder;
        init { _placeholder = value; Tag = value; }
    }

    public bool ForSearch { get; init; }

    private bool _lastExtended;

    public event Action<string?>? HotkeyChanged;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        Tag = EmptyHint;
        Width = 154;
        InputMethod.SetIsInputMethodEnabled(this, false);
        SetResourceReference(StyleProperty, typeof(TextBox));
        _hintText.SetResourceReference(TextBlock.ForegroundProperty, "TooltipText");
        var border = new Border { Padding = new Thickness(8, 5, 8, 6), CornerRadius = Theme.ThemeManager.Round(6), Child = _hintText };
        border.SetResourceReference(Border.BackgroundProperty, "TooltipBg");
        border.SetResourceReference(Border.BorderBrushProperty, "TooltipEdge");
        Hairline.Frame(border);
        _hint = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, VerticalOffset = 3, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            StaysOpen = true, Focusable = false, IsHitTestVisible = false, Child = border,
        };
        RenderOptions.SetClearTypeHint(border, ClearTypeHint.Enabled);
        Unloaded += (_, _) => StopRecording();
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopRecording(); };
    }

    public void SetHotkey(string? hotkey)
    {
        Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey;
        if (!_recording) Text = Theme.ThemeManager.Case(PitBox.Hotkeys.Hotkey.Display(Hotkey));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (_recording) return;
        _recording = true;
        if (!_paused)
        {
            _paused = true;
            AppServices.Current?.Hotkeys.Pause();
        }
        Text = "";
        Tag = "Press the keys…";
        _hintText.Text = ForSearch
            ? "Press a key combination to see what it does.\nEsc clears · click anywhere else to cancel."
            : Hotkey is null
            ? "Press the key combination to use.\nClick anywhere else to cancel."
            : $"Press the new keys to replace {PitBox.Hotkeys.Hotkey.Display(Hotkey)}.\nEsc deletes this keybind · click anywhere else to cancel.";
        _hint.IsOpen = true;
        Listen(true);
        if (NumpadByPosition) ComponentDispatcher.ThreadPreprocessMessage += OnRawKey;
        else PitBox.Hotkeys.ControllerButtons.Shared.Record(OnButton);
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewMouseDown += OnWindowMouseDown;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseOver) Cancel();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Cancel();

    private void Cancel()
    {
        StopRecording();
        if (IsKeyboardFocused) Keyboard.ClearFocus();
        if (FocusManager.GetFocusScope(this) is { } scope && FocusManager.GetFocusedElement(scope) == this)
            FocusManager.SetFocusedElement(scope, null);
    }

    private void StopRecording()
    {
        if (_window is not null)
        {
            _window.PreviewMouseDown -= OnWindowMouseDown;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
        _hint.IsOpen = false;
        Listen(false);
        if (_recording && !NumpadByPosition) PitBox.Hotkeys.ControllerButtons.Shared.StopRecording();
        ComponentDispatcher.ThreadPreprocessMessage -= OnRawKey;
        if (_paused)
        {
            _paused = false;
            AppServices.Current?.Hotkeys.Resume();
        }
        _recording = false;
        Tag = _placeholder;
        Text = Theme.ThemeManager.Case(PitBox.Hotkeys.Hotkey.Display(Hotkey));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (Hotkey is not null) Update(null);
            Cancel();
            return;
        }
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = false;
            return;
        }

        if (NumpadByPosition && !_lastExtended && NumpadName(key) is { } pad)
        {
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Windows)) return;
            Update((mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + pad);
            Cancel();
            return;
        }
        if (PitBox.Hotkeys.Hotkey.FromWpf(key, Keyboard.Modifiers) is { } hotkey)
        {
            Update(hotkey.ToString());
            Cancel();
            return;
        }
        ShowHeld();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (_recording) ShowHeld();
    }

    private void ShowHeld()
    {
        if (_caps is null) return;
        var mods = Keyboard.Modifiers;
        var held = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) held.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) held.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) held.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) held.Add("Win");
        _caps.Show(held);
        Tag = held.Count > 0 ? "" : "Press the keys…";
    }

    private void Listen(bool on)
    {
        if (!on)
        {
            ClearValue(BorderBrushProperty);
            if (_caps is not null)
            {
                AdornerLayer.GetAdornerLayer(this)?.Remove(_caps);
                _caps = null;
            }
            return;
        }
        if (AdornerLayer.GetAdornerLayer(this) is { } layer)
        {
            _caps = new KeyCaps(this);
            layer.Add(_caps);
        }
        if (!Views.Motion.Flourish || TryFindResource("Accent") is not SolidColorBrush accent) return;
        var pulse = new SolidColorBrush(accent.Color);
        BorderBrush = pulse;
        pulse.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(550)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Views.Motion.Ease });
    }

    private void Flash()
    {
        if (!Views.Motion.Flourish || TryFindResource("AccentSoft") is not SolidColorBrush soft) return;
        var flash = new SolidColorBrush(soft.Color);
        Background = flash;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500));
        fade.Completed += (_, _) => { if (ReferenceEquals(Background, flash)) ClearValue(BackgroundProperty); };
        flash.BeginAnimation(Brush.OpacityProperty, fade);
    }

    private sealed class KeyCaps : Adorner
    {
        private IReadOnlyList<string> _keys = Array.Empty<string>();

        public KeyCaps(UIElement box) : base(box) => IsHitTestVisible = false;

        public void Show(IReadOnlyList<string> keys)
        {
            bool added = keys.Count > _keys.Count;
            _keys = keys;
            InvalidateVisual();
            if (!added || !Views.Motion.Flourish) return;
            var pop = new ScaleTransform();
            RenderTransform = pop;
            RenderTransformOrigin = new Point(0, 0.5);
            var grow = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = Views.Motion.Ease, FillBehavior = FillBehavior.Stop };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_keys.Count == 0 || AdornedElement is not Control box) return;
            var text = box.TryFindResource("Text") as Brush ?? box.Foreground;
            var fill = box.TryFindResource("SurfaceAlt") as Brush;
            var edge = Hairline.Pen(box.TryFindResource("LineStrong") as Brush ?? text, this);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip, x = 8, height = 18, y = Math.Round((box.ActualHeight - height) / 2);
            var typeface = new Typeface(box.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            for (int i = 0; i < _keys.Count; i++)
            {
                if (i > 0)
                {
                    var plus = new FormattedText(ThemeManager.Case("+"), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, text, null, TextFormattingMode.Display, dip);
                    dc.DrawText(plus, new Point(x + 3, y + (height - plus.Height) / 2));
                    x += plus.Width + 6;
                }
                var label = new FormattedText(ThemeManager.Case(_keys[i]), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, text, null, TextFormattingMode.Display, dip);
                double width = Math.Round(label.Width + 12);
                dc.DrawRectangle(fill, edge, new Rect(x + 0.5, y + 0.5, width, height));
                dc.DrawText(label, new Point(x + 6.5, y + 0.5 + (height - label.Height) / 2));
                x += width + 3;
            }
        }
    }

    private void OnButton(PitBox.Hotkeys.ControllerButton button)
    {
        if (!_recording) return;
        Update(button.ToString());
        Cancel();
    }

    private void OnRawKey(ref System.Windows.Interop.MSG msg, ref bool handled)
    {
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (msg.message is WM_KEYDOWN or WM_SYSKEYDOWN)
            _lastExtended = ((long)msg.lParam & (1L << 24)) != 0;
    }

    internal static string? NumpadName(Key key) => key switch
    {
        Key.Insert => "Num 0", Key.End => "Num 1", Key.Down => "Num 2", Key.PageDown => "Num 3", Key.Left => "Num 4",
        Key.Clear => "Num 5", Key.Right => "Num 6", Key.Home => "Num 7", Key.Up => "Num 8", Key.PageUp => "Num 9",
        Key.Delete => "Num .",
        _ => null,
    };

    private void Update(string? hotkey)
    {
        Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey;
        HotkeyChanged?.Invoke(Hotkey);
        if (Hotkey is not null) Flash();
    }
}
