using System.Drawing;
using PitBox.Config;

namespace PitBox.SimRacing.Hud;

internal sealed class HudToolbar
{
    private const string TitleText = "Arranging the iRacing Overlay";
    private static readonly (string Id, string Label)[] Buttons = { ("undo", "Undo"), ("redo", "Redo"), ("snap", "Snap"), ("gridDown", ""), ("grid", ""), ("gridUp", ""), ("cancel", "Cancel"), ("done", "Done") };
    private const float Height = 40, Pad = 6, Gap = 6, Step = 28;

    private readonly HudWindow _window = new("Pit-Box · iRacing Overlay editing") { ClickThrough = false };
    private readonly HudPainter _painter = new();
    private readonly List<(string Id, RectangleF Box)> _boxes = new();
    private float _scale = 1;

    public HudToolbar() => _window.Clicked += (_, at) => Click(new PointF(at.X / _scale, at.Y / _scale));

    public event Action<string>? Command;

    private bool _closed;

    public void Draw(HudHost host)
    {
        if (_closed) return;
        var monitor = HudGeometry.MonitorFor("primary", host.Monitors);
        _scale = monitor.Scale;
        _boxes.Clear();
        float title = _painter.Measure(TitleText, _painter.SmallBold) + 18;
        float x = Pad + title;
        string gridLabel = $"Grid {host.Settings.GridSize} px";
        foreach (var (id, label) in Buttons)
        {
            float w = id switch
            {
                "gridDown" or "gridUp" => Step,
                "grid" => _painter.Measure(gridLabel, _painter.Bold) + 16,
                _ => Math.Max(64, _painter.Measure(label, _painter.Bold) + 26),
            };
            _boxes.Add((id, new RectangleF(x, Pad, w, Height - Pad * 2)));
            x += w + (id is "gridDown" or "grid" ? 0 : Gap);
        }
        float width = x - Gap + Pad;
        var rect = new RectangleF(monitor.Bounds.Left + (monitor.Bounds.Width - width * _scale) / 2, monitor.Bounds.Top + 24 * _scale, width * _scale, Height * _scale);
        _window.Rect = rect;
        var size = new Size((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height));
        var g = _window.Surface(size);
        var look = host.Settings.Appearance;
        var p = _painter;
        p.Begin(g, _scale, new SizeF(width, Height), HudPalette.Of(look), look);
        p.Fill(p.Bounds, HudPalette.Alpha(p.P.Surface, 245), 8);
        p.Outline(new RectangleF(0.5f, 0.5f, width - 1, Height - 1), p.P.Accent, 1.5f, 8);
        p.Text(TitleText, p.SmallBold, p.P.AccentText, new RectangleF(Pad + 6, 0, title - 6, Height), HAlign.Left, VAlign.Center, false);
        foreach (var (id, box) in _boxes)
        {
            bool enabled = id switch
            {
                "undo" => host.CanUndo,
                "redo" => host.CanRedo,
                "gridDown" => host.Settings.GridSize > RaceHudSettings.MinGrid,
                "gridUp" => host.Settings.GridSize < RaceHudSettings.MaxGrid,
                _ => true,
            };
            if (id is "grid")
            {
                p.Line(box.X, box.Y, box.Right, box.Y, p.P.LineStrong);
                p.Line(box.X, box.Bottom, box.Right, box.Bottom, p.P.LineStrong);
                p.Text(gridLabel, p.Bold, p.P.Text, box, HAlign.Center, VAlign.Center, false);
                continue;
            }
            if (id is "gridDown" or "gridUp")
            {
                p.Fill(box, p.P.SurfaceAlt, 5);
                p.Outline(box, p.P.LineStrong, 1, 5);
                float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2, arm = 4, dir = id == "gridUp" ? 1 : -1;
                var stroke = enabled ? p.P.Text : p.P.Faint;
                p.Line(cx - arm / 2 * dir, cy - arm, cx + arm / 2 * dir, cy, stroke, 1.6f);
                p.Line(cx + arm / 2 * dir, cy, cx - arm / 2 * dir, cy + arm, stroke, 1.6f);
                continue;
            }
            bool primary = id == "done";
            bool on = id == "snap" && host.Settings.Snap;
            var fill = primary ? p.P.Accent : on ? p.P.AccentSoft : p.P.SurfaceAlt;
            p.Fill(box, fill, 5);
            p.Outline(box, primary ? p.P.Accent : p.P.LineStrong, 1, 5);
            string label = id == "snap" ? (host.Settings.Snap ? "Snap on" : "Snap off") : Buttons.First(b => b.Id == id).Label;
            var color = primary ? p.P.OnAccent : enabled ? p.P.Text : p.P.Faint;
            p.Text(label, p.Bold, color, box, HAlign.Center, VAlign.Center);
        }
        p.End(_window.SurfaceDc, size);
        if (!_window.Visible) _window.Show();
        _window.Present(new Point((int)rect.X, (int)rect.Y));
        _window.KeepOnTop();
    }

    private void Click(PointF at)
    {
        foreach (var (id, box) in _boxes)
            if (box.Contains(at)) { Command?.Invoke(id); return; }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _window.Hide();
        _window.Dispose();
        _painter.Dispose();
    }
}
