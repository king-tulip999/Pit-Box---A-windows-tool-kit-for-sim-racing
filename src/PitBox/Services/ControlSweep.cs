using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PitBox.Views;

namespace PitBox.Services;

internal static class ControlSweep
{
    private const int RepeatCap = 4;
    private const int MaxDepth = 3;

    private static readonly string[] StyleKeys =
    {
        "PrimaryButton", "SoftButton", "SelectedButton", "GhostButton", "DangerButton", "DangerConfirmButton", "IconButton",
        "QuietButton", "QuietIconButton", "CaptionButton", "CloseCaptionButton", "ThemeLogoButton", "Btn", "NavItem", "Switch", "Segment", "PageTab",
    };

    private static readonly string[] ButtonRoles = { "PrimaryButton", "SoftButton", "SelectedButton", "GhostButton", "DangerButton", "DangerConfirmButton", "IconButton", "Btn", "implicit" };

    private static readonly HashSet<string> CaptionNames = new(StringComparer.Ordinal) { "MinButton", "MaxButton", "CloseButton", "SetupMinButton", "SetupCloseButton" };

    private static readonly string[] Destructive = { "delete", "remove", "reset", "clear", "forget", "restore", "revert", "discard", "quit", "uninstall" };

    private sealed record Surface(string Page, string? State, IReadOnlyList<string> Path, string Layer, string Name, string Base);

    private sealed class Found
    {
        public required FrameworkElement Element;
        public Hyperlink? Link;
        public required string Kind, Type, Style, Label, Tip, Context, Key, Group;
        public Rect Bounds;
        public bool Enabled;
        public string View = "in view";
    }

    private sealed class Row
    {
        public required string Page, Surface, Kind, Type, Style, Label, Tip, Context, Key;
        public Rect Bounds;
        public bool Enabled;
        public string View = "";
        public string Outcome = "";
        public readonly List<string> Issues = new();
    }

    private static readonly List<string> Errors = new();
    private static readonly List<Row> Rows = new();
    private static readonly Dictionary<string, Row> Unique = new(StringComparer.Ordinal);
    private static readonly HashSet<string> SweptLayers = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PicturedLayers = new(StringComparer.Ordinal);
    private static readonly List<Surface> Deferred = new();
    private static bool _late;
    private static readonly HashSet<string> TipsSeen = new(StringComparer.Ordinal);
    private static readonly List<string> TipFindings = new();
    private static readonly List<string> Blocked = new();
    private static Dictionary<Style, string>? _styleNames;
    private static StreamWriter? _log;
    private static string _folder = "";
    private static int _shot;

    public static bool Active { get; private set; }

    public static void RecordError(Exception ex)
    {
        lock (Errors) Errors.Add(ex.ToString());
        Log("  ERROR " + ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    "));
    }

    private static int ErrorCount { get { lock (Errors) return Errors.Count; } }

    private static void Log(string text)
    {
        try { _log?.WriteLine(text); } catch (IOException) { }
    }

    public static async void Run(MainWindow window, string folder)
    {
        Active = true;
        _folder = folder;
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "layers"));
        Directory.CreateDirectory(Path.Combine(folder, "tips"));
        _log = new StreamWriter(Path.Combine(folder, "steps.txt")) { AutoFlush = true };
        External.Gate = what => { lock (Blocked) Blocked.Add(what); Log("  blocked: " + what); return false; };
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            (window.Left, window.Top, window.Width, window.Height) = (0, 0, 1280 + MainWindow.SidebarDips, 820);
            window.Show();
            await Task.Delay(1500);
            var only = Arg("--pages")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (window.SetupOpen)
                await SweepAsync(window, new Surface("setup", null, Array.Empty<string>(), "setup", "setup", "setup"), 0);
            if (window.SetupOpen) window.CloseSetup();
            await Settle(400);
            if (only is null)
                await SweepAsync(window, new Surface(window.FirstPage, null, Array.Empty<string>(), "shell", "shell", "shell"), 0);
            foreach (var page in window.NavKeys.Where(k => only is null || only.Contains(k, StringComparer.OrdinalIgnoreCase)))
            {
                await SweepAsync(window, new Surface(page, null, Array.Empty<string>(), "page", page, "page"), 0);
                if (window.PageFor(page) is IScreenshotStates states)
                    foreach (var name in states.ScreenshotStates().Select(s => s.Name).ToList())
                        await SweepAsync(window, new Surface(page, name, Array.Empty<string>(), "page", $"{page}/{name}", "page"), 0);
                _late = true;
                foreach (var deferred in Deferred.ToList()) await SweepAsync(window, deferred, MaxDepth);
                Deferred.Clear();
                _late = false;
            }
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
        finally
        {
            try { Write(); } catch (Exception ex) { RecordError(ex); }
            lock (Errors)
                File.WriteAllText(Path.Combine(folder, "errors.txt"), Errors.Count == 0 ? "none\n" : string.Join("\n\n", Errors));
            _log?.Dispose();
            External.Gate = null;
            App.Quit();
        }
    }

    private static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static async Task Settle(int ms = 120)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (ms > 0) await Task.Delay(ms);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task<bool> ReachAsync(MainWindow window, Surface s)
    {
        await CloseLayersAsync(window, s.Page == "setup");
        if (s.Page == "setup")
        {
            if (!window.SetupOpen) window.ShowSetup(true);
        }
        else
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            if (window.CurrentPage != s.Page) window.Navigate(s.Page);
        }
        await Settle(250);
        if (s.State is not null && window.PageFor(s.Page) is IScreenshotStates states)
        {
            var enter = states.ScreenshotStates().FirstOrDefault(x => x.Name == s.State).Enter;
            if (enter is null) return false;
            try { await enter(); }
            catch (Exception ex) { RecordError(ex); return false; }
            await Settle(400);
        }
        for (int i = 0; i < s.Path.Count; i++)
        {
            var layer = new Surface(s.Page, s.State, s.Path.Take(i).ToList(), i == 0 ? s.Base : "layer", "", s.Base);
            var target = Collect(window, layer).FirstOrDefault(f => f.Key == s.Path[i]);
            if (target is null) return false;
            await OpenAsync(target);
            await Settle(250);
        }
        return true;
    }

    private static async Task OpenAsync(Found f)
    {
        switch (f.Kind)
        {
            case "context menu":
                f.Element.ContextMenu!.PlacementTarget = f.Element;
                f.Element.ContextMenu.IsOpen = true;
                break;
            case "dropdown" when f.Element is ComboBox combo:
                combo.IsDropDownOpen = true;
                break;
            default:
                Press(f);
                break;
        }
        await Task.CompletedTask;
    }

    private static async Task CloseLayersAsync(MainWindow window, bool keepSetup)
    {
        for (int round = 0; round < 4; round++)
        {
            bool any = false;
            foreach (var popup in OpenPopups())
            {
                any = true;
                if (popup.Child is ContextMenu menu) menu.IsOpen = false;
                else if (popup.TemplatedParent is ComboBox combo) combo.IsDropDownOpen = false;
                else popup.IsOpen = false;
            }
            if (window.ModalLayer.Visibility == Visibility.Visible && window.ModalContent.Content is not null)
            {
                any = true;
                window.CloseModal(false);
            }
            if (!keepSetup && window.SetupOpen)
            {
                any = true;
                window.CloseSetup();
            }
            if (window.Toasts.Children.Count > 0)
            {
                any = true;
                window.Toasts.Children.Clear();
            }
            if (window.CurrentPage is { } page && window.PageFor(page) is ICancelEdit edit && edit.CancelEdit()) any = true;
            if (!any) break;
            await Settle(200);
        }
    }

    private static List<Popup> OpenPopups()
    {
        var found = new List<Popup>();
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is FrameworkElement { IsVisible: true } root && root.GetType().Name == "PopupRoot" && root.Parent is Popup { IsOpen: true } popup
                && popup.Child is not ToolTip)
                found.Add(popup);
        return found;
    }

    private static IEnumerable<(Visual Root, string Where)> Roots(MainWindow window, Surface s)
    {
        switch (s.Layer)
        {
            case "setup":
                yield return (window.SetupContent, "setup");
                break;
            case "shell":
                yield return (window.SidebarHost, "sidebar");
                yield return (window.TitleBar, "title bar");
                break;
            case "page":
                yield return (window.PageContent, "page");
                break;
            default:
                if (window.ModalLayer.Visibility == Visibility.Visible && window.ModalContent.Content is Visual modal)
                    yield return (modal, "dialog");
                foreach (var popup in OpenPopups())
                    if (PresentationSource.FromVisual(popup.Child) is { RootVisual: Visual root })
                        yield return (root, popup.Child is ContextMenu ? "menu" : "popup");
                if (window.Toasts.Children.Count > 0)
                    yield return (window.Toasts, "notice");
                break;
        }
    }

    private static Dictionary<Style, string> StyleNames()
    {
        if (_styleNames is not null) return _styleNames;
        _styleNames = new Dictionary<Style, string>();
        foreach (var key in StyleKeys)
            if (Application.Current.TryFindResource(key) is Style style && !_styleNames.ContainsKey(style))
                _styleNames[style] = key;
        return _styleNames;
    }

    private static string StyleOf(FrameworkElement e)
    {
        if (e.Style is null) return "implicit";
        if (StyleNames().TryGetValue(e.Style, out var name)) return name;
        for (var based = e.Style.BasedOn; based is not null; based = based.BasedOn)
            if (StyleNames().TryGetValue(based, out var parent)) return parent + "+";
        return "custom";
    }

    private static string Plain(string text) => Theme.TextCase.Plain(text).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();

    private static string OwnText(DependencyObject node)
    {
        var parts = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is UIElement { IsVisible: false }) return;
            if (d is TextBlock { Text.Length: > 0 } tb) parts.Add(Plain(tb.Text));
            int count = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < count && parts.Count < 3; i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(node);
        string text = string.Join(" ", parts.Where(p => p.Length > 0));
        return text.Length > 70 ? text[..70] : text;
    }

    private static string TipOf(FrameworkElement e) => e.ToolTip switch
    {
        string s => Plain(s),
        ToolTip { Content: string s } => Plain(s),
        ToolTip { Content: FrameworkElement fe } => "[" + OwnText(fe) + "]",
        FrameworkElement fe => "[" + OwnText(fe) + "]",
        null => "",
        var other => Plain(other.ToString() ?? ""),
    };

    private static string ContextOf(FrameworkElement e)
    {
        DependencyObject? node = e;
        for (int level = 0; level < 4 && node is not null; level++)
        {
            var parent = VisualTreeHelper.GetParent(node);
            if (parent is null) break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            int at = 0;
            while (at < count && !ReferenceEquals(VisualTreeHelper.GetChild(parent, at), node)) at++;
            for (int i = at - 1; i >= 0 && i >= at - 3; i--)
            {
                var sibling = VisualTreeHelper.GetChild(parent, i);
                if (sibling is UIElement { IsVisible: true } && FirstTextOutsideControls(sibling, 0) is { Length: > 0 } t) return t.Length > 50 ? t[..50] : t;
            }
            node = parent;
        }
        return "";
    }

    private static string? FirstTextOutsideControls(DependencyObject d, int depth)
    {
        if (depth > 6 || d is UIElement { IsVisible: false } || d is ButtonBase or ComboBox or TextBoxBase or Slider) return null;
        if (d is TextBlock { Text.Length: > 0 } tb) return Plain(tb.Text);
        int count = Math.Min(VisualTreeHelper.GetChildrenCount(d), 8);
        for (int i = 0; i < count; i++)
            if (FirstTextOutsideControls(VisualTreeHelper.GetChild(d, i), depth + 1) is { } t) return t;
        return null;
    }

    private static bool InsideControl(DependencyObject e)
    {
        for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ButtonBase or ComboBox or TextBoxBase or Slider or ScrollBar or MenuItem) return true;
        return false;
    }

    private static readonly Dictionary<Type, bool> PointerTypes = new();

    private static bool IsPointerSurface(FrameworkElement e)
    {
        var type = e.GetType();
        if (type.Assembly != typeof(ControlSweep).Assembly) return false;
        if (PointerTypes.TryGetValue(type, out bool known)) return known;
        bool yes = false;
        for (var t = type; t is not null && t.Assembly == typeof(ControlSweep).Assembly && !yes; t = t.BaseType)
            foreach (var name in new[] { "OnMouseLeftButtonDown", "OnMouseDown", "OnPreviewMouseLeftButtonDown", "OnMouseMove", "OnMouseWheel" })
                if (t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not null) { yes = true; break; }
        PointerTypes[type] = yes;
        return yes;
    }

    private static string? KindOf(FrameworkElement e)
    {
        if (e is UIElement { IsVisible: false } || !e.IsHitTestVisible || e.ActualWidth <= 0 || e.ActualHeight <= 0) return null;
        if (e.TemplatedParent is ComboBox or ScrollBar or Slider or ScrollViewer || e.TemplatedParent is TextBoxBase) return null;
        if (e is ScrollBar or Thumb || e is RepeatButton && e.TemplatedParent is not null) return null;
        switch (e)
        {
            case ComboBox: return "dropdown";
            case Slider: return "slider";
            case PitBox.Controls.HotkeyBox: return "keybind box";
            case TextBox: return "text input";
            case PasswordBox: return "text input";
            case MenuItem mi: return mi.HasItems ? "submenu" : "menu item";
            case RadioButton rb when StyleOf(rb) == "PageTab": return "tab";
            case RadioButton rb when StyleOf(rb) == "Segment": return "segment";
            case RadioButton: return "radio";
            case CheckBox cb when StyleOf(cb) == "Switch": return "switch";
            case CheckBox: return "checkbox";
            case ToggleButton: return "toggle";
            case ButtonBase b: return OwnText(b).Length == 0 ? "icon button" : "button";
        }
        if (InsideControl(e)) return null;
        if (IsPointerSurface(e)) return "pointer surface";
        var cursor = DependencyPropertyHelper.GetValueSource(e, FrameworkElement.CursorProperty).BaseValueSource;
        if (cursor != BaseValueSource.Default && e.Cursor == Cursors.Hand && e is not Window) return "clickable";
        return null;
    }

    private static List<Found> Collect(MainWindow window, Surface s)
    {
        var found = new List<Found>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (root, where) in Roots(window, s))
        {
            void Add(FrameworkElement e, Hyperlink? link, string kind)
            {
                string type = link is not null ? "Hyperlink" : e.GetType().Name;
                string style = link is not null ? "link" : StyleOf(e);
                string label = link is not null ? Plain(new TextRange(link.ContentStart, link.ContentEnd).Text) : e switch
                {
                    ComboBox combo => "items: " + string.Join(" / ", combo.Items.Cast<object>().Take(3).Select(ItemText)),
                    Slider slider => $"{slider.Minimum:0.##}–{slider.Maximum:0.##}",
                    TextBox box => box.Tag is string tag ? "placeholder: " + Plain(tag) : "",
                    _ => OwnText(e),
                };
                if (e.Name is { Length: > 0 } n && label.Length == 0) label = "#" + n;
                string tip = link is not null ? "" : TipOf(e);
                string automation = AutomationProperties.GetName(e);
                if (tip.Length == 0 && automation.Length > 0) tip = "(name) " + automation;
                string context = ContextOf(e);
                string baseKey = $"{where}|{kind}|{type}|{style}|{label}|{tip}|{new string(context.Where(ch => !char.IsDigit(ch)).ToArray())}";
                counts[baseKey] = counts.GetValueOrDefault(baseKey) + 1;
                Rect bounds;
                try { bounds = e.TransformToAncestor(window).TransformBounds(new Rect(e.RenderSize)); }
                catch (InvalidOperationException) { bounds = new Rect(e.RenderSize); }
                found.Add(new Found
                {
                    Element = e, Link = link, Kind = kind, Type = type, Style = style, Label = label, Tip = tip, Context = context,
                    Key = baseKey + "#" + counts[baseKey], Group = ListOf(e) is { } list ? $"{where}|{kind}|{type}|{style}|list{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(list)}" : "", Bounds = bounds,
                    Enabled = e.IsEnabled && (link?.IsEnabled ?? true), View = ViewOf(window, e),
                });
            }

            void Walk(DependencyObject node)
            {
                if (node is UIElement { IsVisible: false }) return;
                if (node is FrameworkElement fe)
                {
                    if (KindOf(fe) is { } kind)
                    {
                        Add(fe, null, kind);
                        if (kind is "pointer surface" or "dropdown" or "slider" or "text input" or "keybind box") return;
                    }
                    if (fe.ContextMenu is not null && fe is not TextBoxBase && !InsideControl(fe)) Add(fe, null, "context menu");
                    if (fe is TextBlock tb)
                        foreach (var link in Links(tb.Inlines)) Add(tb, link, "link");
                }
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
            }
            Walk(root);
        }
        return found;
    }

    private static Panel? ListOf(DependencyObject e)
    {
        for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is Panel { Children.Count: >= 8 } panel && panel is not Grid) return panel;
        return null;
    }

    private static string ItemText(object item) => item switch
    {
        ComboBoxItem { Content: string s } => Plain(s),
        ComboBoxItem { Content: DependencyObject d } => OwnText(d),
        Choice c => Plain(c.Text),
        string s => Plain(s),
        _ => Plain(item.ToString() ?? ""),
    };

    private static IEnumerable<Hyperlink> Links(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Hyperlink h) yield return h;
            else if (inline is Span span)
                foreach (var inner in Links(span.Inlines)) yield return inner;
        }
    }

    private static string ViewOf(MainWindow window, FrameworkElement e)
    {
        try
        {
            var r = e.TransformToAncestor(window).TransformBounds(new Rect(e.RenderSize));
            var client = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer sv && sv.TemplatedParent is not ComboBox && sv.ActualHeight > 0)
                {
                    var port = sv.TransformToAncestor(window).TransformBounds(new Rect(0, 0, sv.ViewportWidth > 0 ? sv.ViewportWidth : sv.ActualWidth, sv.ViewportHeight > 0 ? sv.ViewportHeight : sv.ActualHeight));
                    client.Intersect(port);
                    if (client.IsEmpty) return "scrolled off";
                }
            var shown = Rect.Intersect(r, client);
            if (shown.IsEmpty) return "scrolled off";
            return shown.Width * shown.Height >= r.Width * r.Height - 2 ? "in view" : "partly in view";
        }
        catch (InvalidOperationException)
        {
            return "in a popup";
        }
    }

    private static int Order(Found f)
    {
        string text = (f.Label + " " + f.Tip).ToLowerInvariant();
        if (f.Style.StartsWith("Danger", StringComparison.Ordinal) || Destructive.Any(d => text.Contains(d, StringComparison.Ordinal))) return 2;
        return f.Kind == "tab" ? 1 : 0;
    }

    private static async Task SweepAsync(MainWindow window, Surface s, int depth)
    {
        Log($"=== {s.Name} (depth {depth})");
        if (!await ReachAsync(window, s))
        {
            Log("  could not reach");
            return;
        }
        var census = Collect(window, s);
        if (s.Layer == "layer")
        {
            string title = LayerTitle(window);
            if (PicturedLayers.Add(title)) CaptureLayer(window, s);
            string signature = string.Join(" | ", census.Take(8).Select(f => f.Kind + ":" + f.Style + ":" + (f.Group.Length > 0 ? "row" : f.Label)));
            if (!SweptLayers.Add(signature) && !_late)
            {
                Log("  same controls as a layer already swept: " + title);
                return;
            }
        }
        var radios = census.Where(f => f.Element is RadioButton { IsChecked: true }).Select(f => f.Key).ToList();
        Inspect(window, s.Layer == "layer" ? s with { Name = s.Name + " [" + Short(LayerTitle(window)) + "]" } : s, census);
        await TipsAsync(window, census);

        var children = new List<Surface>();
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var plan = census.OrderBy(Order).ToList();
        bool dirty = false;
        foreach (var item in plan)
        {
            string uniqueKey = UniqueKey(s, item);
            var row = Unique[uniqueKey];
            if (row.Outcome.Length > 0 && !row.Outcome.StartsWith("unreachable", StringComparison.Ordinal)) continue;
            int seen = item.Group.Length == 0 ? 0 : groups[item.Group] = groups.GetValueOrDefault(item.Group) + 1;
            if (seen > RepeatCap && item.Kind is not ("tab" or "segment"))
            {
                row.Outcome = $"skipped: list repeat (exercised {RepeatCap} of this row type)";
                continue;
            }
            if (!item.Enabled) { row.Outcome = "skipped: disabled"; continue; }
            if (!_late && item.Style == "DangerConfirmButton")
            {
                if (!Deferred.Contains(s)) Deferred.Add(s);
                continue;
            }
            if (CaptionNames.Contains(item.Element.Name)) { row.Outcome = "skipped: window caption button (min / max / close)"; continue; }
            if (item.Kind == "pointer surface") { row.Outcome = "skipped: drag / pointer surface (not a click target)"; continue; }
            Found? target = null;
            if (dirty && s.Layer != "layer")
            {
                await CloseLayersAsync(window, s.Layer == "setup");
                if (Alive(window, s, item)) target = item;
            }
            else if (!dirty) target = item;
            if (target is null && dirty)
            {
                if (!await ReachAsync(window, s)) { row.Outcome = "unreachable: surface could not be reopened"; continue; }
                await RestoreRadiosAsync(window, s, radios);
            }
            target ??= Collect(window, s).FirstOrDefault(f => f.Key == item.Key);
            if (target is null && s.Layer != "setup")
            {
                window.Navigate(window.NavKeys.FirstOrDefault(k => k != s.Page) ?? s.Page);
                await Settle(250);
                if (await ReachAsync(window, s))
                {
                    await RestoreRadiosAsync(window, s, radios);
                    target = Collect(window, s).FirstOrDefault(f => f.Key == item.Key);
                }
            }
            if (target is null) { row.Outcome = "unreachable: gone after resetting the surface"; dirty = true; continue; }
            try { target.Element.BringIntoView(); } catch (InvalidOperationException) { }
            int errors = ErrorCount;
            int blocked = Blocked.Count;
            string before = Fingerprint(window);
            var popupsBefore = OpenPopups().Count;
            bool modalBefore = window.ModalLayer.Visibility == Visibility.Visible;
            int toastsBefore = window.Toasts.Children.Count;
            string page = window.CurrentPage ?? "";
            string outcome;
            try { outcome = await ExerciseAsync(window, target); }
            catch (Exception ex) { RecordError(ex); outcome = "threw " + ex.GetType().Name; }
            await Settle(200);
            bool modal = window.ModalLayer.Visibility == Visibility.Visible && !modalBefore;
            bool popup = OpenPopups().Count > popupsBefore;
            bool toast = window.Toasts.Children.Count > toastsBefore;
            string after = Fingerprint(window);
            var effects = new List<string>();
            if (modal) effects.Add("opened a dialog: " + LayerTitle(window));
            if (popup) effects.Add("opened a menu / popup");
            if (toast) effects.Add("notice: " + OwnText(window.Toasts.Children[^1]));
            if (window.CurrentPage != page) effects.Add("went to page " + window.CurrentPage);
            if (Blocked.Count > blocked) effects.Add("asked to leave the app (refused): " + string.Join(", ", Blocked.Skip(blocked)));
            if (effects.Count == 0 && after != before) effects.Add("changed the view");
            if (effects.Count == 0 && outcome is "pressed" or "clicked") AddIssue(row, "no visible effect");
            if (ErrorCount > errors) AddIssue(row, $"error ({ErrorCount - errors})");
            row.Outcome = outcome + (effects.Count > 0 ? "; " + string.Join("; ", effects) : "");
            Log($"  {item.Kind} “{item.Label}” {item.Tip} → {row.Outcome}");
            if (depth < MaxDepth && (modal || popup || toast && window.Toasts.Children[^1] is DependencyObject t && HasControl(t)))
                children.Add(new Surface(s.Page, s.State, s.Path.Append(item.Key).ToList(), "layer", s.Name + " > " + Short(item.Label.Length > 0 ? item.Label : item.Tip.Length > 0 ? item.Tip : item.Kind), s.Base));
            dirty = true;
        }
        foreach (var child in children)
            await SweepAsync(window, child, depth + 1);
    }

    private static bool Alive(MainWindow window, Surface s, Found f)
    {
        if (!f.Element.IsLoaded || !f.Element.IsVisible || PresentationSource.FromVisual(f.Element) is null) return false;
        if (s.Layer == "page" && window.CurrentPage != s.Page) return false;
        if (s.Layer == "setup" && !window.SetupOpen) return false;
        return window.ModalLayer.Visibility != Visibility.Visible;
    }

    private static bool HasControl(DependencyObject node)
    {
        if (node is ButtonBase) return true;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (HasControl(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }

    private static string Short(string text) => text.Length > 40 ? text[..40] : text;

    private static string LayerTitle(MainWindow window)
    {
        if (window.ModalLayer.Visibility == Visibility.Visible && window.ModalContent.Content is DependencyObject d) return OwnText(d);
        return OpenPopups().Select(p => p.Child is DependencyObject c ? OwnText(c) : "").FirstOrDefault() ?? "";
    }

    private static async Task RestoreRadiosAsync(MainWindow window, Surface s, List<string> radios)
    {
        if (radios.Count == 0) return;
        bool changed = false;
        foreach (var f in Collect(window, s))
            if (f.Element is RadioButton { IsChecked: false } rb && radios.Contains(f.Key) && UIElementAutomationPeer.CreatePeerForElement(rb) is ISelectionItemProvider select)
            {
                select.Select();
                changed = true;
            }
        if (changed) await Settle(250);
    }

    private static string UniqueKey(Surface s, Found f) => $"{s.Page}|{(s.Layer == "layer" ? s.Name.Split(" [")[0] : s.Layer)}|{f.Key}";

    private static void Press(Found f)
    {
        if (f.Link is { } link)
        {
            link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, link));
            return;
        }
        switch (f.Element)
        {
            case ButtonBase button when UIElementAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke:
                invoke.Invoke();
                break;
            case ButtonBase button:
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                break;
            case MenuItem item when UIElementAutomationPeer.CreatePeerForElement(item) is IInvokeProvider invoke:
                invoke.Invoke();
                break;
            default:
                Click(f.Element);
                break;
        }
    }

    private static void Click(UIElement e)
    {
        foreach (var routed in new[] { UIElement.PreviewMouseDownEvent, UIElement.MouseDownEvent, UIElement.PreviewMouseUpEvent, UIElement.MouseUpEvent })
            e.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed, Source = e });
    }

    private static async Task<string> ExerciseAsync(MainWindow window, Found f)
    {
        switch (f.Element)
        {
            case ComboBox combo when f.Link is null:
            {
                int old = combo.SelectedIndex;
                combo.IsDropDownOpen = true;
                await Settle(200);
                bool opened = combo.IsDropDownOpen;
                if (opened) LintPopups(window, f);
                combo.IsDropDownOpen = false;
                await Settle(100);
                if (combo.Items.Count < 2) return opened ? $"opened ({combo.Items.Count} item)" : "did not open";
                int pick = old + 1 < combo.Items.Count ? old + 1 : 0;
                combo.SelectedIndex = pick;
                await Settle(250);
                string picked = $"opened ({combo.Items.Count} items); picked “{ItemText(combo.Items[pick])}”";
                if (combo.IsLoaded && combo.SelectedIndex != old) { combo.SelectedIndex = old; await Settle(200); picked += ", put back"; }
                return picked;
            }
            case Slider slider:
            {
                double old = slider.Value;
                slider.Value = slider.Maximum;
                await Settle(150);
                slider.Value = slider.Minimum;
                await Settle(150);
                if (slider.IsLoaded) slider.Value = old;
                return "moved to max, min, put back";
            }
            case TextBox box when f.Link is null:
            {
                if (box.IsReadOnly) return "read only";
                string old = box.Text;
                box.Focus();
                box.Text = new string('W', 120);
                await Settle(150);
                box.Text = "";
                await Settle(150);
                if (box.IsLoaded) box.Text = old;
                Keyboard.Focus(window);
                return "typed long text, emptied, put back";
            }
            case RadioButton rb when f.Link is null:
            {
                if (rb.IsChecked == true) return "already selected";
                if (UIElementAutomationPeer.CreatePeerForElement(rb) is ISelectionItemProvider select) select.Select();
                else rb.IsChecked = true;
                return rb.IsChecked == true ? "selected" : "selection refused";
            }
            case ToggleButton toggle when f.Link is null:
            {
                bool? old = toggle.IsChecked;
                if (UIElementAutomationPeer.CreatePeerForElement(toggle) is IToggleProvider t) t.Toggle();
                else toggle.IsChecked = !(old ?? false);
                await Settle(200);
                bool flipped = toggle.IsChecked != old;
                if (toggle.IsLoaded && toggle.IsVisible && toggle.IsChecked != old && UIElementAutomationPeer.CreatePeerForElement(toggle) is IToggleProvider back)
                {
                    await CloseLayersQuietly(window);
                    if (toggle.IsChecked != old) back.Toggle();
                    await Settle(200);
                    return flipped ? $"switched {(old == true ? "off" : "on")}, then back{(toggle.IsChecked == old ? "" : " (did not go back)")}" : "did not switch";
                }
                return flipped ? $"switched {(old == true ? "off" : "on")}" : "did not switch";
            }
        }
        if (f.Kind == "context menu")
        {
            f.Element.ContextMenu!.PlacementTarget = f.Element;
            f.Element.ContextMenu.IsOpen = true;
            return $"opened its menu ({f.Element.ContextMenu.Items.Count} items)";
        }
        Press(f);
        await Settle(150);
        return f.Kind == "clickable" ? "clicked" : "pressed";
    }

    private static async Task CloseLayersQuietly(MainWindow window)
    {
        if (window.ModalLayer.Visibility == Visibility.Visible && window.ModalContent.Content is not null)
        {
            window.CloseModal(false);
            await Settle(150);
        }
    }

    private static string Fingerprint(MainWindow window)
    {
        var sb = new StringBuilder();
        int elements = 0;
        void Walk(DependencyObject d)
        {
            if (d is UIElement { IsVisible: false }) return;
            elements++;
            switch (d)
            {
                case TextBlock tb when !tb.Text.Any(char.IsDigit): sb.Append(tb.Text).Append('|'); break;
                case ToggleButton t: sb.Append(t.IsChecked).Append('|'); break;
                case ComboBox c: sb.Append(c.SelectedIndex).Append('|'); break;
                case RangeBase r: sb.Append(r.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append('|'); break;
                case TextBox box: sb.Append(box.Text).Append('|'); break;
                case Panel p: sb.Append(p.Children.Count).Append('|'); break;
                case ContentControl cc: sb.Append(cc.Content is null ? '0' : '1'); break;
                case FrameworkElement fe when fe.RenderTransform is RotateTransform r: sb.Append(r.Angle).Append('|'); break;
            }
            int count = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(window);
        foreach (var popup in OpenPopups())
            if (popup.Child is DependencyObject c) Walk(c);
        sb.Append(elements).Append(window.CurrentPage);
        return sb.ToString();
    }

    private static void LintPopups(MainWindow window, Found f)
    {
        foreach (var popup in OpenPopups())
            if (PresentationSource.FromVisual(popup.Child) is { RootVisual: Visual root })
                foreach (var finding in LayoutLint.Inspect(root, $"{window.CurrentPage} dropdown {f.Label}"))
                    lock (Errors) Errors.Add(finding.ToString());
    }

    private static void Inspect(MainWindow window, Surface s, List<Found> census)
    {
        foreach (var f in census)
        {
            string key = UniqueKey(s, f);
            if (Unique.ContainsKey(key)) continue;
            var row = new Row
            {
                Page = s.Page, Surface = s.Name, Kind = f.Kind, Type = f.Type, Style = f.Style, Label = f.Label, Tip = f.Tip, Context = f.Context,
                Key = key, Bounds = f.Bounds, Enabled = f.Enabled, View = f.View,
            };
            Unique[key] = row;
            Rows.Add(row);
            Check(window, f, row);
        }
        CheckRows(s, census);
        CheckPrimaries(s, census);
    }

    private static void AddIssue(Row row, string issue)
    {
        if (!row.Issues.Contains(issue)) row.Issues.Add(issue);
    }

    private static void Check(MainWindow window, Found f, Row row)
    {
        bool labelled = f.Label.Length > 0 && !f.Label.StartsWith('#') || f.Tip.Length > 0;
        if (!labelled && f.Kind is not ("pointer surface" or "text input" or "slider" or "dropdown" or "keybind box"))
            AddIssue(row, "no label, tooltip or name");
        if (f.Kind == "icon button" && f.Tip.Length == 0) AddIssue(row, "icon-only without a tooltip");
        if (f.Enabled && f.Element is ButtonBase && AutomationProperties.GetName(f.Element) is { Length: > 0 } name && f.Element.ToolTip is string tip && Opposite(FirstWord(name), FirstWord(Plain(tip))))
            AddIssue(row, $"name: “{name}” but tooltip “{Plain(tip)}”");
        if (f.Kind is "button" or "icon button" or "dropdown" or "tab" or "segment" && f.Bounds.Height > 0 && f.Bounds.Height < 26 && f.Style is not ("QuietButton" or "QuietButton+" or "custom" or "CaptionButton" or "CloseCaptionButton"))
            AddIssue(row, $"short: {f.Bounds.Height:0.#} px tall");
        if (f.Kind is not ("pointer surface" or "link") && f.Enabled && (f.Bounds.Width < 16 || f.Bounds.Height < 16) && f.Bounds.Width > 0)
            AddIssue(row, $"small target: {f.Bounds.Width:0}×{f.Bounds.Height:0}");
        if (f.Element is Button b && ButtonRoles.Contains(f.Style.TrimEnd('+')))
        {
            var local = new List<string>();
            foreach (var dp in new DependencyProperty[] { Control.BackgroundProperty, Control.BorderBrushProperty, Control.ForegroundProperty, FrameworkElement.HeightProperty, FrameworkElement.MinHeightProperty, Control.FontSizeProperty, Control.FontWeightProperty, Control.PaddingProperty })
                if (DependencyPropertyHelper.GetValueSource(b, dp).BaseValueSource == BaseValueSource.Local) local.Add(dp.Name);
            if (local.Count > 0) AddIssue(row, "look set locally: " + string.Join(", ", local));
        }
        if (f.View == "partly in view" && Clipped(f.Element)) AddIssue(row, "clipped by a parent");
    }

    private static readonly (string A, string B)[] Opposites =
    {
        ("show", "hide"), ("open", "close"), ("play", "pause"), ("start", "stop"), ("expand", "collapse"), ("fold", "unfold"), ("mute", "unmute"), ("pin", "unpin"), ("on", "off"),
    };

    private static string FirstWord(string text) => text.Split(' ', 2)[0].Trim().ToLowerInvariant();

    private static bool Opposite(string a, string b) => Opposites.Any(o => (o.A == a && o.B == b) || (o.A == b && o.B == a));

    private static bool Clipped(FrameworkElement e)
    {
        for (DependencyObject? p = VisualTreeHelper.GetParent(e); p is FrameworkElement parent; p = VisualTreeHelper.GetParent(p))
        {
            if (parent is ScrollContentPresenter or ScrollViewer or Popup) return false;
            if (!parent.ClipToBounds && LayoutInformation.GetLayoutClip(parent) is null) continue;
            try
            {
                var r = e.TransformToAncestor(parent).TransformBounds(new Rect(e.RenderSize));
                if (r.Left < -1 || r.Top < -1 || r.Right > parent.ActualWidth + 1 || r.Bottom > parent.ActualHeight + 1) return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        return false;
    }

    private static void CheckRows(Surface s, List<Found> census)
    {
        var sized = census.Where(f => f.View == "in view" && f.Kind is "button" or "icon button" or "dropdown" or "text input" or "keybind box" or "segment" or "toggle").ToList();
        var byParent = sized.GroupBy(f => RowPanel(f.Element));
        foreach (var group in byParent)
        {
            var items = group.OrderBy(f => f.Bounds.Left).ToList();
            for (int i = 0; i + 1 < items.Count; i++)
            {
                var a = items[i];
                var b = items[i + 1];
                double overlapY = Math.Min(a.Bounds.Bottom, b.Bounds.Bottom) - Math.Max(a.Bounds.Top, b.Bounds.Top);
                if (overlapY < Math.Min(a.Bounds.Height, b.Bounds.Height) * 0.5) continue;
                double gap = b.Bounds.Left - a.Bounds.Right;
                if (!Unique.TryGetValue(UniqueKey(s, b), out var row)) continue;
                if (gap < -1) AddIssue(row, $"overlaps “{a.Label}{a.Tip}” by {-gap:0.#} px");
                else if (gap < 3.5 && !(a.Kind == "segment" && b.Kind == "segment")) AddIssue(row, $"{gap:0.#} px from “{Short(a.Label.Length > 0 ? a.Label : a.Tip)}”");
                if (Math.Abs(a.Bounds.Height - b.Bounds.Height) > 1.5 && a.Kind != "text input" && b.Kind != "text input")
                    AddIssue(row, $"height {b.Bounds.Height:0.#} beside “{Short(a.Label.Length > 0 ? a.Label : a.Tip)}” at {a.Bounds.Height:0.#}");
                else if (Math.Abs((a.Bounds.Top + a.Bounds.Bottom) / 2 - (b.Bounds.Top + b.Bounds.Bottom) / 2) > 1.5)
                    AddIssue(row, $"not centered with “{Short(a.Label.Length > 0 ? a.Label : a.Tip)}” ({(b.Bounds.Top + b.Bounds.Bottom) / 2 - (a.Bounds.Top + a.Bounds.Bottom) / 2:+0.#;-0.#} px)");
            }
        }
    }

    private static DependencyObject? RowPanel(FrameworkElement e)
    {
        for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is Panel { Children.Count: > 1 }) return p;
        return null;
    }

    private static void CheckPrimaries(Surface s, List<Found> census)
    {
        var primaries = census.Where(f => f.View == "in view" && f.Style == "PrimaryButton").ToList();
        foreach (var group in primaries.GroupBy(f => CardOf(f.Element)))
            if (group.Key is not null && group.Count() > 1)
                foreach (var f in group.Skip(1))
                    if (Unique.TryGetValue(UniqueKey(s, f), out var row))
                        AddIssue(row, $"second primary button in one card (with “{group.First().Label}”)");
    }

    private static DependencyObject? CardOf(DependencyObject e)
    {
        var card = Application.Current.TryFindResource("Card") as Style;
        var lifted = Application.Current.TryFindResource("LiftedCard") as Style;
        for (var p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is Border b && (ReferenceEquals(b.Style, card) || ReferenceEquals(b.Style, lifted))) return b;
        return null;
    }

    private static async Task TipsAsync(MainWindow window, List<Found> census)
    {
        foreach (var f in census)
        {
            if (f.View != "in view" || f.Element.ToolTip is not (string or ToolTip { Content: string })) continue;
            if (!TipsSeen.Add(f.Tip)) continue;
            ToolTip tip;
            bool own = f.Element.ToolTip is ToolTip;
            if (f.Element.ToolTip is ToolTip existing) tip = existing;
            else tip = new ToolTip { Content = f.Element.ToolTip };
            tip.PlacementTarget = f.Element;
            try
            {
                tip.IsOpen = true;
                await Settle(60);
                if (PresentationSource.FromVisual(tip) is { RootVisual: FrameworkElement root })
                {
                    var tipRect = new Rect(root.PointToScreen(new Point()), root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight)));
                    var controlRect = new Rect(f.Element.PointToScreen(new Point()), f.Element.PointToScreen(new Point(f.Element.ActualWidth, f.Element.ActualHeight)));
                    var overlap = Rect.Intersect(tipRect, controlRect);
                    string where = $"“{Short(f.Label.Length > 0 ? f.Label : f.Kind)}” on {window.CurrentPage}";
                    if (!overlap.IsEmpty && overlap.Width > 1 && overlap.Height > 1) TipFindings.Add($"covers its control: {where}: “{Short(f.Tip)}”");
                    if (f.Tip.Length > 260) TipFindings.Add($"long ({f.Tip.Length} characters): {where}: “{Short(f.Tip)}…”");
                    foreach (var finding in LayoutLint.Inspect(root, "tooltip " + where)) TipFindings.Add(finding.ToString());
                    if (_shot < 900) SaveVisual(root, Path.Combine(_folder, "tips", $"{++_shot:000}.png"));
                    File.AppendAllText(Path.Combine(_folder, "tips.txt"), $"{_shot:000}\t{window.CurrentPage}\t{f.Kind}\t{f.Label}\t{f.Tip}{Environment.NewLine}");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                TipFindings.Add($"could not open: “{Short(f.Tip)}”: {ex.Message}");
            }
            finally
            {
                tip.IsOpen = false;
                if (!own) tip.Content = null;
            }
        }
    }

    private static void SaveVisual(FrameworkElement root, string path)
    {
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0) return;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void CaptureLayer(MainWindow window, Surface s)
    {
        string name = string.Concat(s.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        if (name.Length > 90) name = name[..90];
        string path = Path.Combine(_folder, "layers", $"{PicturedLayers.Count:000}-{name}.png");
        try
        {
            ScreenshotRunner.CaptureWindow(window, path);
            foreach (var finding in LayoutLint.Inspect(window.ModalLayer.Visibility == Visibility.Visible ? window.ModalLayer : window, s.Name))
                lock (Errors) Errors.Add(finding.ToString());
        }
        catch (Exception ex) { RecordError(ex); }
    }

    private static void Write()
    {
        static string Cell(string s) => s.Replace('\t', ' ').Replace('\n', ' ');
        var inv = new StringBuilder("page\tsurface\tkind\ttype\tstyle\tlabel\ttooltip\tcontext\tsize\tview\tenabled\toutcome\tissues\n");
        foreach (var r in Rows)
            inv.Append(Cell(r.Page)).Append('\t').Append(Cell(r.Surface)).Append('\t').Append(r.Kind).Append('\t').Append(r.Type).Append('\t').Append(r.Style).Append('\t')
                .Append(Cell(r.Label)).Append('\t').Append(Cell(r.Tip)).Append('\t').Append(Cell(r.Context)).Append('\t')
                .Append($"{r.Bounds.Width:0}x{r.Bounds.Height:0}").Append('\t').Append(r.View).Append('\t').Append(r.Enabled ? "yes" : "no").Append('\t')
                .Append(Cell(r.Outcome)).Append('\t').Append(Cell(string.Join("; ", r.Issues))).Append('\n');
        File.WriteAllText(Path.Combine(_folder, "inventory.tsv"), inv.ToString());

        var sum = new StringBuilder();
        sum.AppendLine($"controls found: {Rows.Count} (unique per page / surface; list rows past {RepeatCap} of one kind counted, not pressed)");
        sum.AppendLine($"surfaces: {Rows.Select(r => r.Surface).Distinct().Count()}, dialogs / menus / notices reached: {PicturedLayers.Count} (by title), {SweptLayers.Count} with different controls swept");
        sum.AppendLine();
        sum.AppendLine("kind\tfound\texercised\tskipped\tunreachable\tnot reached\twith issues");
        foreach (var g in Rows.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()))
        {
            int exercised = g.Count(r => r.Outcome.Length > 0 && !r.Outcome.StartsWith("skipped", StringComparison.Ordinal) && !r.Outcome.StartsWith("unreachable", StringComparison.Ordinal));
            int skipped = g.Count(r => r.Outcome.StartsWith("skipped", StringComparison.Ordinal));
            int unreachable = g.Count(r => r.Outcome.StartsWith("unreachable", StringComparison.Ordinal));
            int none = g.Count(r => r.Outcome.Length == 0);
            sum.AppendLine($"{g.Key}\t{g.Count()}\t{exercised}\t{skipped}\t{unreachable}\t{none}\t{g.Count(r => r.Issues.Count > 0)}");
        }
        sum.AppendLine();
        sum.AppendLine("styles of buttons: " + string.Join(", ", Rows.Where(r => r.Kind is "button" or "icon button").GroupBy(r => r.Style).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        sum.AppendLine("skipped: " + string.Join(", ", Rows.Where(r => r.Outcome.StartsWith("skipped", StringComparison.Ordinal)).GroupBy(r => r.Outcome).Select(g => $"{g.Key} ×{g.Count()}")));
        sum.AppendLine();
        sum.AppendLine("issues:");
        foreach (var g in Rows.SelectMany(r => r.Issues.Select(i => (Issue: Category(i), r))).GroupBy(x => x.Issue).OrderByDescending(g => g.Count()))
            sum.AppendLine($"  {g.Key}: {g.Count()}");
        sum.AppendLine();
        sum.AppendLine($"tooltips opened: {TipsSeen.Count}, findings {TipFindings.Count}");
        foreach (var t in TipFindings.Distinct()) sum.AppendLine("  " + t);
        File.WriteAllText(Path.Combine(_folder, "summary.txt"), sum.ToString());
    }

    private static string Category(string issue)
    {
        int colon = issue.IndexOf(':');
        if (issue.Contains(" px from ", StringComparison.Ordinal)) return "closer than 4 px to a neighbor";
        if (issue.StartsWith("overlaps", StringComparison.Ordinal)) return "overlaps a neighbor";
        if (issue.StartsWith("height ", StringComparison.Ordinal)) return "height differs from its neighbor";
        if (issue.StartsWith("not centered", StringComparison.Ordinal)) return "not centered with its neighbor";
        if (issue.StartsWith("second primary", StringComparison.Ordinal)) return "second primary button in a card";
        if (issue.StartsWith("error", StringComparison.Ordinal)) return "error";
        return colon > 0 ? issue[..colon] : issue;
    }
}
