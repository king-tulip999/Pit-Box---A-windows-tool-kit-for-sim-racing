using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Config;
using Core.Controls;
using Core.Hotkeys;
using Core.Plugins;
using Core.Plugins.BuiltIn;
using Core.Services;
using Core.SimRacing.Hud;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;
using Core.Theme;

namespace Core.Views.Pages;

internal interface IHudEditorHost
{
    PreviewSource Preview { get; }
    string ShownLayout { get; }
    void Changed();
    Task<bool> Confirm(string title, string message, string confirmText, bool danger);
    void ShowSettings();
}

internal sealed class RaceHudSection : StackPanel, IScreenshotStates, ICancelEdit, IHudEditorHost, IStagedBuild
{
    private static readonly string[] Tabs = { "Widgets", "Alerts", "Fuel", "Overlay settings" };
    private const int WidgetsTab = 0, AlertsTabIndex = 1, FuelTabIndex = 2, SettingsTab = 3;

    private readonly AppServices _app;
    private readonly IModalHost _window;
    private readonly Border _body = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TextBlock _status = TextFit.TipWhenTrimmed(new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, MaxWidth = 300 });
    private readonly StackPanel _titleTools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _layoutSlot = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private Border _statusChip = new();
    private (string, string, string) _statusShown;
    private Border _segments = new();
    private int _tab;
    private bool _shown;
    private RaceHudWidgets? _widgets;
    private Action? _tick;

    private PreviewSource? _preview;
    private int _steps;

    public PreviewSource Preview => _preview ??= PreviewSource.Take(() => S);

    public RaceHudSection(AppServices app, IModalHost window)
    {
        _app = app;
        _window = window;
        _timer.Tick += (_, _) => { Preview.Advance(Host); RefreshStatus(); _tick?.Invoke(); };

        _status.Style = Ui.Res<Style>("Caption");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(0, 0, 10, 0);
        _titleTools.Children.Add(_status);
        _titleTools.Children.Add(_statusChip);
        _arrange.Margin = new Thickness(Ui.GroupGap, 0, 0, 0);
        _titleTools.Children.Add(_arrange);
        var power = Ui.Switch(S.Enabled, on => Host?.SetEnabled(on));
        var powerTitle = ToggleCards.RowTitle("Show the overlay");
        Ui.AddInfo(powerTitle, "Shows over borderless and windowed iRacing, not exclusive fullscreen. Nothing is injected into the sim. When each widget shows is set per widget and under Overlay settings.");
        ToggleCards.Row(power, powerTitle, null);
        power.Margin = new Thickness(Ui.GroupGap, 0, 0, 0);
        power.VerticalAlignment = VerticalAlignment.Center;
        power.HorizontalAlignment = HorizontalAlignment.Left;
        _titleTools.Children.Add(power);
        System.Windows.Automation.AutomationProperties.SetName(power, "iRacing Overlay");

        _segments = Ui.PageTabs(Tabs, 0, i => { _tab = i; Build(); Motion.FadeIn(_body); }, "hudtabs");
        _segments.HorizontalAlignment = HorizontalAlignment.Left;
        _segments.VerticalAlignment = VerticalAlignment.Center;
        var tabRow = Ui.SplitRow(_segments, _layoutSlot, 19);
        tabRow.Margin = Ui.TabRowMargin;
        Children.Add(tabRow);
        Children.Add(_body);
        Ui.Anchor(this, "simracing.hud");
        RefreshStatus();
        RefreshArrange();
    }

    public FrameworkElement TitleTools => _titleTools;

    private bool _warmed;

    public bool Done => _steps >= 2 && _warmed;

    public bool BuildStep()
    {
        if (_steps == 0)
        {
            _ = Preview;
            _steps = 1;
            return true;
        }
        if (_steps == 1)
        {
            Build();
            return true;
        }
        if (_warmed) return false;
        _warmed = true;
        _widgets?.WarmPreview();
        return true;
    }

    private void Finish()
    {
        while (BuildStep()) { }
    }

    private Tile? _rulesCard;

    private readonly StackPanel _arrange = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private Button? _editLayout;

    private void RefreshArrange()
    {
        _arrange.Children.Clear();
        var h = Host;
        if (h?.Editing == true)
        {
            var cancel = Ui.LabeledButton("I.Undo", "Cancel", () => { Host?.EndEdit(keep: false); RefreshArrange(); });
            cancel.ToolTip = "Put every widget back where it was";
            cancel.Margin = Ui.AfterButton;
            _arrange.Children.Add(cancel);
            _arrange.Children.Add(Ui.LabeledButton("I.Check", "Done arranging", () => { Host?.EndEdit(keep: true); RefreshArrange(); }, "PrimaryButton"));
            return;
        }
        bool simulating = h?.Preview == true;
        var simulate = Ui.LabeledButton("I.Play", simulating ? "Stop simulating" : "Simulate data", () => { Host?.SetPreview(!(Host?.Preview ?? false)); RefreshStatus(); RefreshArrange(); },
            simulating ? "SelectedButton" : "GhostButton");
        simulate.IsEnabled = !_app.Demo && h is not null;
        simulate.ToolTip = _app.Demo ? "Not with demo data (no widgets are put on screen)"
            : "Puts the widgets on screen with a made-up race (marked SIMULATED), to try layouts without iRacing (not while a session runs). Not saved.";
        ToolTipService.SetShowOnDisabled(simulate, true);
        simulate.Margin = Ui.AfterButton;
        _arrange.Children.Add(simulate);
        _editLayout = Ui.LabeledButton("I.Pencil", "Edit layout", () => { if (Host is { } hud) HudQuickEditor.Toggle(_app, hud); });
        _editLayout.Margin = Ui.AfterButton;
        ToolTipService.SetShowOnDisabled(_editLayout, true);
        SyncEditLayout();
        _arrange.Children.Add(_editLayout);
        var arrange = Ui.LabeledButton("I.Move", "Arrange on screen", () => { Host?.BeginEdit(); RefreshArrange(); });
        arrange.IsEnabled = !_app.Demo && h is not null;
        arrange.ToolTip = _app.Demo ? "Not with demo data (no widgets are put on screen)"
            : "Drag widgets to move them, drag a corner to scale them (Alt = no snapping). The bar at the top of the screen has Undo, Snap and Done.";
        ToolTipService.SetShowOnDisabled(arrange, true);
        _arrange.Children.Add(arrange);
    }

    private void SyncEditLayout()
    {
        if (_editLayout is not { } b) return;
        bool on = !_app.Demo && Host?.OnScreen == true;
        if (b.IsEnabled != on) b.IsEnabled = on;
        var shown = on ? Visibility.Visible : Visibility.Collapsed;
        if (b.Visibility != shown) b.Visibility = shown;
        string key = S.QuickEditHotkey is { Length: > 0 } k ? $" ({Hotkey.Display(k)})" : "";
        b.ToolTip = _app.Demo ? "Not with demo data (no widgets are put on screen)"
            : on ? $"Opens the overlay editor over the game: the widgets on screen, their settings, undo, snap{key}"
            : $"Opens once the overlay is on screen: in a session, or with Simulate data{key}";
    }

    private RaceHudSettings S => _app.Config.SimRacing.Hud;

    private HudHost? _watched;
    private HudHost? _changedHost;

    private HudHost? Host => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;

    public void OnShown()
    {
        Finish();
        if (_shown) return;
        _shown = true;
        if (Host is { } h)
        {
            h.Changed += OnHostChanged;
            _changedHost = h;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_shown || _watched is not null) return;
                h.Watch(true);
                _watched = h;
            }, DispatcherPriority.Background);
        }
        _timer.Start();
        RefreshStatus();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        _rules?.SetEditing(false);
        if (_changedHost is { } h) h.Changed -= OnHostChanged;
        _changedHost = null;
        _watched?.Watch(false);
        _watched = null;
        _timer.Stop();
    }

    private void OnHostChanged()
    {
        RefreshStatus();
        RefreshArrange();
        if (_tab == WidgetsTab)
        {
            if (LayoutsSignature() != _builtLayouts)
                Dispatcher.BeginInvoke(() => { if (_tab == WidgetsTab && LayoutsSignature() != _builtLayouts) Build(); }, DispatcherPriority.Background);
            else SyncLayoutPick();
        }
    }

    private string _builtLayouts = "";
    private ComboBox? _layoutPick;

    private string LayoutsSignature() => string.Join("|", S.Layouts.Select(l => l.Name));

    public Task<bool> Confirm(string title, string message, string confirmText, bool danger) => _window.Confirm(title, message, confirmText, danger);

    public void ShowSettings()
    {
        SelectTab(SettingsTab);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Ui.FindAnchor(_body, WebAnchor) is not { } web) return;
            web.UpdateLayout();
            web.BringIntoView(new Rect(0, 0, web.ActualWidth, web.ActualHeight));
            HighlightAdorner.Flash(web);
        }), DispatcherPriority.ContextIdle);
    }

    public void PickWidget(string id)
    {
        Finish();
        if (_tab != WidgetsTab) SelectTab(WidgetsTab);
        _widgets?.SelectId(id);
    }

    public string ShownLayout => Host?.LayoutName is { Length: > 0 } n ? n : S.ActiveLayout;

    private void SyncLayoutPick()
    {
        if (_layoutPick is null) return;
        var want = _layoutPick.Items.Cast<Choice>().FirstOrDefault(c => c.Id == ShownLayout);
        if (want is not null && !Equals(_layoutPick.SelectedItem, want)) _layoutPick.SelectedItem = want;
        _widgets?.FollowLayout();
    }

    public void Changed()
    {
        if (Host is { } h) { h.SettingsChanged(); return; }
        bool renamed = HudPresets.Fork(S) is not null;
        _app.Save();
        if (renamed && _tab == WidgetsTab) Dispatcher.BeginInvoke(Build, DispatcherPriority.Background);
    }

    private void RefreshStatus()
    {
        SyncEditLayout();
        var t = _app.Telemetry;
        var h = Host;
        var (text, soft, deep) = !S.Enabled ? ("Off", "DangerSoft", "Danger")
            : t.Simulated && _app.Demo ? ("Simulated (demo)", "PlumSoft", "PlumDeep")
            : t.Status switch
            {
                SimStatus.Connected => (h?.Preview == true ? "Preview" : "iRacing connected", "SageSoft", "SageDeep"),
                SimStatus.Waiting => ("iRacing loading", "WarnSoft", "WarnDeep"),
                _ => ("Waiting for iRacing", "SlateSoft", "SlateDeep"),
            };
        if (_statusChip.Parent is Panel parent && (text, soft, deep) != _statusShown)
        {
            var chip = Ui.Chip(text, soft, deep);
            chip.BorderThickness = new Thickness(0);
            chip.Margin = new Thickness(0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            int i = parent.Children.IndexOf(_statusChip);
            parent.Children.RemoveAt(i);
            parent.Children.Insert(i, chip);
            _statusChip = chip;
            _statusShown = (text, soft, deep);
        }
        string why = h?.MatchedRule is { } rule ? $"Layout picked by a rule: {rule.Describe()} · " : "";
        string state = h?.OnScreen == true ? (why.Length == 0 ? "On screen" : "on screen") : ShowWhenText(why.Length == 0);
        _status.Text = _app.Demo || !S.Enabled ? ""
            : why + state + (h?.Editing == true ? " · arranging" : "");
        _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ShowWhenText(bool start) => S.ShowWhen switch
    {
        HudShowWhen.Always => start ? "Shown always" : "shown always",
        HudShowWhen.Connected => start ? "Shows when a session is loaded" : "shows when a session is loaded",
        _ => start ? "Shows when you're in the car" : "shows when you're in the car",
    };

    private void Build()
    {
        var keep = _widgets?.Keep();
        _steps = 2;
        _tick = null;
        _rules = null;
        _ruleCount = null;
        _alertGroups = null;
        _widgets = null;
        _layoutPick = null;
        _layoutSlot.Content = null;
        _body.Child = _tab switch
        {
            AlertsTabIndex => AlertsTab(),
            FuelTabIndex => FuelTab(),
            SettingsTab => HudSettingsTab(),
            _ => WidgetsTabView(),
        };
        if (_tab == AlertsTabIndex) Ui.Anchor((FrameworkElement)_body.Child, AlertsAnchor);
        else if (_tab == SettingsTab) Ui.Anchor((FrameworkElement)_body.Child, SettingsAnchor);
        if (_widgets is not null)
        {
            if (keep is { } kept) _widgets.Restore(kept);
            _tick = _widgets.Tick;
        }
    }

    private static Tile Group(string title, string? caption, params UIElement[] rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title, "H3"));
        if (caption is not null) stack.Children.Add(Caption(caption, 2));
        var body = new SettingsFlow { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var r in rows) body.Children.Add(r);
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = stack };
    }

    private static TextBlock Caption(string text, double top = 6) => new()
    {
        Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
    };

    private StackPanel WidgetsTabView()
    {
        var root = new StackPanel();

        var pick = _layoutPick = Ui.Combo(S.Layouts.Select(l => new Choice(l.Name, l.Name)), ShownLayout, c =>
        {
            if (c.Id == ShownLayout) return;
            Host?.UseLayout(c.Id!);
            if (Host is null) { S.ActiveLayout = c.Id!; _app.Save(); }
            RefreshStatus();
            _widgets?.FollowLayout();
        }, 160);
        _builtLayouts = LayoutsSignature();
        var add = Ui.LabeledButton("I.Plus", "New layout", () => { }, "GhostButton");
        add.Click += (_, _) => NewLayoutBox(add, S, S.Layout(ShownLayout), AddLayout);
        var dup = Ui.IconButton("I.Copy", "Duplicate this layout", () =>
        {
            var from = S.Layout(ShownLayout);
            if (from is null) return;
            var copy = from.Copy(S.UniqueName(from.Name + " copy"));
            foreach (var w in copy.Widgets) w.Id = HudWidgetConfig.NewId(w.Kind);
            AddLayout(copy);
        });
        var rename = Ui.IconButton("I.Pencil", "Rename or reset this layout", EditLayout);
        var delete = Ui.IconButton("I.Trash", "Delete this layout", async () =>
        {
            string name = ShownLayout;
            if (S.Layouts.Count <= 1 || !await _window.Confirm($"Delete “{name}”?", "Its widgets and the rules that pick it are removed.", "Delete", danger: true)) return;
            S.DeleteLayout(name);
            Changed();
            _app.Hotkeys.RegisterAll();
            Build();
        });
        bool isDefault = RaceHudSettings.IsDefaultName(ShownLayout);
        delete.IsEnabled = S.Layouts.Count > 1 && !isDefault;
        rename.IsEnabled = !isDefault;
        if (isDefault)
        {
            rename.ToolTip = delete.ToolTip = "The default layout stays as it is; a change to it goes into a new Custom layout";
            ToolTipService.SetShowOnDisabled(rename, true);
            ToolTipService.SetShowOnDisabled(delete, true);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var layoutLabel = Ui.Text("Layout", "H3");
        layoutLabel.VerticalAlignment = VerticalAlignment.Center;
        layoutLabel.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(layoutLabel);
        row.Children.Add(pick);
        foreach (var b in new[] { rename, dup, delete }) { b.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0); row.Children.Add(b); }
        add.Margin = new Thickness(Ui.ButtonGap, 0, 0, 0);
        row.Children.Add(add);

        var rules = new StackPanel();
        var addRule = Ui.LabeledButton("I.Plus", "Add rule", () =>
        {
            S.Rules.Add(new HudLayoutRule { Layout = ShownLayout, Session = "Race" });
            Changed();
            _rules?.Fill();
            _rulesEmpty!.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshRuleCount();
        }, "SoftButton");
        _rules = new ArrangeList(
            () => S.Rules.Select(r => (r.Id, r.Describe(), (Func<FrameworkElement>)(() => RuleRow(r)))).ToList(),
            () => S.Rules.Select(r => r.Id).ToList(),
            order =>
            {
                S.Rules = order.Select(id => S.Rules.First(r => r.Id == id)).ToList();
                Changed();
                RefreshStatus();
            },
            "Arrange", "Drag the rules into order: when two fit a session equally, the higher one wins");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _rules.Button.Margin = Ui.AfterButton;
        actions.Children.Add(_rules.Button);
        actions.Children.Add(addRule);
        _ruleCount = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var useRules = Ui.Switch(S.AutoSwitch, on =>
        {
            S.AutoSwitch = on;
            Changed();
            RefreshStatus();
            if (_rulesCard is not null) _rulesCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            RefreshRuleCount();
        });
        System.Windows.Automation.AutomationProperties.SetName(useRules, "Switch layouts automatically");
        var switchLabel = ToggleCards.RowTitle("Switch layouts automatically");
        Ui.AddInfo(switchLabel, "Rules pick a layout by session, class, car and track. The most specific matching rule wins, then the higher one; with none, the layout picked on the left shows.");
        switchLabel.TextWrapping = TextWrapping.NoWrap;
        ToggleCards.Row(useRules, switchLabel, null, trailing: _ruleCount);
        useRules.VerticalAlignment = VerticalAlignment.Center;
        useRules.HorizontalAlignment = HorizontalAlignment.Left;
        useRules.Margin = new Thickness(Ui.GroupGap, 0, 0, 0);
        row.Children.Add(useRules);
        _rules.Fill();
        _rulesEmpty = Caption("No rules yet: the layout picked above always shows.", 0);
        _rulesEmpty.VerticalAlignment = VerticalAlignment.Center;
        _rulesEmpty.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var ruleBar = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Right);
        ruleBar.Children.Add(actions);
        ruleBar.Children.Add(_rulesEmpty);
        rules.Children.Add(ruleBar);
        _rules.View.Margin = new Thickness(0, 6, 0, 0);
        rules.Children.Add(_rules.View);
        RefreshRuleCount();
        root.Children.Add(_rulesCard = new Tile
        {
            Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Padding = new Thickness(13, 10, 13, 10), Child = rules,
            Visibility = S.AutoSwitch ? Visibility.Visible : Visibility.Collapsed,
        });
        Ui.Anchor(row, LayoutsAnchor);
        _layoutSlot.Content = row;

        _widgets = new RaceHudWidgets(_app, this);
        root.Children.Add(_widgets.Root);
        return root;
    }

    private ArrangeList? _rules;
    private TextBlock? _rulesEmpty;
    private TextBlock? _ruleCount;

    private void RefreshRuleCount()
    {
        if (_ruleCount is null) return;
        _ruleCount.Text = S.Rules.Count == 0 ? "No rules" : RaceFormat.Count(S.Rules.Count, "rule");
        _ruleCount.Visibility = S.AutoSwitch || S.Rules.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement RuleRow(HudLayoutRule rule)
    {
        var f = Preview.Race.Frame;
        var me = f.Session.Player;
        var parts = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement Part(string label, ComboBox combo)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 5) };
            stack.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 0, 0, 2) });
            stack.Children.Add(combo);
            return stack;
        }
        ComboBox Pick(IEnumerable<string> known, string? value, string any, Action<string?> set, double width)
        {
            var names = known.ToList();
            if (value is not null && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Insert(0, value);
            var combo = new SearchCombo { MinWidth = width };
            foreach (var c in names.Select(n => new Choice(n, n)).Prepend(new Choice("", any))) combo.Items.Add(c);
            combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(c => string.Equals(c.Id, value ?? "", StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c) { set(c.Id is { Length: > 0 } s ? s : null); Changed(); RefreshStatus(); } };
            return combo;
        }
        parts.Children.Add(Part("Show layout", Ui.Combo(S.Layouts.Select(l => new Choice(l.Name, l.Name)), rule.Layout, c => { rule.Layout = c.Id!; Changed(); RefreshStatus(); }, 122)));
        parts.Children.Add(Part("In", Ui.Combo(new[]
        {
            new Choice("", "Every session"), new Choice("Practice", "Practice"), new Choice("Qualify", "Qualifying"), new Choice("Race", "Race"),
            new Choice("TimeTrial", "Time trials"),
        }, rule.Session ?? "", c => { rule.Session = c.Id is { Length: > 0 } s ? s : null; Changed(); RefreshStatus(); }, 113)));
        parts.Children.Add(Part("Class", Pick(SessionNames.Merge(rule.Class, S.KnownClasses.Append(me?.ClassName), SessionNames.Classes), rule.Class, "Any class", v => rule.Class = v, 150)));
        parts.Children.Add(Part("Car", Pick(SessionNames.Merge(rule.Car, S.KnownCars.Append(me?.CarName), Core.SimRacing.Setups.SetupCars.BuiltInNames), rule.Car, "Any car", v => rule.Car = v, 230)));
        parts.Children.Add(Part("Track", Pick(SessionNames.Merge(rule.Track, S.KnownTracks.Append(f.Session.TrackDisplayName), SessionNames.Tracks), rule.Track, "Any track", v => rule.Track = v, 250)));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(parts);
        var remove = Ui.IconButton("I.Trash", "Remove this rule", () =>
        {
            int at = S.Rules.IndexOf(rule);
            void Refresh()
            {
                Changed();
                RefreshStatus();
                _rules?.Fill();
                if (_rulesEmpty is not null) _rulesEmpty.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshRuleCount();
            }
            S.Rules.Remove(rule);
            Refresh();
            _app.Notify("Rule removed", "", action: new NoticeAction("Undo", () =>
            {
                if (S.Rules.Contains(rule)) return;
                S.Rules.Insert(Math.Clamp(at, 0, S.Rules.Count), rule);
                Refresh();
            }));
        }, "QuietIconButton");
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);
        var tile = Hairline.Frame(new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(10, 6, 6, 3), Child = grid });
        tile.SetResourceReference(Border.BorderBrushProperty, "Line");
        return tile;
    }

    internal static void NewLayoutBox(Button anchor, RaceHudSettings settings, HudLayout? current, Action<HudLayout> add)
    {
        var name = new TextBox { Text = settings.UniqueName(current is null ? "Layout" : current.Name + " copy"), Width = 211, MaxLength = Ui.NameLength };
        var clean = new CheckBox { Content = "Clean screen", IsChecked = current is null, IsEnabled = current is not null, Margin = new Thickness(0, 10, 0, 0) };
        var cleanNote = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        ToggleCards.Row(clean, ToggleCards.RowTitle("Clean screen"), null, cleanNote);
        clean.MaxWidth = 243;
        clean.HorizontalAlignment = HorizontalAlignment.Left;
        void Note()
        {
            cleanNote.Text = clean.IsChecked == true ? "Starts empty: add the widgets you want." : $"Starts as a copy of “{current?.Name}”.";
            if (clean.IsChecked == true && name.Text == settings.UniqueName(current?.Name + " copy")) name.Text = settings.UniqueName("Layout");
            else if (clean.IsChecked != true && name.Text == settings.UniqueName("Layout") && current is not null) name.Text = settings.UniqueName(current.Name + " copy");
        }
        Note();
        clean.Click += (_, _) => Note();
        var error = new TextBlock { Foreground = Ui.Res<Brush>("DangerDeep"), TextWrapping = TextWrapping.Wrap, MaxWidth = 243, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "New layout", Style = Ui.Res<Style>("H3"), Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TextBlock { Text = "Name", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(name);
        stack.Children.Add(error);
        stack.Children.Add(clean);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 13, 0, 0) };
        var cancel = Ui.LabeledButton("I.X", "Cancel", () => { }, "GhostButton");
        var save = Ui.LabeledButton("I.Check", "Save", () => { }, "PrimaryButton");
        save.Margin = new Thickness(6, 0, 0, 0);
        save.IsDefault = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);

        var box = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(15, 11, 15, 13), Margin = new Thickness(0, 5, 0, 0), Child = stack },
        };
        RenderOptions.SetClearTypeHint(box.Child, ClearTypeHint.Enabled);
        Ui.DropBelow(box);
        Ui.FollowTarget(box);
        void Save()
        {
            string wanted = name.Text.Trim();
            if (wanted.Length == 0 || settings.Layout(wanted) is not null)
            {
                error.Text = wanted.Length == 0 ? "Give it a name." : "A layout with that name already exists.";
                error.Visibility = Visibility.Visible;
                return;
            }
            HudLayout layout;
            if (clean.IsChecked == true || current is null) layout = new HudLayout { Name = wanted };
            else
            {
                layout = current.Copy(wanted);
                foreach (var w in layout.Widgets) w.Id = HudWidgetConfig.NewId(w.Kind);
            }
            box.IsOpen = false;
            add(layout);
        }
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => box.IsOpen = false;
        name.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { Save(); e.Handled = true; }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { box.IsOpen = false; e.Handled = true; }
        };
        box.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        WindowScale.Follow(box);
        box.IsOpen = true;
    }

    private void AddLayout(HudLayout layout)
    {
        S.Layouts.Add(layout);
        S.ActiveLayout = layout.Name;
        Host?.UseLayout(layout.Name);
        Changed();
        Build();
    }

    private void EditLayout()
    {
        string old = ShownLayout;
        var box = new TextBox { Text = old, Width = 259, MaxLength = Ui.NameLength };
        var error = new TextBlock();
        var buttons = Ui.DialogButtons(_window, "Save", () =>
        {
            if (!S.RenameLayout(old, box.Text)) { error.Text = "That name is empty, already used or the default's."; return false; }
            Changed();
            Build();
            return true;
        });
        var reset = Ui.LabeledButton("I.Undo", "Reset to default", async () =>
        {
            _window.CloseModal(false);
            if (!await _window.Confirm($"Reset “{old}” to the default layout?", "Its widgets, their places and settings go back to how the overlay starts. Duplicate it first to keep this version.", "Reset", danger: true)) return;
            if (Host is { } h) h.ResetLayout(old);
            else { S.ResetToDefault(old, HudPresets.DefaultLayouts()[0]); _app.Save(); }
            _app.Hotkeys.RegisterAll();
            Build();
        });
        reset.IsEnabled = S.Layout(old) is { } shown && !HudPresets.IsDefault(shown);
        reset.ToolTip = reset.IsEnabled ? "This layout back to the overlay's default widgets and places" : "This layout is the default already";
        ToolTipService.SetShowOnDisabled(reset, true);
        _ = _window.ShowModal(Ui.Dialog(_window, "Edit layout", "Rules that pick it follow a new name.", Ui.FieldRow("Name", box), buttons, error, 430, reset));
        box.Focus();
        box.SelectAll();
    }

    private StackPanel HudSettingsTab()
    {
        var root = HudSettings(_app, Changed, Build, RefreshStatus);
        var web = BrowserSources(_app, Changed);
        Ui.Anchor(web, WebAnchor);
        root.Children.Insert(Math.Min(1, root.Children.Count), web);
        return root;
    }

    internal static Tile BrowserSources(AppServices app, Action changed)
    {
        var S = app.Config.SimRacing.Hud;
        HudHost? Host() => app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;
        var port = new NumberBox(S.WebPort, "", 75) { Minimum = RaceHudSettings.MinWebPort, Maximum = RaceHudSettings.MaxWebPort, Step = 1 };
        System.Windows.Automation.AutomationProperties.SetName(port, "Port");
        var open = Ui.LabeledButton("I.External", "Open the overlay list", () => Ui.OpenUrl(SimRacing.Hud.Web.HudWebServer.Url(S.WebPort)), "GhostButton");
        ToolTipService.SetShowOnDisabled(open, true);
        var problem = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        problem.SetResourceReference(TextBlock.ForegroundProperty, "WarnDeep");
        void Refresh()
        {
            bool running = Host()?.WebPort is not null;
            open.IsEnabled = running;
            open.ToolTip = running ? "Every widget's page, with live previews" : S.WebOverlays ? "Not running: see the note above" : "Turn on Browser sources first";
            problem.Text = Host()?.WebProblem ?? "";
            problem.Visibility = problem.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void Apply()
        {
            changed();
            Host()?.SyncWeb();
            Refresh();
        }
        var toggle = Ui.Switch(S.WebOverlays, on => { S.WebOverlays = on; Apply(); });
        port.ValueChanged += v => { S.WebPort = (int)v; Apply(); };
        Refresh();
        return Group("Browser sources", null,
            Ui.SettingRow("Browser sources", "Every widget as a page on this PC only, for a wheel screen or streaming software. Each widget's URL is on its page.", toggle),
            problem,
            Ui.SettingRow("Port", null, port),
            Ui.SettingRow("Overlay list", null, open));
    }

    internal static StackPanel HudSettings(AppServices app, Action changed, Action rebuild, Action? statusChanged)
    {
        var S = app.Config.SimRacing.Hud;
        HudHost? Host() => app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;
        var a = S.Appearance;
        var root = new StackPanel();
        var when = Ui.Combo(new[]
        {
            new Choice(nameof(HudShowWhen.Driving), "In the car"),
            new Choice(nameof(HudShowWhen.Connected), "Whenever a session is loaded"),
            new Choice(nameof(HudShowWhen.Always), "Always"),
        }, S.ShowWhen.ToString(), c => { S.ShowWhen = Enum.Parse<HudShowWhen>(c.Id!); changed(); statusChanged?.Invoke(); }, 194);
        var stdLook = new HudAppearance();
        Ui.SettingDefault D(Func<bool> same, Action reset, string shown) => new(same, () => { reset(); changed(); rebuild(); }, shown);
        root.Children.Add(Group("Overlay", null,
            Ui.SettingRow("Show the widgets", null, when),
            Ui.SettingRow("Click-through", "Clicks go to the game. Off: click a driver in the Relative or Standings to highlight them in both (the window never takes the keyboard).",
                Ui.Switch(!S.Interactive, on => { S.Interactive = !on; changed(); })),
            Ui.SettingRow("Units", null, Ui.Segmented(new[] { "Metric", "Imperial" }, (int)S.Units, i => { S.Units = (HudUnits)i; changed(); }, "hudunits"),
                D(() => S.Units == HudUnits.Metric, () => S.Units = HudUnits.Metric, "Metric"))));
        var keys = new KeybindTree(app, e => e.Id.StartsWith("hud.", StringComparison.Ordinal), skip: 2, label: ShortKeybindLabel, flat: true) { Style = null, Padding = new Thickness(0), Margin = new Thickness(-5, 5, 0, 0) };
        root.Children.Add(new Tile
        {
            Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing),
            Child = new StackPanel { Children = { Ui.Text("Keybinds", "H3"), keys } },
        });
        root.Children.Add(Group("Look", null, LookRows(a, changed, rebuild, (t, d, c, f) => Ui.SettingRow(t, d, c, f is null ? null : D(f.Value.Same, f.Value.Reset, f.Value.Shown))).Concat(new UIElement[] {
            Ui.SettingRow("Background", null, Ui.SliderWithValue(0, 100, a.Background, "{0:0}%", v => { a.Background = (int)v; changed(); }),
                D(() => a.Background == stdLook.Background, () => a.Background = stdLook.Background, stdLook.Background + "%")),
            Ui.SettingRow("Corners", null, Ui.SliderWithValue(0, 16, a.Corner, "{0:0} px", v => { a.Corner = (int)v; changed(); }),
                D(() => a.Corner == stdLook.Corner, () => a.Corner = stdLook.Corner, stdLook.Corner + " px")),
            Ui.SettingRow("Outline", null, Ui.Switch(a.Outline, on => { a.Outline = on; changed(); }),
                D(() => a.Outline == stdLook.Outline, () => a.Outline = stdLook.Outline, stdLook.Outline ? "On" : "Off")) }).ToArray()));
        var grid = Ui.SliderWithValue(RaceHudSettings.MinGrid, RaceHudSettings.MaxGrid, S.GridSize, "{0:0} px", v =>
        {
            S.GridSize = (int)v;
            changed();
            Host()?.ShowGrid();
        }, step: RaceHudSettings.GridStep);
        root.Children.Add(Group("Arranging on screen", null,
            Ui.SettingRow("Snap while arranging", "Edges line up with other widgets and the screen, else a grid. Hold Alt to place freely.", Ui.Switch(S.Snap, on => { if (Host() is { } host) host.SetSnap(on); else S.Snap = on; changed(); })),
            Ui.SettingRow("Show the grid while arranging", "Snapping works with the grid hidden.", Ui.Switch(S.GridShown, on => { if (Host() is { } host) host.SetGridShown(on); else S.GridShown = on; changed(); })),
            Ui.SettingRow("Grid", app.Demo ? "The grid is shown over your screens while you adjust it (not with demo data)." : "Shown over your screens while you adjust it.", grid)));

        return root;
    }

    private const string PresetColors = "Preset";

    internal static IEnumerable<UIElement> LookRows(HudAppearance a, Action changed, Action rebuild, Func<string, string?, FrameworkElement, (Func<bool> Same, Action Reset, string Shown)?, UIElement> row)
    {
        bool preset = HudThemes.Known(a.Colors) is not null;
        string mode = preset ? PresetColors : a.Colors;
        var modes = new[]
        {
            new Choice(HudAppearance.DefaultColors, "Default"),
            new Choice(HudAppearance.AppColors, "Same as the app theme"),
            new Choice(PresetColors, "Theme"),
            new Choice(HudAppearance.CustomColors, "Custom"),
        };
        var pick = Ui.Combo(modes, mode, c =>
        {
            if (c.Id == PresetColors) a.Colors = HudThemes.Known(a.Colors) ?? HudThemes.Presets.First().Name;
            else if (c.Id == HudAppearance.CustomColors)
            {
                if (a.Colors != HudAppearance.CustomColors) a.Custom = HudThemes.StartingColors(a);
                a.Colors = HudAppearance.CustomColors;
            }
            else a.Colors = c.Id!;
            changed();
            rebuild();
        }, 194);
        yield return row("Colors", a.Colors == HudAppearance.AppColors ? "Applies to every widget. Follows the app theme." : "Applies to every widget.", pick,
            (() => a.Colors == HudAppearance.DefaultColors, () => a.Colors = HudAppearance.DefaultColors, "Default"));
        if (preset)
        {
            var theme = new Button { Style = Ui.Res<Style>("MenuButton"), MinWidth = 194, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            void Face()
            {
                var name = TextFit.TipWhenTrimmed(new TextBlock { Text = a.Colors, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 0, 0) });
                theme.Content = Ui.MenuFace(name, ThemeMenu.PresetSpec(a.Colors) is { } spec ? ThemeMenu.Dots(spec) : null);
                System.Windows.Automation.AutomationProperties.SetName(theme, "Theme: " + a.Colors);
            }
            theme.Click += (_, _) => ThemeMenu.OpenPresetMenu(theme, a.Colors, name =>
            {
                a.Colors = name;
                Face();
                changed();
            });
            Face();
            yield return row("Theme", null, theme, null);
        }
        if (a.Colors != HudAppearance.CustomColors) yield break;
        var custom = a.Custom;
        foreach (var (label, get, set) in new (string, Func<string>, Action<string>)[]
        {
            ("Panel", () => custom.Background, v => custom.Background = v),
            ("Text", () => custom.Text, v => custom.Text = v),
            ("Edges", () => custom.Edges, v => custom.Edges = v),
            ("Accent", () => custom.Accent, v => custom.Accent = v),
            ("Good", () => custom.Success, v => custom.Success = v),
            ("Warning", () => custom.Warning, v => custom.Warning = v),
            ("Danger", () => custom.Error, v => custom.Error = v),
        })
            yield return row(label, null, HexField(get, v => { set(v); changed(); }), null);
    }

    private static StackPanel HexField(Func<string> get, Action<string> set)
    {
        var swatch = new Border { Width = 28, Height = 28, BorderThickness = Hairline.Edges(), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Pick a color" };
        swatch.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        swatch.MouseEnter += (_, _) => swatch.SetResourceReference(Border.BorderBrushProperty, "Accent");
        swatch.MouseLeave += (_, _) => swatch.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        System.Windows.Automation.AutomationProperties.SetName(swatch, "Pick a color");
        Motion.Pressable(swatch);
        var box = new TextBox { Width = 96, Text = get(), MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
        void Show() => swatch.Background = CustomPalette.Parse(get()) is { } c ? new SolidColorBrush(c) : null;
        void Commit()
        {
            if (CustomPalette.Parse(box.Text) is { } c)
            {
                string hex = Palettes.ToHex(c);
                if (!string.Equals(hex, get(), StringComparison.OrdinalIgnoreCase)) set(hex);
            }
            box.Text = get();
            Show();
        }
        swatch.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (PickColor(swatch, get()) is not { } picked) return;
            box.Text = picked;
            Commit();
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
        Show();
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, box } };
    }

    private sealed class DialogOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }

    private static string? PickColor(FrameworkElement near, string current)
    {
        if (!External.Allowed("color dialog")) return null;
        var start = CustomPalette.Parse(current);
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (start is { } c) dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        var owner = Window.GetWindow(near) is { } window ? new System.Windows.Interop.WindowInteropHelper(window).Handle : IntPtr.Zero;
        var result = owner == IntPtr.Zero ? dialog.ShowDialog() : dialog.ShowDialog(new DialogOwner(owner));
        if (result != System.Windows.Forms.DialogResult.OK) return null;
        var chosen = dialog.Color;
        return $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
    }

    internal static string ShortKeybindLabel(string label)
    {
        string text = label.Replace("the iRacing Overlay", "", StringComparison.Ordinal).Replace("iRacing Overlay", "", StringComparison.Ordinal);
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? label : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private Grid? _alertGroups;
    private int _alertColumns;
    private Action? _alertFoldChanged;

    private void FillAlertGroups()
    {
        if (_alertGroups is null) return;
        _alertGroups.Children.Clear();
        _alertGroups.ColumnDefinitions.Clear();
        var order = S.AlertGroupOrder;
        var groups = AlertCatalog.Groups.OrderBy(g => order.FindIndex(id => string.Equals(id, g.Id, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : int.MaxValue).ToList();
        var weights = groups.Select(g => S.AlertGroupsFolded.Contains(g.Id, StringComparer.OrdinalIgnoreCase) ? 1.0 : 1.0 + g.Alerts.Length).ToList();
        var placed = AlertColumnOf(weights, _alertColumns);
        int columns = placed.Count == 0 ? 1 : placed.Max() + 1;
        for (int c = 0; c < columns; c++)
        {
            _alertGroups.ColumnDefinitions.Add(new ColumnDefinition());
            var column = new StackPanel { Margin = new Thickness(0, -1, 0, 0) };
            for (int i = 0; i < groups.Count; i++) if (placed[i] == c) column.Children.Add(AlertGroup(groups[i]));
            var cell = new Border { Child = column, ClipToBounds = true };
            if (c > 0)
            {
                Hairline.Frame(cell, Sides.Left);
                cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            }
            Grid.SetColumn(cell, c);
            _alertGroups.Children.Add(cell);
        }
    }

    internal static List<int> AlertColumnOf(IReadOnlyList<double> weights, int columns)
    {
        var heights = new double[Math.Clamp(columns, 1, Math.Max(1, weights.Count))];
        var placed = new List<int>();
        foreach (var weight in weights)
        {
            int shortest = Array.IndexOf(heights, heights.Min());
            heights[shortest] += weight;
            placed.Add(shortest);
        }
        return placed;
    }

    private const double AlertColumnWidth = 560;

    private StackPanel AlertsTab()
    {
        var root = new StackPanel();
        _alertColumns = 1;
        _alertGroups = new Grid();
        _alertGroups.SizeChanged += (_, e) =>
        {
            int columns = Math.Clamp((int)(e.NewSize.Width / AlertColumnWidth), 1, 3);
            if (columns == _alertColumns) return;
            _alertColumns = columns;
            FillAlertGroups();
        };
        var foldAll = new CollapseAllButton { Margin = Ui.AfterButton };
        void CountOpen() => foldAll.SetOpen(AlertCatalog.Groups.Count(g => !S.AlertGroupsFolded.Contains(g.Id, StringComparer.OrdinalIgnoreCase)), AlertCatalog.Groups.Count());
        foldAll.Requested += fold =>
        {
            S.AlertGroupsFolded = fold ? AlertCatalog.Groups.Select(g => g.Id).ToList() : new List<string>();
            _app.Save();
            FillAlertGroups();
            CountOpen();
        };
        _alertFoldChanged = CountOpen;
        CountOpen();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        var about = Core.Views.HudSettings.HelpTip.For("Alerts show in the Alerts widget. One isn't raised again within its cooldown; sticky ones stay until the condition clears.", "Alerts");
        about.VerticalAlignment = VerticalAlignment.Center;
        about.Margin = new Thickness(0, 0, 10, 0);
        buttons.Children.Add(about);
        foldAll.Margin = new Thickness(0);
        buttons.Children.Add(foldAll);
        root.Children.Add(buttons);
        FillAlertGroups();
        root.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), ClipToBounds = true, Child = _alertGroups });
        return root;
    }

    private FrameworkElement AlertGroup((string Id, string Title, string Icon, string[] Alerts) group)
    {
        var defs = group.Alerts.Select(AlertCatalog.Find).OfType<AlertDef>().ToList();
        bool folded = S.AlertGroupsFolded.Contains(group.Id, StringComparer.OrdinalIgnoreCase);
        var count = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        void Count() => count.Text = $"{defs.Count(d => RaceState.ChoiceFor(S, d).Enabled)} of {defs.Count} on";
        Count();
        var rows = new StackPanel { Margin = new Thickness(8, 0, 11, 6), Visibility = folded ? Visibility.Collapsed : Visibility.Visible };
        for (int i = 0; i < defs.Count; i++)
        {
            var cell = new Border { Child = AlertRow(defs[i], Count) };
            if (i > 0) Hairline.Frame(cell, Sides.Top);
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            rows.Children.Add(cell);
        }
        var chevron = Ui.FoldChevron(!folded);
        var header = new DockPanel { LastChildFill = true };
        header.Children.Add(chevron);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(new TextBlock { Text = group.Title, Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center });
        var bar = new Button { Style = Ui.Res<Style>("QuietButton"), Content = header, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(13, 8, 18, 6) };
        Controls.ClickGuard.SetAllowRepeat(bar, true);
        System.Windows.Automation.AutomationProperties.SetName(bar, group.Title);
        bar.Click += (_, _) =>
        {
            bool fold = rows.Visibility == Visibility.Visible;
            rows.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
            Ui.SetFold(chevron, !fold);
            S.AlertGroupsFolded.RemoveAll(x => string.Equals(x, group.Id, StringComparison.OrdinalIgnoreCase));
            if (fold) S.AlertGroupsFolded.Add(group.Id);
            _app.Save();
            _alertFoldChanged?.Invoke();
        };
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(rows);
        var section = Hairline.Frame(new Border { Child = stack }, Sides.Top);
        section.SetResourceReference(Border.BackgroundProperty, "Surface");
        section.SetResourceReference(Border.BorderBrushProperty, "Line");
        return section;
    }

    private FrameworkElement AlertRow(AlertDef def, Action changed)
    {
        var choice = RaceState.ChoiceFor(S, def);
        HudAlertSetting Mine() => S.Alerts.TryGetValue(def.Id, out var x) ? x : S.Alerts[def.Id] = new HudAlertSetting();
        var grid = new Grid { Margin = new Thickness(24, 6, 0, 6) };
        bool waiting = def.Id == "stint.limit" && S.Fuel.MaxStintMinutes <= 0;
        FrameworkElement? below = null;
        if (waiting)
        {
            var note = new TextBlock { Text = "Can't go off until Longest stint is set.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "WarnDeep");
            var go = Ui.LabeledButton("I.ChevronRight", "Fuel tab", () => SelectTab(FuelTabIndex));
            go.VerticalAlignment = VerticalAlignment.Center;
            below = new WrapPanel { Margin = new Thickness(0, 3, 0, 0), Children = { note, go } };
        }
        NumberBox? box = null;
        if (def.Threshold is not null)
        {
            box = new NumberBox(choice.Threshold ?? def.Threshold.Value, def.ThresholdUnit, 49)
            {
                Minimum = 0, Maximum = 1000, Step = def.Threshold.Value < 5 ? 0.5 : 1, Decimals = def.Threshold.Value % 1 == 0 ? 0 : 1,
                ToolTip = "When it's raised",
            };
            box.ColumnDefinitions[2].MinWidth = UnitColumn;
            box.ValueChanged += v => { Mine().Threshold = v; Changed(); };
        }
        var on = Ui.Switch(choice.Enabled, v =>
        {
            Mine().Enabled = v;
            Changed();
            Apply(v);
            changed();
        });
        on.ToolTip = "Raise this alert";
        System.Windows.Automation.AutomationProperties.SetName(on, def.Title);
        ToggleCards.Row(on, ToggleCards.RowTitle(def.Title), def.Description.Length > 0 ? def.Description : null, below, box);
        grid.Children.Add(on);
        void Apply(bool enabled)
        {
            if (box is not null) box.IsEnabled = enabled && !waiting;
        }
        Apply(choice.Enabled);
        return grid;
    }

    private const double UnitColumn = 34;

    private StackPanel FuelTab()
    {
        var root = new StackPanel();
        var fuel = S.Fuel;
        NumberBox Box(double value, string unit, double min, double max, double step, int decimals, Action<double> set)
        {
            var b = new NumberBox(value, unit, 57) { Minimum = min, Maximum = max, Step = step, Decimals = decimals };
            b.ColumnDefinitions[2].MinWidth = UnitColumn;
            b.ValueChanged += v => { set(v); Changed(); RefreshFormulas(); };
            return b;
        }
        var std = new HudFuelSettings();
        Ui.SettingDefault D(Func<bool> same, Action reset, string shown) => new(same, () => { reset(); Changed(); Build(); }, shown);
        root.Children.Add(Group("Fuel and strategy", null,
            Ui.SubHeader("Measuring", first: true),
            Ui.SettingRow("Laps measured", "Recent clean green-flag laps used for fuel per lap.", Box(fuel.Window, "laps", 1, 30, 1, 0, v => fuel.Window = (int)v),
                D(() => fuel.Window == std.Window, () => fuel.Window = std.Window, RaceFormat.Count(std.Window, "lap")),
                "Fuel per lap comes from this many recent clean laps under green; newer laps count more and a lap far from the rest is left out."),
            Ui.SettingRow("Extra consumption", "For a pace that uses more than your clean laps.", Box(fuel.ExtraPercent, "%", 0, 25, 0.5, 1, v => fuel.ExtraPercent = v),
                D(() => fuel.ExtraPercent == std.ExtraPercent, () => fuel.ExtraPercent = std.ExtraPercent, "0%"),
                "This share of the expected fuel to the flag is added, for a pace or style that uses more than your clean laps did."),
            Ui.SubHeader("Extra fuel"),
            Ui.SettingRow("Safety reserve", "Extra fuel for lap-to-lap variation.",
                Ui.Segmented(new[] { "Aggressive", "Balanced", "Conservative" }, (int)fuel.Safety, i => { fuel.Safety = (FuelSafety)i; Changed(); RefreshFormulas(); }, "fuelsafety"),
                D(() => fuel.Safety == std.Safety, () => fuel.Safety = std.Safety, std.Safety.ToString()),
                "Extra fuel for how much your laps vary, how sure the average is and how fuel use drifts over a stint (at least 1 / 1.5 / 2% of the fuel to the flag): Aggressive ≈ enough 9 times in 10, Balanced ≈ 19 in 20, Conservative ≈ 99 in 100. A model, not a guarantee; it needs 2 clean laps."),
            Ui.SettingRow("Margin at the flag", "Your own fuel on top of the reserve.", Box(fuel.MarginLaps, "laps", 0, 10, 0.1, 1, v => fuel.MarginLaps = v),
                D(() => fuel.MarginLaps == std.MarginLaps, () => fuel.MarginLaps = std.MarginLaps, $"{std.MarginLaps:0.0} laps")),
            Ui.SettingRow("Fixed reserve", "Kept on top of everything else.", Box(fuel.ReserveLiters, "L", 0, 50, 0.1, 1, v => fuel.ReserveLiters = v),
                D(() => fuel.ReserveLiters == std.ReserveLiters, () => fuel.ReserveLiters = std.ReserveLiters, "0 L")),
            Ui.SettingRow("Extra distance", "A long pit lane, the lap after the flag.", Box(fuel.ExtraKm, Units.Km, 0, 50, 0.1, 1, v => fuel.ExtraKm = v),
                D(() => fuel.ExtraKm == std.ExtraKm, () => fuel.ExtraKm = std.ExtraKm, $"0 {Units.Km}"),
                "Distance on top of the race (a long pit lane, the lap after the flag), turned into laps of this track."),
            Ui.SubHeader("Stops"),
            Ui.SettingRow("A pit stop costs", "Lane plus standing time.", Box(fuel.PitLaneLossSeconds, Units.S, 0, 300, 1, 0, v => fuel.PitLaneLossSeconds = v),
                D(() => fuel.PitLaneLossSeconds == std.PitLaneLossSeconds, () => fuel.PitLaneLossSeconds = std.PitLaneLossSeconds, $"{std.PitLaneLossSeconds:0} {Units.S}"),
                "Lane plus standing time, used for strategy notes."),
            Ui.SettingRow("Longest stint", "0 = no limit.", Box(fuel.MaxStintMinutes, "min", 0, 600, 5, 0, v => fuel.MaxStintMinutes = v),
                D(() => fuel.MaxStintMinutes == std.MaxStintMinutes, () => fuel.MaxStintMinutes = std.MaxStintMinutes, "no limit"),
                "The Stint time alert warns before it.")));

        _formulas = new UniformGrid { Margin = new Thickness(0, 3, 0, 0) };
        _formulas.SizeChanged += (_, e) => FitFormulaColumns(e.NewSize.Width);
        _formulaNote = Caption("", 0);
        _formulaCaution = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };
        _formulaCautionLine = Ui.IconText("I.Alert", _formulaCaution, "WarnDeep");
        _formulaCautionLine.Margin = new Thickness(0, 5, 0, 0);
        Button sums = null!;
        sums = Ui.LabeledButton("I.ChevronDown", _showSums ? "Hide the sums" : "Show the sums", () =>
        {
            _showSums = !_showSums;
            _formulaRows.Clear();
            RefreshFormulas();
            sums.Content = Ui.IconLabel(_showSums ? "I.ChevronsUp" : "I.ChevronDown", _showSums ? "Hide the sums" : "Show the sums");
        }, "GhostButton");
        sums.HorizontalAlignment = HorizontalAlignment.Left;
        sums.Margin = new Thickness(0, 8, 0, 0);
        var card = Group("How the numbers are worked out", "The fuel widget's figures from the session running now (or the simulated one). Every figure to the flag is an estimate.",
            new StackPanel { Children = { _formulaNote, _formulaCautionLine, _formulas, sums } });
        Ui.Anchor(card, FuelAnchor);
        root.Children.Add(card);
        _formulaRows.Clear();
        RefreshFormulas();
        _tick = RefreshFormulas;
        return root;
    }

    private const double FormulaColumnsFrom = 960;

    private void FitFormulaColumns(double width)
    {
        if (_formulas is null) return;
        bool side = !_showSums && width >= FormulaColumnsFrom;
        int columns = side ? _formulas.Children.Count : 1;
        if (_formulas.Columns != columns) _formulas.Columns = columns;
        for (int i = 0; i < _formulas.Children.Count; i++)
            if (_formulas.Children[i] is FrameworkElement column)
                column.Margin = new Thickness(0, 0, side && i < _formulas.Children.Count - 1 ? Ui.PanelSpacing : 0, side ? 0 : 6);
    }

    private bool _showSums;
    private UniformGrid? _formulas;
    private TextBlock? _formulaNote, _formulaCaution;
    private FrameworkElement? _formulaCautionLine;
    private readonly List<(TextBlock Now, TextBlock Formula)> _formulaRows = new();

    private void RefreshFormulas()
    {
        if (_formulas is null || _formulaNote is null || _formulaCaution is null || _formulaCautionLine is null) return;
        var race = Preview.Race;
        var f = race.Frame;
        var u = S.Units;
        var fuel = S.Fuel;
        var plan = race.Fuel;
        string F(double? litres) => litres is { } l ? RaceFormat.Fuel(l, u) : RaceFormat.Missing;
        string L(double? laps) => laps is { } l ? $"{l:0.0} laps" : RaceFormat.Missing;
        var est = race.FuelData.Estimate;
        double? togo = race.LapsToGo(f);
        double? rate = est.HasEstimate ? est.MeanLitersPerLap : null;
        double? km = f.Session.TrackLengthKm is > 0 and var k ? k : null;
        double floor = Core.SimRacing.FuelEngine.FuelStrategyCalculator.MinimumReserveFraction(new Core.SimRacing.FuelEngine.FuelStrategyRequest
        {
            CurrentFuelLiters = 0, LapsRemaining = 0, Consumption = est, SafetyMode = FuelPlanner.Mode(fuel.Safety),
        });
        string Spread() => est.StandardDeviationLitersPerLap is { } sd ? $"± {RaceFormat.Fuel(sd, u)} a lap" : "needs 2 clean laps";
        string Extra(double? litres) => litres is { } l ? (l > 0 ? "+" + F(l) : "none") : RaceFormat.Missing;
        var sections = new (string Title, (string Name, string Formula, string Now)[] Rows)[]
        {
            ("Measured", new[]
            {
                ("Fuel per lap", $"From your last {RaceFormat.Count(fuel.Window, "clean lap")} under green (no pit road, refuel, full-course caution, formation lap or incident): newer laps count more, and from 5 laps one far from the rest is left out.",
                    est.HasEstimate ? $"{RaceFormat.Fuel(est.MeanLitersPerLap, u)} · {RaceFormat.Count(est.UsedLapCount, "lap")} · {Core.SimRacing.Hud.Widgets.FuelWidget.Confidence(est.Reliability)}" : race.FuelData.EnergyUnits ? "kWh car: not supported" : "measuring…"),
                ("Lap to lap", "How much the clean laps differ (weighted spread); it sets the safety reserve.", est.HasEstimate ? Spread() : RaceFormat.Missing),
                ("Laps of fuel", "Fuel in the tank ÷ fuel per lap.", plan is { } p1 ? $"{F(f.Fuel)} ÷ {F(p1.PerLap)} = {L(p1.LapsOfFuel)}" : RaceFormat.Missing),
                ("Laps to go", "Lap races: total laps − laps done − the part of this lap already driven (− laps the leader is ahead). Timed races (a forecast until iRacing shows the white flag): the leader takes the flag at their first crossing once the clock has run out; you finish at your first crossing after that, at your recent pace.",
                    togo is { } tg ? L(tg) + (plan?.LapsToGoForecast == true ? " (forecast)" : "") : RaceFormat.Missing),
                ("Rest of this lap", "From the fuel your clean laps used on each tenth of the lap (once 3 clean laps cover what's left of this one); until then this lap's share × fuel per lap.",
                    plan?.CurrentLap is { } cl ? $"{F(cl)} ({(plan.CurrentLapFromProfile ? "lap profile" : "share of a lap")})" : RaceFormat.Missing),
            }),
            ("To the flag", new[]
            {
                ("Expected", "Rest of this lap + whole laps after it × fuel per lap.", F(plan?.Expected)),
                ("Safety reserve", $"{fuel.Safety}: covers the lap-to-lap spread over the laps to go, the average's own uncertainty, and at least {floor:P1} of the expected fuel for drift over a stint (a model, not a guarantee).",
                    plan is null ? RaceFormat.Missing : plan.StatisticalReserve > 0 ? F(plan.StatisticalReserve) : "none yet (needs 2 clean laps)"),
                ("Margin at the flag", $"{fuel.MarginLaps:0.#} laps × fuel per lap.", Extra(rate * fuel.MarginLaps)),
                ("Fixed reserve", "As set above.", Extra(fuel.ReserveLiters)),
                ("Extra consumption", $"{fuel.ExtraPercent:0.#}% × fuel per lap × laps to go.", Extra(rate * togo * fuel.ExtraPercent / 100)),
                ("Extra distance", $"{fuel.ExtraKm:0.#} {Units.Km} ÷ the track's length × fuel per lap.", km is null && fuel.ExtraKm > 0 ? "track length unknown" : Extra(rate * (km is { } length ? fuel.ExtraKm / length : 0))),
                ("Fuel to the flag", "Expected + safety reserve + the four extras above.", F(plan?.ToFinish)),
            }),
            ("Plan", new[]
            {
                ("Spare after reserve", "Fuel in the tank − fuel to the flag (below zero = you need to stop).", plan?.Surplus is { } s ? (s >= 0 ? $"+{F(s)} to spare" : $"short by {F(-s)}") : RaceFormat.Missing),
                ("Left at the flag", "Fuel in the tank − expected to the flag, before any reserve or extra (below zero = you run out first).", plan?.AtFlag is { } at ? (at >= 0 ? F(at) : $"short by {F(-at)}") : RaceFormat.Missing),
                ("Stops", "Shortfall ÷ usable tank, rounded up.", plan?.Stops is { } st ? $"{st}" + (race.Tank is { } tank ? $" (tank {F(tank)})" : "") : RaceFormat.Missing),
                ("Add at the next stop", $"The shortfall rounded up to {FuelMath.AddStep:0.0} L, at most a full tank (you arrive close to empty).", F(plan?.AddAtNextStop)),
                ("Pit window", "Opens when the stops still to come can cover the rest: laps to go − (stops × tank − reserve and extras) ÷ fuel per lap. Closes when the fuel runs out (laps of fuel).",
                    plan?.WindowOpensIn is { } o ? (o <= 0 ? $"open now · closes in {L(plan.WindowClosesIn)}" : $"opens in {L(o)} · closes in {L(plan.WindowClosesIn)}") : plan?.Stops == 0 ? "no stop needed" : RaceFormat.Missing),
            }),
        };
        _formulaNote.Text = f.Status != SimStatus.Connected ? "Waiting for a session." : f.Simulated ? "Numbers from the simulated session (no iRacing running)." : "Live from iRacing.";
        string? caution = f.Status != SimStatus.Connected ? null : race.FuelData.EnergyUnits ? "This car reports energy (kWh), not fuel: the fuel calculator doesn't run for it." : plan?.Warning ?? FuelPlanner.Warning(est);
        _formulaCaution.Text = caution ?? "";
        _formulaCautionLine.Visibility = caution is null ? Visibility.Collapsed : Visibility.Visible;
        if (_formulaRows.Count != sections.Sum(x => x.Rows.Length))
        {
            _formulas.Children.Clear();
            _formulaRows.Clear();
            for (int section = 0; section < sections.Length; section++)
            {
                var (title, rows) = sections[section];
                var column = new StackPanel();
                _formulas.Children.Add(column);
                column.Children.Add(Ui.SubHeader(title, first: section == 0 || !_showSums));
                bool result = title == "Plan";
                for (int i = 0; i < rows.Length; i++)
                {
                    if (i > 0) column.Children.Add(Hairline.Rule(new Border { Margin = new Thickness(0, 5, 0, 5), Background = Ui.Res<Brush>("Line") }));
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_showSums ? 154 : 136) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = _showSums ? new GridLength(227) : new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = _showSums ? new GridLength(1, GridUnitType.Star) : new GridLength(0) });
                    grid.Children.Add(new TextBlock { Text = rows[i].Name, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) });
                    var now = new TextBlock { Style = Ui.Res<Style>("Body"), FontFamily = Ui.Res<FontFamily>("MonoFont"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 13, 0) };
                    if (result)
                    {
                        now.FontWeight = FontWeights.SemiBold;
                        now.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                    }
                    Grid.SetColumn(now, 1);
                    grid.Children.Add(now);
                    var formula = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0), Visibility = _showSums ? Visibility.Visible : Visibility.Collapsed };
                    Grid.SetColumn(formula, 2);
                    grid.Children.Add(formula);
                    column.Children.Add(grid);
                    _formulaRows.Add((now, formula));
                }
            }
            FitFormulaColumns(_formulas.ActualWidth);
        }
        int n = 0;
        foreach (var (_, rows) in sections)
            foreach (var row in rows)
            {
                var (now, formula) = _formulaRows[n++];
                bool missing = row.Now == RaceFormat.Missing;
                if (missing != (now.ReadLocalValue(TextBlock.FontFamilyProperty) == DependencyProperty.UnsetValue))
                {
                    if (missing) now.ClearValue(TextBlock.FontFamilyProperty);
                    else now.FontFamily = Ui.Res<FontFamily>("MonoFont");
                }
                TextCase.Set(now, row.Now);
                TextCase.Set(formula, row.Formula);
            }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("hud", () => { SelectTab(WidgetsTab); return Task.CompletedTask; });
        yield return ("hud-rules", () => { _rulesCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("hud-rules-arrange", () => { _rules?.SetEditing(true); _rulesCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("hud-rules-done", () => { _rules?.SetEditing(false); return Task.CompletedTask; });
        yield return ("hud-widgets", () => { _widgets?.Select("relative"); return Task.CompletedTask; });
        yield return ("hud-standings", () => { _widgets?.Select("standings"); return Task.CompletedTask; });
        yield return ("hud-standings-every-column", () =>
        {
            var all = Core.SimRacing.Hud.Widgets.StandingsWidget.Options.First(o => o.Key == "columns").Choices!.Select(c => c.Id);
            _widgets?.SetOption("standings", "columns", string.Join(",", all));
            _widgets?.ShowAll("standings");
            return Task.CompletedTask;
        });
        yield return ("hud-standings-default-columns", () => { _widgets?.SetOption("standings", "columns", null); return Task.CompletedTask; });
        yield return ("hud-standings-scrolled", async () =>
        {
            _widgets?.ShowAll("standings");
            await Task.Delay(300);
            _widgets?.ScrollPageBy(900);
            await Task.Delay(300);
        });
        yield return ("hud-every-widget", () => { _widgets?.DrawEveryKind(); _widgets?.Select("fuel"); return Task.CompletedTask; });
        yield return ("hud-standings-click-class-heading", () => { _widgets?.Reveal("standings", "classInfo"); return Task.CompletedTask; });
        yield return ("hud-standings-hover-title-row", () => { _widgets?.Reveal("standings", "titleRow"); return Task.CompletedTask; });
        yield return ("hud-standings-column-ticks", async () => { _widgets?.Reveal("standings", "columns"); await Task.Delay(300); });
        yield return ("hud-relative-panels", async () =>
        {
            _widgets?.Select("relative");
            await Task.Delay(300);
            _widgets?.ScrollPageBy(700);
            await Task.Delay(300);
        });
        yield return ("hud-relative-pick-classes", () => { _widgets?.SetOption("relative", "classes", "pick"); _widgets?.ShowAll("relative"); return Task.CompletedTask; });
        yield return ("hud-relative-all-classes", () => { _widgets?.SetOption("relative", "classes", null); return Task.CompletedTask; });
        yield return ("hud-timing", () => { _widgets?.ShowAll("timing"); return Task.CompletedTask; });
        yield return ("hud-fuel-sections", () => { _widgets?.ShowAll("fuel"); return Task.CompletedTask; });
        foreach (var def in HudCatalog.All)
        {
            string kind = def.Kind;
            yield return ($"hud-sweep-{kind}", async () => { if (_tab != WidgetsTab) SelectTab(WidgetsTab); _widgets?.SweepSelect(kind); await Task.Delay(500); _widgets?.SweepSelect(kind); await Task.Delay(300); });
            foreach (var (name, target) in _widgets?.SweepParts() ?? Array.Empty<(string, RaceHudWidgets.PanelTarget)>())
            {
                yield return ($"hud-sweep-{kind}-{name}", async () => { _widgets?.SweepOpen(target); await Task.Delay(200); });
                if (name != "all") continue;
                int drops = _widgets?.SweepDropdowns() ?? 0;
                for (int i = 0; i < drops; i++)
                {
                    int at = i;
                    yield return ($"hud-sweep-{kind}-all-drop{at}", async () => { _widgets?.SweepOpenDropdown(at); await Task.Delay(250); });
                }
                yield return ($"hud-sweep-{kind}-exercise", async () => { if (_widgets is { } w) await w.SweepExercise(kind); });
            }
        }
        yield return ("hud-alerts", () => { SelectTab(AlertsTabIndex); return Task.CompletedTask; });
        yield return ("hud-fuel", () => { SelectTab(FuelTabIndex); return Task.CompletedTask; });
        yield return ("hud-settings", () => { SelectTab(SettingsTab); return Task.CompletedTask; });
        yield return ("hud-settings-back", () => { SelectTab(WidgetsTab); return Task.CompletedTask; });
    }

    public bool CancelEdit() => _rules?.Cancel() == true;

    private void SelectTab(int tab)
    {
        if (_segments.Child is Panel panel && panel.Children[tab] is RadioButton rb) rb.IsChecked = true;
        _tab = tab;
        Build();
    }

    public const string LayoutsAnchor = "simracing.hud.layouts", AlertsAnchor = "simracing.hud.alerts", FuelAnchor = "simracing.hud.fuelformulas", SettingsAnchor = "simracing.hud.settings", WebAnchor = "simracing.hud.web";

    public bool ShowTabFor(string anchor)
    {
        int tab = anchor switch
        {
            LayoutsAnchor => WidgetsTab,
            AlertsAnchor => AlertsTabIndex,
            FuelAnchor => FuelTabIndex,
            SettingsAnchor or WebAnchor => SettingsTab,
            _ => -1,
        };
        Finish();
        if (tab < 0 || tab == _tab) return false;
        SelectTab(tab);
        return true;
    }
}

internal sealed class PreviewSource
{
    private readonly DemoTelemetry _demo = new();
    private readonly TelemetryFrame _frame = new();
    private long _seq;
    private static Task<PreviewSource>? _warming;

    public static void Warm(Func<RaceHudSettings> settings) => _warming ??= Task.Run(() => new PreviewSource(Simulation(settings)));

    public static PreviewSource Take(Func<RaceHudSettings> settings)
    {
        var warming = _warming;
        _warming = null;
        if (warming is not null)
        {
            try
            {
                var warmed = warming.GetAwaiter().GetResult();
                warmed._demo.Restart();
                warmed.Advance(null);
                return warmed;
            }
            catch (Exception ex) { App.LogError(ex); }
        }
        return new PreviewSource(settings);
    }

    public PreviewSource(Func<RaceHudSettings> settings) : this(Simulation(settings))
    {
        Advance(null);
    }

    private PreviewSource(RaceState simulated)
    {
        Simulated = simulated;
        DemoTelemetry.WarmUp(Simulated, _frame, ref _seq);
    }

    private static RaceState Simulation(Func<RaceHudSettings> settings) => new(settings) { MapLoader = SimRacing.TrackMaps.TrackMapLibrary.ForRace };

    public RaceState Simulated { get; }

    public RaceState Race { get; private set; } = null!;

    public void Advance(HudHost? host)
    {
        if (host?.Race.Frame is { Status: SimStatus.Connected })
        {
            Race = host.Race;
            return;
        }
        _demo.Fill(_frame);
        _frame.Sequence = ++_seq;
        Simulated.Update(_frame);
        Race = Simulated;
    }
}
