using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PitBox.Views;

internal sealed class Odometer : StackPanel, IFrameStep
{
    private const double Overhang = 6;
    private const string Strip = "0\n1\n2\n3\n4\n5\n6\n7\n8\n9";
    private static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    private sealed class Slot
    {
        public TextBlock? Plain;
        public TextBlock? Sizer;
        public TranslateTransform? Move;
        public Canvas? Strip;
        public double From, To, Shown;
        public int FromRight;
    }

    private readonly List<Slot> _slots = new();
    private string _text = "";
    private string _shape = "";
    private double _start, _stagger;
    private bool _counted;

    public double RollMs { get; init; } = 650;

    public bool CountUpOnLoad { get; init; }

    public string Text => _text;

    public Odometer()
    {
        Orientation = Orientation.Horizontal;
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Loaded += (_, _) => OnFirstShow();
        Unloaded += (_, _) => Snap();
    }

    public Odometer(double size, string brush, bool italic = false, bool heavy = true) : this()
    {
        TextElement.SetFontSize(this, size);
        TextElement.SetFontStyle(this, italic ? FontStyles.Italic : FontStyles.Normal);
        TextElement.SetFontWeight(this, heavy ? FontWeights.ExtraBold : FontWeights.Bold);
        SetResourceReference(TextElement.FontFamilyProperty, "RaceFont");
        SetResourceReference(TextElement.ForegroundProperty, brush);
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void SetBrush(string brush) => SetResourceReference(TextElement.ForegroundProperty, brush);

    public void Set(string text, bool fromZero = false)
    {
        if (text == _text && !fromZero) return;
        _text = text;
        if (!SameShape(text)) Rebuild(text, fromZero);
        int fromRight = 0;
        for (int i = _slots.Count - 1; i >= 0; i--, fromRight++)
        {
            var slot = _slots[i];
            slot.FromRight = fromRight;
            char ch = text[i];
            if (slot.Sizer is null)
            {
                string plain = ch < 128 ? Ascii[ch] : ch.ToString();
                if (slot.Plain!.Text != plain) slot.Plain.Text = plain;
                continue;
            }
            int d = ch - '0';
            slot.Sizer.Text = Digits[d];
            slot.From = fromZero ? 0 : slot.Shown;
            slot.To = d;
            if (slot.From != slot.To) Settle(slot, false);
        }
        _stagger = fromZero ? 45 : 18;
        if (!Motion.Flourish || !IsLoaded)
        {
            Snap();
            return;
        }
        _start = FrameDriver.Now;
        FrameDriver.Run(this);
    }

    private static readonly string[] Ascii = Enumerable.Range(0, 128).Select(c => ((char)c).ToString()).ToArray();

    private bool SameShape(string text)
    {
        if (text.Length != _shape.Length) return false;
        for (int i = 0; i < text.Length; i++)
            if (char.IsAsciiDigit(text[i]) != (_shape[i] == '0')) return false;
        return true;
    }

    private void Rebuild(string text, bool fromZero)
    {
        var previous = _slots.Where(s => s.Sizer is not null).Select(s => s.Shown).Reverse().ToList();
        Children.Clear();
        _slots.Clear();
        _shape = new string(text.Select(c => char.IsAsciiDigit(c) ? '0' : 'x').ToArray());
        int digitsFromRight = text.Count(char.IsAsciiDigit);
        foreach (char ch in text)
        {
            if (!char.IsAsciiDigit(ch))
            {
                var plain = new TextBlock { Text = ch.ToString() };
                Children.Add(plain);
                _slots.Add(new Slot { Plain = plain });
                continue;
            }
            digitsFromRight--;
            double start = fromZero || digitsFromRight >= previous.Count ? 0 : previous[digitsFromRight];
            var sizer = new TextBlock { Text = Digits[ch - '0'], Visibility = Visibility.Hidden };
            var move = new TranslateTransform();
            var strip = new TextBlock { Text = Strip, RenderTransform = move };
            var canvas = new Canvas();
            canvas.Children.Add(strip);
            var cell = new Grid();
            cell.Children.Add(sizer);
            cell.Children.Add(canvas);
            var slot = new Slot { Sizer = sizer, Move = move, Strip = canvas, Shown = start, From = start, To = start };
            sizer.SizeChanged += (_, e) => move.Y = -slot.Shown * e.NewSize.Height;
            var clip = new Border { ClipToBounds = true, Child = cell, Padding = new Thickness(Overhang, 0, Overhang, 0), Margin = new Thickness(-Overhang, 0, -Overhang, 0) };
            Children.Add(clip);
            _slots.Add(slot);
        }
    }

    public bool Step(double now)
    {
        bool running = false;
        foreach (var slot in _slots)
        {
            if (slot.Sizer is null) continue;
            double t = (now - _start - slot.FromRight * _stagger) / RollMs;
            if (t < 1) running = true;
            slot.Shown = slot.From + (slot.To - slot.From) * FrameDriver.EaseOut(t);
            slot.Move!.Y = -slot.Shown * slot.Sizer.ActualHeight;
            Settle(slot, t >= 1);
        }
        return running;
    }

    private void Snap()
    {
        FrameDriver.Stop(this);
        foreach (var slot in _slots)
        {
            if (slot.Sizer is null) continue;
            slot.Shown = slot.From = slot.To;
            slot.Move!.Y = -slot.Shown * slot.Sizer.ActualHeight;
            Settle(slot, true);
        }
    }

    private static void Settle(Slot slot, bool still)
    {
        var sizer = still ? Visibility.Visible : Visibility.Hidden;
        if (slot.Sizer!.Visibility != sizer) slot.Sizer.Visibility = sizer;
        var strip = still ? Visibility.Hidden : Visibility.Visible;
        if (slot.Strip!.Visibility != strip) slot.Strip.Visibility = strip;
    }

    private void OnFirstShow()
    {
        if (!CountUpOnLoad || _counted) return;
        _counted = true;
        if (!Motion.Flourish || _text.Length == 0) return;
        foreach (var slot in _slots)
            if (slot.Move is not null && slot.Sizer is not null) { slot.Shown = 0; slot.Move.Y = 0; Settle(slot, false); }
        Motion.AfterPageSettles(Dispatcher, () => Set(_text, fromZero: true));
    }
}
