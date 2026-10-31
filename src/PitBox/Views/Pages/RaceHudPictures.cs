using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PitBox.Config;
using PitBox.SimRacing.Hud;
using PitBox.Views.HudSettings;

namespace PitBox.Views.Pages;

internal sealed partial class RaceHudWidgets
{
    private const double SideBySideMin = 860, SettingsShare = 1.05, GroupMinWidth = 440;
    private const double PictureMaxW = 80, PictureMaxH = 34, PicturePadX = 3, PicturePadY = 5, PictureMinScale = 0.45;
    private const int PictureCache = 64;

    private readonly Dictionary<string, UIElement> _presetDots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SettingGroup, FrameworkElement> _groupCards = new();
    private readonly HudPainter _picturePainter = new() { Items = new(), Regions = new() };
    private static readonly Dictionary<string, IReadOnlyList<ImageSource>?> PictureMemo = new();
    private System.Drawing.Bitmap? _pictureBitmap;

    private void ShowPresetState(HudWidgetConfig w, HudWidgetDef def)
    {
        var editor = def.EditorDef;
        var nearest = editor.Nearest(def, w);
        _presets?.Show(editor.PresetName(def, w), nearest);
        var changed = (nearest?.Changed ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, dot) in _presetDots) dot.Visibility = changed.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
    }

    private IReadOnlyList<ImageSource>? Pictures(HudWidgetConfig w, HudWidgetDef def, string settingId, IReadOnlyList<IReadOnlyDictionary<string, string>> choices)
    {
        if (Host is not { } host || choices.Count == 0) return null;
        string signature = $"{w.Kind}|{settingId}|{w.W:0}x{w.H:0}|{_previewPixels:0.00}|"
            + string.Join("/", choices.Select(ch => string.Join(",", ch.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)))) + "|"
            + string.Join(";", w.Options.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)) + "|"
            + System.Text.Json.JsonSerializer.Serialize(host.Settings.Appearance, ConfigStore.JsonOptions);
        if (PictureMemo.TryGetValue(signature, out var cached)) return cached;
        if (PictureMemo.Count >= PictureCache) PictureMemo.Clear();
        var names = def.EditorDef.RegionsOf(settingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var race = _section.Preview.Race;
        var copy = w.Copy();
        copy.Id = w.Id + "-picture";
        void Pick(IReadOnlyDictionary<string, string> values)
        {
            foreach (var (key, value) in values)
            {
                if (def.Option(key) is not { } option) continue;
                if (value == option.Default) copy.Options.Remove(key); else copy.Options[key] = value;
            }
        }
        System.Drawing.RectangleF? area = null;
        try
        {
            foreach (var choice in choices)
            {
                Pick(choice);
                host.MeasurePreview(copy, race, _picturePainter);
                var boxes = (_picturePainter.Regions ?? new()).Where(r => names.Contains(r.Id)).Select(r => r.Box)
                    .Concat((_picturePainter.Items ?? new()).Where(i => names.Contains(i.Key)).Select(i => i.Box));
                foreach (var box in boxes) area = area is { } a ? System.Drawing.RectangleF.Union(a, box) : box;
            }
            if (area is not { Width: >= 4, Height: >= 4 } found)
            {
                PictureMemo[signature] = null;
                return null;
            }
            found.Inflate((float)PicturePadX, (float)PicturePadY);
            found.Intersect(new System.Drawing.RectangleF(0, 0, (float)w.W, (float)w.H));
            double fit = Math.Clamp(PictureMaxH / found.Height, PictureMinScale, 1);
            found.Width = (float)Math.Min(found.Width, PictureMaxW / fit);
            found.Height = (float)Math.Min(found.Height, PictureMaxH / fit);
            double k = _previewPixels;
            float scale = (float)(fit * k);
            var images = new List<ImageSource>();
            foreach (var choice in choices)
            {
                Pick(choice);
                host.RenderPreview(copy, race, _picturePainter, scale, ref _pictureBitmap);
                var bitmap = _pictureBitmap!;
                var crop = System.Drawing.Rectangle.Intersect(System.Drawing.Rectangle.Round(new System.Drawing.RectangleF(found.X * scale, found.Y * scale, found.Width * scale, found.Height * scale)),
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (crop.Width < 1 || crop.Height < 1)
                {
                    PictureMemo[signature] = null;
                    return null;
                }
                images.Add(Crop(bitmap, crop, k));
            }
            PictureMemo[signature] = images;
            return images;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            PictureMemo[signature] = null;
            return null;
        }
    }

    private static BitmapSource Crop(System.Drawing.Bitmap bitmap, System.Drawing.Rectangle crop, double k)
    {
        var target = new WriteableBitmap(crop.Width, crop.Height, 96 * k, 96 * k, PixelFormats.Pbgra32, null);
        var data = bitmap.LockBits(crop, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try { target.WritePixels(new Int32Rect(0, 0, crop.Width, crop.Height), data.Scan0, data.Stride * crop.Height, data.Stride); }
        finally { bitmap.UnlockBits(data); }
        target.Freeze();
        return target;
    }
}
