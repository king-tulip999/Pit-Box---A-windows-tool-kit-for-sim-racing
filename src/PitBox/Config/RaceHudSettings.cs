namespace PitBox.Config;

public enum HudShowWhen
{
    Driving,
    Connected,
    Always,
}

public enum HudUnits { Metric, Imperial }

public enum HudDensity { Compact, Standard, Expanded }

public enum HudAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

public enum HudColors { Default, Custom }

public sealed class RaceHudSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; }

    public bool Enabled { get; set; }

    public HudShowWhen ShowWhen { get; set; } = HudShowWhen.Driving;

    public bool Interactive { get; set; }

    public string ActiveLayout { get; set; } = SimRacing.Hud.HudPresets.DefaultName;

    public bool AutoSwitch { get; set; } = false;

    public List<HudLayout> Layouts { get; set; } = new();

    public List<HudLayoutRule> Rules { get; set; } = new();

    public HudAppearance Appearance { get; set; } = new();

    public HudUnits Units { get; set; } = HudUnits.Metric;

    public const int DefaultWebPort = 8642, MinWebPort = 1024, MaxWebPort = 65535;

    public bool WebOverlays { get; set; }

    public int WebPort { get; set; } = DefaultWebPort;

    public const int MinGrid = 2, MaxGrid = 64, GridStep = 2;

    public int GridSize { get; set; } = 12;

    public bool Snap { get; set; } = true;

    public bool GridShown { get; set; }

    public HudFuelSettings Fuel { get; set; } = new();

    public Dictionary<string, HudAlertSetting> Alerts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> AlertGroupOrder { get; set; } = new();

    public List<string> AlertGroupsFolded { get; set; } = new();

    public List<string> FoldedPanels { get; set; } = new();

    public List<string> EditorSectionsToggled { get; set; } = new();

    public List<TrackNote> Notes { get; set; } = new();

    public const int MaxNotes = 500, NoteLength = 160;

    public IEnumerable<TrackNote> NotesFor(string track, string config, string car) =>
        Notes.Where(n => n.Matches(track, config, car));

    public List<string> KnownClasses { get; set; } = new();
    public List<string> KnownCars { get; set; } = new();
    public List<string> KnownTracks { get; set; } = new();

    public const int MaxKnown = 200;

    public bool Remember(string? carClass, string? car, string? track)
    {
        static bool Add(List<string> list, string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || list.Count >= MaxKnown || list.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)) return false;
            list.Add(name.Trim());
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return true;
        }
        bool a = Add(KnownClasses, carClass), b = Add(KnownCars, car), c = Add(KnownTracks, track);
        return a || b || c;
    }

    public static string SessionKind(string session) => string.Equals(session, "Warmup", StringComparison.OrdinalIgnoreCase) ? "Practice" : session;

    public static readonly string[] RemovedKinds = { "blindspot", "spotter", "h2h", "pit", "flags", "dash", "trackmap", "radio", "spotterbox" };

    public string? ToggleHotkey { get; set; }
    public string? EditHotkey { get; set; }
    public string? NextLayoutHotkey { get; set; }
    public string? PreviousLayoutHotkey { get; set; }
    public string? DismissHotkey { get; set; }
    public string? QuickEditHotkey { get; set; } = QuickEditDefault;
    public const string QuickEditDefault = "Ctrl+Alt+E";

    public HudLayout? Layout(string? name) => Layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public HudLayout? Resolve(string session, string car, string track, string carClass)
    {
        if (AutoSwitch)
        {
            HudLayoutRule? best = null;
            int bestScore = -1;
            foreach (var rule in Rules)
            {
                if (Layout(rule.Layout) is null || !rule.Matches(session, car, track, carClass)) continue;
                int score = rule.Specificity * 8 + rule.ExactFields(car, track, carClass);
                if (score > bestScore) { best = rule; bestScore = score; }
            }
            if (best is not null) return Layout(best.Layout);
        }
        return Layout(ActiveLayout) ?? Layouts.FirstOrDefault();
    }

    public bool RenameLayout(string from, string to)
    {
        to = to.Trim();
        var layout = Layout(from);
        if (layout is null || to.Length == 0 || IsDefaultName(layout.Name) || IsDefaultName(to)
            || Layouts.Any(l => l != layout && string.Equals(l.Name, to, StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var rule in Rules.Where(r => string.Equals(r.Layout, layout.Name, StringComparison.OrdinalIgnoreCase))) rule.Layout = to;
        if (string.Equals(ActiveLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) ActiveLayout = to;
        layout.Name = to;
        return true;
    }

    public bool DeleteLayout(string name)
    {
        var layout = Layout(name);
        if (layout is null || Layouts.Count <= 1 || IsDefaultName(layout.Name)) return false;
        Layouts.Remove(layout);
        Rules.RemoveAll(r => string.Equals(r.Layout, layout.Name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(ActiveLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) ActiveLayout = Layouts[0].Name;
        return true;
    }

    public static bool IsDefaultName(string name) => string.Equals(name, SimRacing.Hud.HudPresets.DefaultName, StringComparison.OrdinalIgnoreCase);

    public string? MarkCustomized(Func<HudLayout, bool> isDefault, Func<HudLayout> preset)
    {
        var layout = Layouts.FirstOrDefault(l => IsDefaultName(l.Name));
        if (layout is null || isDefault(layout)) return null;
        var custom = new HudLayout { Name = UniqueName(SimRacing.Hud.HudPresets.CustomName), Widgets = layout.Widgets };
        layout.Widgets = preset().Widgets.Select(w => w.Copy()).ToList();
        foreach (var w in layout.Widgets) w.Clamp();
        Layouts.Insert(Layouts.IndexOf(layout) + 1, custom);
        if (IsDefaultName(ActiveLayout)) ActiveLayout = custom.Name;
        return custom.Name;
    }

    public string ResetToDefault(string name, HudLayout preset)
    {
        var layout = Layout(name);
        if (layout is null) return name;
        layout.Widgets = preset.Widgets.Select(w => w.Copy()).ToList();
        foreach (var w in layout.Widgets) w.Clamp();
        return layout.Name;
    }

    public string UniqueName(string wanted)
    {
        wanted = string.IsNullOrWhiteSpace(wanted) ? "Layout" : wanted.Trim();
        string name = Fit(wanted, "");
        for (int n = 2; Layout(name) is not null; n++) name = Fit(wanted, $" {n}");
        return name;
    }

    private static string Fit(string name, string suffix) =>
        (name.Length + suffix.Length > Views.Ui.NameLength ? name[..(Views.Ui.NameLength - suffix.Length)].TrimEnd() : name) + suffix;

    public void Normalize(Func<List<HudLayout>> defaults, Func<List<HudLayoutRule>> defaultRules)
    {
        Layouts ??= new();
        Rules ??= new();
        if (Version < 1 && Layouts.Count == 0)
        {
            Layouts.AddRange(defaults());
            Rules.AddRange(defaultRules());
        }
        bool before2 = Version < 2;
        Version = CurrentVersion;
        Appearance ??= new();
        if (WebPort is < MinWebPort or > MaxWebPort) WebPort = DefaultWebPort;
        var builtIn = defaults();
        if (!Layouts.Any(l => l is not null && IsDefaultName(l.Name ?? ""))) Layouts.Add(builtIn[0]);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in Layouts.ToList())
        {
            layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "Layout" : layout.Name.Trim();
            if (!names.Add(layout.Name)) { layout.Name = UniqueNameAmong(names, layout.Name); names.Add(layout.Name); }
            layout.Widgets ??= new();
            layout.Widgets.RemoveAll(w => w is null || RemovedKinds.Contains(w.Kind, StringComparer.OrdinalIgnoreCase));
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in layout.Widgets)
            {
                w.Kind ??= "";
                if (string.IsNullOrWhiteSpace(w.Id) || !ids.Add(w.Id)) { w.Id = HudWidgetConfig.NewId(w.Kind); ids.Add(w.Id); }
                SideBannerToTop(w);
                OldInputStyles(w);
                if (before2) ShowClassesInsteadOfHiding(w);
                w.Clamp();
            }
        }
        Rules.RemoveAll(r => r is null || Layout(r.Layout) is null);
        foreach (var r in Rules) if (r.Session is not null) r.Session = SessionKind(r.Session);
        Rules = Rules.DistinctBy(r => (r.Layout.ToLowerInvariant(), r.Session?.ToLowerInvariant(), r.Class?.ToLowerInvariant(), r.Car?.ToLowerInvariant(), r.Track?.ToLowerInvariant())).ToList();
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rules)
            if (string.IsNullOrWhiteSpace(r.Id) || !ruleIds.Add(r.Id)) { r.Id = Guid.NewGuid().ToString("N"); ruleIds.Add(r.Id); }
        AlertGroupOrder = (AlertGroupOrder ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AlertGroupsFolded = (AlertGroupsFolded ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        FoldedPanels = (FoldedPanels ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        EditorSectionsToggled = (EditorSectionsToggled ?? new()).Where(x => !string.IsNullOrWhiteSpace(x) && (!before2 || x.Contains(':'))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Layout(ActiveLayout) is null) ActiveLayout = Layouts[0].Name;
        Appearance ??= new();
        Appearance.Clamp();
        Fuel ??= new();
        Fuel.Clamp();
        GridSize = Math.Clamp(GridSize, MinGrid, MaxGrid);
        var alerts = new Dictionary<string, HudAlertSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, a) in Alerts ?? new()) if (id is not null && a is not null) alerts[id] = a;
        Alerts = alerts;
        static List<string> Clean(List<string>? list) =>
            (list ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(MaxKnown).ToList();
        KnownClasses = Clean(KnownClasses);
        KnownCars = Clean(KnownCars);
        KnownTracks = Clean(KnownTracks);
        var noteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Notes = (Notes ?? new()).Where(n => n is not null && !string.IsNullOrWhiteSpace(n.Track) && !string.IsNullOrWhiteSpace(n.Text)).Take(MaxNotes).ToList();
        foreach (var n in Notes)
        {
            n.Clean();
            if (string.IsNullOrWhiteSpace(n.Id) || !noteIds.Add(n.Id)) { n.Id = Guid.NewGuid().ToString("N"); noteIds.Add(n.Id); }
        }
    }

    internal void ShowClassesInsteadOfHiding(HudWidgetConfig w) =>
        SimRacing.Hud.Editors.HudClassFilter.Migrate(w, (KnownClasses ?? new()).Concat(SimRacing.Race.SessionNames.Classes));

    internal static void OldInputStyles(HudWidgetConfig w)
    {
        if (!string.Equals(w.Kind, "inputs", StringComparison.OrdinalIgnoreCase) || w.Options is null || !w.Options.TryGetValue("style", out var style)) return;
        if (style == "tach")
        {
            w.Options.Remove("style");
            double factor = w.W / 188;
            w.W = Math.Max(1, Math.Round(420 * factor));
            w.H = Math.Max(1, Math.Round(110 * factor));
        }
        else if (style == "strip")
        {
            w.Options["style"] = "lights";
            double factor = w.H / 50;
            w.W = Math.Max(1, Math.Round(470 * factor));
            w.H = Math.Max(1, Math.Round(66 * factor));
        }
    }

    internal static void SideBannerToTop(HudWidgetConfig w)
    {
        if (w.Options is null || !w.Options.TryGetValue(SimRacing.Hud.HudFlags.BannerOption.Key, out var where) || where != SimRacing.Hud.HudFlags.OldSide) return;
        var old = SimRacing.Hud.HudFlags.OldSideRoom;
        var (l, t, r, b) = SimRacing.Hud.HudFlags.Room("top");
        w.Options[SimRacing.Hud.HudFlags.BannerOption.Key] = "top";
        w.W = Math.Max(1, w.W + l + r - old.L - old.R);
        w.H = Math.Max(1, w.H + t + b - old.T - old.B);
    }

    private static string UniqueNameAmong(HashSet<string> taken, string wanted)
    {
        string name = wanted;
        for (int n = 2; taken.Contains(name); n++) name = $"{wanted} {n}";
        return name;
    }
}

public sealed class HudLayout
{
    public string Name { get; set; } = "Layout";
    public List<HudWidgetConfig> Widgets { get; set; } = new();

    public HudLayout Copy(string name) => new()
    {
        Name = name,
        Widgets = Widgets.Select(w => w.Copy()).ToList(),
    };
}

public sealed class HudWidgetConfig
{
    public const double MinSize = 40, MaxSize = 4000;

    public string Id { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Monitor { get; set; } = "primary";

    public HudAnchor Anchor { get; set; } = HudAnchor.TopLeft;

    public double X { get; set; }
    public double Y { get; set; }

    public double W { get; set; } = 300;
    public double H { get; set; } = 200;

    public bool Locked { get; set; }

    public int Opacity { get; set; } = 100;

    public int UpdateHz { get; set; }

    public List<string> Sessions { get; set; } = new();

    public bool HideInReplay { get; set; } = true;

    public bool? OnlyWhenNeeded { get; set; }

    public bool OnlyOnTrack { get; set; }

    public int? Background { get; set; }

    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string NewId(string kind) => $"{(string.IsNullOrEmpty(kind) ? "widget" : kind)}-{Guid.NewGuid().ToString("N")[..6]}";

    public void Clamp()
    {
        W = double.IsFinite(W) ? Math.Clamp(W, MinSize, MaxSize) : 300;
        H = double.IsFinite(H) ? Math.Clamp(H, MinSize, MaxSize) : 200;
        X = double.IsFinite(X) ? Math.Clamp(X, -MaxSize, MaxSize) : 0;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, -MaxSize, MaxSize) : 0;
        Opacity = Math.Clamp(Opacity, 20, 100);
        if (Background is { } bg) Background = Math.Clamp(bg, 0, 100);
        UpdateHz = Math.Clamp(UpdateHz, 0, 60);
        Monitor = string.IsNullOrWhiteSpace(Monitor) ? "primary" : Monitor;
        Sessions = (Sessions ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(RaceHudSettings.SessionKind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Options ?? new()) if (k is not null && v is not null) options[k] = v;
        Options = options;
    }

    public HudWidgetConfig Copy()
    {
        var copy = (HudWidgetConfig)MemberwiseClone();
        copy.Sessions = new List<string>(Sessions);
        copy.Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    public bool ShowsIn(string session) => Sessions.Count == 0 || Sessions.Any(s => string.Equals(s, session, StringComparison.OrdinalIgnoreCase));
}

public sealed class TrackNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Track { get; set; } = "";
    public string Config { get; set; } = "";
    public string? Car { get; set; }
    public double? Pct { get; set; }
    public string? Turn { get; set; }
    public string Text { get; set; } = "";

    public bool Matches(string track, string config, string car) =>
        string.Equals(Track, track.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(Config, config.Trim(), StringComparison.OrdinalIgnoreCase)
        && (Car is null || string.Equals(Car, car.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Clean()
    {
        Track = (Track ?? "").Trim();
        Config = (Config ?? "").Trim();
        Car = string.IsNullOrWhiteSpace(Car) ? null : Car.Trim();
        Text = (Text ?? "").Trim();
        if (Text.Length > RaceHudSettings.NoteLength) Text = Text[..RaceHudSettings.NoteLength].TrimEnd();
        Pct = Pct is { } p && double.IsFinite(p) ? p - Math.Floor(p) : null;
        Turn = Pct is null || string.IsNullOrWhiteSpace(Turn) ? null : Turn.Trim();
    }
}

public sealed class HudLayoutRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Layout { get; set; } = "";
    public string? Session { get; set; }
    public string? Car { get; set; }
    public string? Track { get; set; }
    public string? Class { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Specificity => (Session is null ? 0 : 1) + (Car is null ? 0 : 1) + (Track is null ? 0 : 1) + (Class is null ? 0 : 1);

    public int ExactFields(string car, string track, string carClass) =>
        (Same(Car, car) ? 1 : 0) + (Same(Track, track) ? 1 : 0) + (Same(Class, carClass) ? 1 : 0);

    private static bool Same(string? want, string actual) =>
        !string.IsNullOrWhiteSpace(want) && string.Equals(want.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool Matches(string session, string car, string track, string carClass) =>
        Has(Session, session, exact: true) && Has(Car, car) && Has(Track, track) && Has(Class, carClass);

    private static bool Has(string? want, string actual, bool exact = false) =>
        string.IsNullOrWhiteSpace(want) || (exact ? string.Equals(want.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase) : actual.Contains(want.Trim(), StringComparison.OrdinalIgnoreCase));

    public string Describe()
    {
        var parts = new List<string>();
        if (Session is not null) parts.Add(Session + " sessions");
        if (Class is not null) parts.Add("class " + Class);
        if (Car is not null) parts.Add(Car);
        if (Track is not null) parts.Add("at " + Track);
        return parts.Count == 0 ? "Every session" : string.Join(", ", parts);
    }
}

public sealed class HudAppearance
{
    public HudColors Colors { get; set; } = HudColors.Default;

    public HudCustomColors Custom { get; set; } = new();

    public int Background { get; set; } = 75;

    public int FontScale { get; set; } = 100;

    public HudDensity Density { get; set; } = HudDensity.Compact;

    public int Corner { get; set; }

    public bool ReducedMotion { get; set; }

    public bool Outline { get; set; } = false;

    public void Clamp()
    {
        Custom ??= new();
        Custom.Clean();
        Background = Math.Clamp(Background, 0, 100);
        FontScale = 100;
        Density = HudDensity.Compact;
        ReducedMotion = false;
        Corner = Math.Clamp(Corner, 0, 24);
    }
}

public enum FuelSafety { Aggressive, Balanced, Conservative }

public sealed class HudFuelSettings
{
    public int Window { get; set; } = 5;

    public FuelSafety Safety { get; set; } = FuelSafety.Balanced;

    public double MarginLaps { get; set; } = 0.5;

    public double ReserveLiters { get; set; }

    public double ExtraPercent { get; set; }

    public double ExtraKm { get; set; }

    public double MaxStintMinutes { get; set; }

    public double PitLaneLossSeconds { get; set; } = 25;

    public void Clamp()
    {
        Window = Math.Clamp(Window, 1, 30);
        MarginLaps = double.IsFinite(MarginLaps) ? Math.Clamp(MarginLaps, 0, 10) : 0.5;
        ReserveLiters = double.IsFinite(ReserveLiters) ? Math.Clamp(ReserveLiters, 0, 50) : 0;
        ExtraPercent = double.IsFinite(ExtraPercent) ? Math.Clamp(ExtraPercent, 0, 25) : 0;
        ExtraKm = double.IsFinite(ExtraKm) ? Math.Clamp(ExtraKm, 0, 50) : 0;
        MaxStintMinutes = double.IsFinite(MaxStintMinutes) ? Math.Clamp(MaxStintMinutes, 0, 600) : 0;
        PitLaneLossSeconds = double.IsFinite(PitLaneLossSeconds) ? Math.Clamp(PitLaneLossSeconds, 0, 300) : 25;
    }
}

public sealed class HudAlertSetting
{
    public bool? Enabled { get; set; }
    public double? Cooldown { get; set; }
    public double? Threshold { get; set; }
    public double? Duration { get; set; }
}

public sealed class HudCustomColors
{
    public string Background { get; set; } = "#0A0D16";
    public string Surface { get; set; } = "#151B2B";
    public string Text { get; set; } = "#E9ECF5";
    public string Heading { get; set; } = "#FFFFFF";
    public string Muted { get; set; } = "#8A93AB";
    public string Lines { get; set; } = "#2B3348";
    public string Accent { get; set; } = "#DB156B";
    public string YourRow { get; set; } = "#DB156B";
    public string YourRowText { get; set; } = "#FFFFFF";
    public string Good { get; set; } = "#3DD68C";
    public string Warn { get; set; } = "#F5B53D";
    public string Danger { get; set; } = "#E5484D";
    public string FastestLap { get; set; } = "#B06BFF";
    public string Lapping { get; set; } = "#F87171";
    public string Lapped { get; set; } = "#60A5FA";
    public string MapTrack { get; set; } = "#535A68";
    public string MapEdge { get; set; } = "#8B919C";
    public string MapPit { get; set; } = "#3C4353";
    public string MapCars { get; set; } = "#CFD4DA";

    public IEnumerable<(string Id, Func<string> Get, Action<string> Set)> All()
    {
        yield return (nameof(Background), () => Background, v => Background = v);
        yield return (nameof(Surface), () => Surface, v => Surface = v);
        yield return (nameof(Text), () => Text, v => Text = v);
        yield return (nameof(Heading), () => Heading, v => Heading = v);
        yield return (nameof(Muted), () => Muted, v => Muted = v);
        yield return (nameof(Lines), () => Lines, v => Lines = v);
        yield return (nameof(Accent), () => Accent, v => Accent = v);
        yield return (nameof(YourRow), () => YourRow, v => YourRow = v);
        yield return (nameof(YourRowText), () => YourRowText, v => YourRowText = v);
        yield return (nameof(Good), () => Good, v => Good = v);
        yield return (nameof(Warn), () => Warn, v => Warn = v);
        yield return (nameof(Danger), () => Danger, v => Danger = v);
        yield return (nameof(FastestLap), () => FastestLap, v => FastestLap = v);
        yield return (nameof(Lapping), () => Lapping, v => Lapping = v);
        yield return (nameof(Lapped), () => Lapped, v => Lapped = v);
        yield return (nameof(MapTrack), () => MapTrack, v => MapTrack = v);
        yield return (nameof(MapEdge), () => MapEdge, v => MapEdge = v);
        yield return (nameof(MapPit), () => MapPit, v => MapPit = v);
        yield return (nameof(MapCars), () => MapCars, v => MapCars = v);
    }

    public void Clean()
    {
        var defaults = new HudCustomColors().All().ToDictionary(e => e.Id, e => e.Get());
        static bool Valid(string? v) => v is { Length: 7 } && v[0] == '#' && v.Skip(1).All(Uri.IsHexDigit);
        foreach (var (id, get, set) in All())
            set(Valid(get()) ? get().ToUpperInvariant() : defaults[id]);
    }
}

public sealed record HudColorSet(string Name, int Goals, HudCustomColors Colors);

public static class HudColorSets
{
    private static HudCustomColors Set(string background, string surface, string text, string muted, string lines, string accent, string yourRow, string yourRowText,
        string mapTrack, string mapEdge, string mapPit, string mapCars, string heading = "#FFFFFF", string good = "#3DD68C", string warn = "#F5B53D", string danger = "#E5484D",
        string fastest = "#B06BFF", string lapping = "#F87171", string lapped = "#60A5FA") => new()
    {
        Background = background, Surface = surface, Text = text, Heading = heading, Muted = muted, Lines = lines, Accent = accent, YourRow = yourRow, YourRowText = yourRowText,
        Good = good, Warn = warn, Danger = danger, FastestLap = fastest, Lapping = lapping, Lapped = lapped, MapTrack = mapTrack, MapEdge = mapEdge, MapPit = mapPit, MapCars = mapCars,
    };

    public static readonly HudColorSet[] All =
    {
        new("Pit lane", 1, Set("#070D0A", "#111D17", "#E4F1EB", "#82B099", "#23392E", "#2DD27A", "#25AD64", "#FFFFFF", "#516159", "#879B91", "#36453D", "#D4DDD9")),
        new("Night race", 2, Set("#06080F", "#0E1220", "#E2E6F3", "#828DB0", "#1D253E", "#42C6FA", "#0BB5F9", "#FFFFFF", "#4D5365", "#8389A0", "#323748", "#D2D5E0")),
        new("Podium gold", 3, Set("#0E0B07", "#1E1910", "#F2EDE3", "#B0A182", "#3B3221", "#F2B231", "#E09A0E", "#0A0A0A", "#635C50", "#9D9586", "#464035", "#DEDBD3")),
        new("Carbon", 5, Set("#0A0A0A", "#171717", "#EBEBEB", "#999999", "#2E2E2E", "#FF512E", "#F72900", "#FFFFFF", "#595959", "#919191", "#3D3D3D", "#D9D9D9")),
        new("Sunset strip", 7, Set("#0E0807", "#1E1310", "#F2E7E3", "#B08E82", "#3B2721", "#FA792E", "#EC5B06", "#FFFFFF", "#635550", "#9D8C86", "#463935", "#DED6D3")),
        new("Glacier", 9, Set("#070B0D", "#11191D", "#E4EDF1", "#82A1B0", "#233239", "#79D9EC", "#41C9E4", "#0A0A0A", "#515C61", "#87959B", "#364045", "#D4DADD")),
        new("Rally dust", 12, Set("#0D0A08", "#1C1712", "#F0EBE6", "#B09982", "#372E25", "#DD873C", "#C46E23", "#FFFFFF", "#605953", "#9A9189", "#433D37", "#DDD9D5")),
        new("Electric violet", 15, Set("#0A060E", "#160F1F", "#E9E2F3", "#9582B0", "#2B1F3D", "#B264F7", "#9529F4", "#FFFFFF", "#574F64", "#8F849F", "#3C3347", "#D8D3DF")),
        new("Racing green", 18, Set("#060E09", "#0F1F14", "#E2F3E8", "#82B091", "#1F3D29", "#EEBD2B", "#D5A411", "#0A0A0A", "#4F6456", "#849F8D", "#33473A", "#D3DFD7")),
        new("Endurance blue", 21, Set("#05090F", "#0D1521", "#E2E9F3", "#8295B0", "#1C2B40", "#1F8CF9", "#0673E0", "#FFFFFF", "#4C5767", "#818FA2", "#313B49", "#D1D7E0")),
        new("Neon nights", 25, Set("#0D060F", "#1D0E20", "#F0E2F3", "#A882B0", "#391D3E", "#FF33BB", "#FB00A7", "#FFFFFF", "#614D65", "#9B83A0", "#453248", "#DDD2E0")),
        new("Ice white", 30, Set("#F1F2F4", "#FEFEFE", "#22262A", "#656B72", "#D5D9DD", "#259DF4", "#259DF4", "#FFFFFF", "#A5ADB6", "#67737E", "#C1C7CD", "#22262A", heading: "#0E0F11", good: "#1F9E5A", warn: "#C98A0B", danger: "#D1343A", fastest: "#8A3FE0", lapping: "#D9534F", lapped: "#2F7DE1")),
        new("Tangerine", 35, Set("#0D0A07", "#1D1611", "#F1EAE4", "#B09582", "#392C23", "#FF851A", "#E66B00", "#FFFFFF", "#615851", "#9B9087", "#453C36", "#DDD8D4")),
        new("Deep sea", 40, Set("#050E10", "#0C1F22", "#E2F0F3", "#82A8B0", "#1A3B42", "#12E2D1", "#0FBAAB", "#FFFFFF", "#4B6368", "#7F9DA3", "#30464B", "#D0DEE1")),
        new("Rosso", 45, Set("#0E0607", "#1F0F10", "#F3E2E4", "#B08286", "#3D1F22", "#EC1325", "#C1101E", "#FFFFFF", "#644F50", "#9F8486", "#473335", "#DFD3D4")),
        new("Grey day", 50, Set("#090A0B", "#151719", "#E9EAEC", "#9298A0", "#2B2D31", "#B8BEC7", "#919BA9", "#0A0A0A", "#57595B", "#8F9194", "#3B3D3F", "#D8D9DA")),
        new("Lime flag", 60, Set("#0B0D07", "#181D11", "#ECF1E4", "#9DB082", "#303923", "#AFF425", "#96DB0C", "#0A0A0A", "#5B6151", "#939B87", "#3E4536", "#DADDD4")),
        new("Midnight teal", 70, Set("#060F0F", "#0E2020", "#E2F3F3", "#82B0B0", "#1D3E3E", "#13ECC8", "#10C1A4", "#0A0A0A", "#4D6565", "#83A0A0", "#324848", "#D2E0E0")),
        new("Desert night", 80, Set("#0D0B07", "#1D1811", "#F1ECE4", "#B09D82", "#393023", "#E85E30", "#CF4517", "#FFFFFF", "#615B51", "#9B9387", "#453E36", "#DDDAD4")),
        new("Aurora", 90, Set("#060E0C", "#0F1F1A", "#E2F3ED", "#82B0A1", "#1F3D33", "#BE5EED", "#A829E7", "#FFFFFF", "#4F645D", "#849F96", "#334740", "#D3DFDB")),
        new("Paddock white", 100, Set("#F1F2F4", "#FEFEFE", "#22252B", "#636973", "#D4D7DD", "#D81878", "#D81878", "#FFFFFF", "#A4AAB7", "#656E81", "#C0C5CE", "#22252B", heading: "#0D0F11", good: "#1F9E5A", warn: "#C98A0B", danger: "#D1343A", fastest: "#8A3FE0", lapping: "#D9534F", lapped: "#2F7DE1")),
        new("Crimson carbon", 115, Set("#0B090A", "#191516", "#EDE9E9", "#A29093", "#322A2B", "#E90C31", "#BF0A28", "#FFFFFF", "#5C5757", "#958E8F", "#403B3C", "#DAD7D8")),
        new("Sapphire", 130, Set("#050710", "#0C1122", "#E2E7F3", "#828EB0", "#1A2442", "#5A74F6", "#2144F3", "#FFFFFF", "#4B5268", "#7F88A3", "#30364B", "#D0D5E1")),
        new("Champagne", 150, Set("#F5F4F0", "#FEFEFE", "#2E2A1F", "#78725E", "#E0DDD1", "#DFCB90", "#DFCB90", "#FFFFFF", "#BEB69D", "#8A7E5C", "#D2CDBC", "#2E2A1F", heading: "#12110C", good: "#1F9E5A", warn: "#C98A0B", danger: "#D1343A", fastest: "#8A3FE0", lapping: "#D9534F", lapped: "#2F7DE1")),
        new("Black gold", 175, Set("#0B0B09", "#191815", "#ECEBE9", "#A09D92", "#312F2B", "#EEBD2B", "#D5A411", "#0A0A0A", "#5B5A57", "#94938F", "#3F3E3B", "#DAD9D8")),
        new("Hall of fame", 200, Set("#0B080C", "#18131B", "#ECE7EF", "#9F87AB", "#302735", "#FAC938", "#F5B906", "#0A0A0A", "#5B545F", "#948B98", "#3F3842", "#DAD6DC")),
    };

    public static bool Unlocked(HudColorSet set, int reached) => reached >= set.Goals;

    public static int UnlockedCount(int reached) => All.Count(s => Unlocked(s, reached));

    public static HudColorSet? Next(int reached) => All.FirstOrDefault(s => !Unlocked(s, reached));

    public static void Apply(HudColorSet set, HudCustomColors into)
    {
        var from = set.Colors.All().ToDictionary(e => e.Id, e => e.Get());
        foreach (var (id, _, put) in into.All()) put(from[id]);
    }

    public static bool Matches(HudColorSet set, HudCustomColors custom)
    {
        var from = set.Colors.All().ToDictionary(e => e.Id, e => e.Get());
        return custom.All().All(e => string.Equals(e.Get(), from[e.Id], StringComparison.OrdinalIgnoreCase));
    }
}

