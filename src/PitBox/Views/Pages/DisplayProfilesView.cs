using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Audio;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Services;

namespace PitBox.Views.Pages;

internal sealed class DisplayProfilesView : Tile
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly System.Windows.Controls.Primitives.UniformGrid _grid = new() { Margin = new Thickness(0, 10, -CardGap, -CardGap) };
    private readonly Func<bool> _hasPending;
    private readonly Border _empty = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch };

    public DisplayProfilesView(AppServices app, MainWindow window, Func<bool> hasPending)
    {
        _app = app;
        _window = window;
        _hasPending = hasPending;
        Style = Ui.Res<Style>("Card");
        Margin = new Thickness(0, 0, 0, 13);

        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text("Saved profiles", "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        Ui.AddInfo(title, "Saved setups like “Desk” and “Sim rig”: which displays are on, arrangement, resolution, refresh rate, main display, HDR, taskbar, wallpapers and audio devices. Give each a keybind to switch between them.");
        head.Children.Add(title);
        var save = Ui.LabeledButton("I.Save", "Save current setup", SaveCurrentAsync, "PrimaryButton");
        Grid.SetColumn(save, 1);
        head.Children.Add(save);
        stack.Children.Add(head);
        stack.Children.Add(_grid);
        stack.Children.Add(_empty);
        Child = stack;
        _grid.SizeChanged += (_, e) => FitColumns(e.NewSize.Width - CardGap);
        Ui.Anchor(this, "displays.profiles");
    }

    public void Refresh()
    {
        _grid.Children.Clear();
        var profiles = _app.Config.Displays.Profiles;
        if (profiles.Count == 0)
        {
            _grid.Visibility = Visibility.Collapsed;
            _empty.Child = Ui.EmptyState("I.Monitor", "No display profiles yet",
                "Set your monitors up the way you like (here or in Windows), then Save current setup. One for each setup: desk, triples, TV…");
            _empty.Visibility = Visibility.Visible;
            return;
        }
        _grid.Visibility = Visibility.Visible;
        _empty.Visibility = Visibility.Collapsed;
        var active = _app.DisplayProfiles.Active;
        foreach (var profile in profiles)
            _grid.Children.Add(ProfileTile(profile, profile == active));
        FitColumns(_grid.ActualWidth - CardGap);
    }

    private FrameworkElement ProfileTile(DisplayProfile profile, bool isActive)
    {
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = RaceParts.Big(profile.Name, 21, "Heading", italic: false, heavy: false);
        name.TextWrapping = TextWrapping.Wrap;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.Margin = new Thickness(0, 0, 8, 0);
        top.Children.Add(name);
        var tags = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (isActive)
        {
            var activeTag = new Grid { Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            activeTag.Children.Add(new Slant("SageSoft", 0.14, 0, 1, 0, 0.86, 1, 0, 1));
            var activeText = RaceParts.Big("Active", 12.5, "SageDeep", italic: false, heavy: false);
            activeText.Margin = new Thickness(11, 1, 11, 2);
            activeTag.Children.Add(activeText);
            tags.Children.Add(activeTag);
        }
        if (profile.Hotkey is { } hk)
            tags.Children.Add(Ui.Chip(PitBox.Hotkeys.Hotkey.Display(hk)));
        if (tags.Children.Count > 0)
        {
            Grid.SetRow(tags, 1);
            Grid.SetColumnSpan(tags, 2);
            top.Children.Add(tags);
        }
        var apply = Ui.LabeledButton("I.Zap", isActive ? "Apply again" : "Apply", () => _ = _app.DisplayProfiles.ApplyAsync(profile));
        apply.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(apply, 1);
        top.Children.Add(apply);
        stack.Children.Add(top);

        var previewBox = new Tile { Style = Ui.Res<Style>("Inset"), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(5) };
        if (profile.Layout is null)
            previewBox.Child = new TextBlock { Text = "Leaves the arrangement as it is", Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 32, 0, 32) };
        else
        {
            var preview = new MonitorLayoutView { Height = 89 };
            preview.SetTiles(ProfileLayout.Tiles(_app, profile.Layout));
            previewBox.Child = preview;
        }
        stack.Children.Add(previewBox);

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (profile.Hdr != HdrMode.Unchanged) chips.Children.Add(Ui.Chip(profile.Hdr == HdrMode.On ? "HDR on" : "HDR off"));
        if (profile.Taskbar != TaskbarMode.Unchanged) chips.Children.Add(Ui.Chip(profile.Taskbar == TaskbarMode.MainOnly ? "Taskbar: main display only" : "Taskbar: every display"));
        if (profile.Wallpapers.Count > 0) chips.Children.Add(Ui.Chip(SimRacing.Race.RaceFormat.Count(profile.Wallpapers.Count, "wallpaper")));
        if (profile.Audio?.PlaybackName is { } speakers) chips.Children.Add(Ui.Chip("Audio: " + speakers));
        if (profile.Volume is { } vol) chips.Children.Add(Ui.Chip($"Volume {vol}%"));
        if (chips.Children.Count > 0)
            stack.Children.Add(chips);

        var changes = isActive ? new List<Display.ProfileChange>() : _app.DisplayProfiles.Preview(profile);
        if (changes.Count > 0)
            stack.Children.Add(ChangesFold(changes.Select(c => (FrameworkElement)Ui.ChangeLine(c.Text, c.Area)).ToList()));
        else
            stack.Children.Add(new TextBlock { Text = isActive ? "On now: applying it changes nothing" : "Applying it changes nothing", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(5, 9, 0, 3) });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        var edit = Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditAsync(profile, isNew: false), "GhostButton");
        edit.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(edit);
        var more = Ui.IconButton("I.More", "More for this profile", () => { });
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            Ui.DropBelow(menu);
            var update = new MenuItem { Header = "Overwrite with current", ToolTip = "Save the setup that's on right now (layout, wallpapers and audio devices) into this profile" };
            update.Click += (_, _) => UpdateToCurrent(profile);
            menu.Items.Add(update);
            menu.IsOpen = true;
        };
        actions.Children.Add(more);

        var card = new DockPanel { Margin = new Thickness(11, 10, 11, 5) };
        DockPanel.SetDock(actions, Dock.Bottom);
        card.Children.Add(actions);
        card.Children.Add(stack);
        var body = new Grid { ClipToBounds = true };
        body.Children.Add(card);
        return new Tile
        {
            Style = Ui.Res<Style>("Inset"), Padding = new Thickness(0), Child = body, ClipToBounds = true, Margin = new Thickness(0, 0, CardGap, CardGap),
            BorderBrush = Ui.Res<Brush>(isActive ? "Sage" : "Line"), VerticalAlignment = VerticalAlignment.Top,
        };
    }

    private const double CardGap = 10, CardMinWidth = 356;

    private void FitColumns(double width)
    {
        int count = Math.Max(1, _grid.Children.Count);
        int columns = Math.Clamp((int)((width + CardGap) / (CardMinWidth + CardGap)), 1, Math.Min(3, count));
        if (_grid.Columns != columns) _grid.Columns = columns;
    }

    private static StackPanel ChangesFold(List<FrameworkElement> lines)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var list = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(3, 3, 0, 0) };
        foreach (var line in lines) list.Children.Add(line);
        var chevron = new Icon { Data = Ui.Res<Geometry>("I.ChevronRight"), Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(chevron);
        label.Children.Add(new TextBlock { Text = lines.Count == 1 ? "What applying it changes (1)" : $"What applying it changes ({lines.Count})", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var toggle = new Button { Style = Ui.Res<Style>("QuietButton"), Content = label, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(5, 3, 6, 3) };
        ClickGuard.SetAllowRepeat(toggle, true);
        toggle.Click += (_, _) =>
        {
            bool open = list.Visibility != Visibility.Visible;
            list.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Data = Ui.Res<Geometry>(open ? "I.ChevronDown" : "I.ChevronRight");
        };
        panel.Children.Add(toggle);
        panel.Children.Add(list);
        return panel;
    }

    private async void SaveCurrentAsync()
    {
        if (_hasPending())
        {
            _app.Notify("Apply your changes first", "Profiles save the setup that's actually on.", NoticeKind.Info, important: true);
            return;
        }
        int n = _app.Config.Displays.Profiles.Count + 1;
        string name = $"Profile {n}";
        while (_app.DisplayProfiles.Find(name) is not null) name = $"Profile {++n}";
        await EditAsync(_app.DisplayProfiles.CaptureCurrent(name), isNew: true);
    }

    private void UpdateToCurrent(DisplayProfile profile)
    {
        if (_hasPending())
        {
            _app.Notify("Apply your changes first", "Profiles save the setup that's actually on.", NoticeKind.Info, important: true);
            return;
        }
        var (layout, wallpapers, audio) = (profile.Layout, profile.Wallpapers, profile.Audio);
        _app.DisplayProfiles.Recapture(profile);
        _app.DisplayProfiles.RaiseChanged();
        _app.Notify("Profile updated", $"“{profile.Name}” now matches your current setup.", NoticeKind.Success, action: new NoticeAction("Undo", () =>
        {
            if (!_app.Config.Displays.Profiles.Contains(profile)) return;
            (profile.Layout, profile.Wallpapers, profile.Audio) = (layout, wallpapers, audio);
            _app.DisplayProfiles.RaiseChanged();
        }));
    }

    private async Task<bool> DeleteAsync(DisplayProfile profile)
    {
        string message = "This can't be undone.";
        if (!await _window.Confirm($"Delete “{profile.Name}”?", message, "Delete", danger: true))
            return false;
        _app.DisplayProfiles.Remove(profile);
        return true;
    }

    public void OpenNew() => SaveCurrentAsync();

    public void OpenEditor(int index)
    {
        var profiles = _app.Config.Displays.Profiles;
        if (index < profiles.Count)
            _ = EditAsync(profiles[index], false);
    }

    private async Task EditAsync(DisplayProfile profile, bool isNew)
    {
        bool deleteAsked = false;
        var editor = new DisplayProfileEditor(_app, _window, profile, isNew, isNew ? null : () => { deleteAsked = true; _window.CloseModal(false); }, _hasPending());
        if (!await _window.ShowModal(editor.Card))
        {
            if (deleteAsked && !await DeleteAsync(profile)) await EditAsync(profile, false);
            return;
        }
        string oldName = profile.Name;
        editor.SaveInto(profile);
        if (isNew)
            _app.Config.Displays.Profiles.Add(profile);
        else
            _app.DisplayProfiles.Renamed(oldName, profile.Name);
        _app.DisplayProfiles.RaiseChanged();
    }
}

internal sealed class DisplayProfileEditor
{
    private const double LabelWidth = 138;
    private readonly AppServices _app;
    private readonly DisplayProfile _profile;
    private readonly TextBox _name = new() { MaxLength = Ui.NameLength };
    private readonly HotkeyBox _hotkey = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, TextBox> _wallpapers = new(StringComparer.OrdinalIgnoreCase);
    private DisplaySnapshot? _current;
    private int _layoutChoice;
    private HdrMode _hdr;
    private TaskbarMode _taskbar;
    private int? _volume;
    private readonly ComboBox _playback = new(), _recording = new();
    private readonly CheckBox _comms = new() { Content = "Also use them for calls (Discord, Teams…)" };

    public Border Card { get; }

    public DisplayProfileEditor(AppServices app, MainWindow window, DisplayProfile profile, bool isNew, Action? onDelete = null, bool pendingEdits = false)
    {
        _app = app;
        _profile = profile;
        _name.Text = profile.Name;
        _hotkey.SetHotkey(profile.Hotkey);
        _hdr = profile.Hdr;
        _taskbar = profile.Taskbar;
        _layoutChoice = profile.Layout is null ? 2 : 0;
        try { _current = isNew ? profile.Layout : app.Displays.CaptureLayout(); } catch { _current = null; }

        var stack = new StackPanel();
        stack.Children.Add(Ui.FieldRow("Name", _name, LabelWidth));
        stack.Children.Add(Ui.FieldRow("Keybind", _hotkey, LabelWidth));

        stack.Children.Add(Ui.EditorSection("Monitors"));
        string[] layoutOptions = isNew ? new[] { "Save this layout", "", "Don't change displays" }
            : new[] { profile.Layout is null ? "" : "Keep saved layout", pendingEdits ? "" : "Use current layout", "Don't change displays" };
        var options = layoutOptions.Select((t, i) => (t, i)).Where(x => x.t.Length > 0).ToList();
        stack.Children.Add(Ui.FieldRow("Layout", Ui.Segmented(options.Select(o => o.t).ToList(), Math.Max(0, options.FindIndex(o => o.i == _layoutChoice)),
            i => _layoutChoice = options[i].i, "layout"), LabelWidth,
            "Which monitors are on, their arrangement, resolution, refresh rate, orientation and the main display."));
        stack.Children.Add(Ui.FieldRow("HDR", Ui.Segmented(new[] { "Leave as is", "On", "Off" }, (int)_hdr, i => _hdr = (HdrMode)i, "hdr",
            new[] { "Don't change HDR when this profile is applied", "Turn HDR on (on displays that support it)", "Turn HDR off" }), LabelWidth));
        stack.Children.Add(Ui.FieldRow("Taskbar", Ui.Segmented(new[] { "Leave as is", "Every display", "Main display only" }, (int)_taskbar, i => _taskbar = (TaskbarMode)i, "taskbar",
            new[] { "Don't change the taskbar", "Show the taskbar on every display", "Show the taskbar only on the main display (e.g. keeps it off side screens in a triple setup)" }), LabelWidth));

        stack.Children.Add(Ui.EditorSection("Audio"));
        stack.Children.Add(Ui.FieldHint("The devices to switch to, e.g. headphones at the desk and the rig's speakers on the triples. Saving the current setup fills these in.", 0));
        _volume = profile.Volume;
        AudioPicker.Fill(app, _playback, EDataFlow.Render, profile.Audio?.PlaybackId, profile.Audio?.PlaybackName);
        AudioPicker.Fill(app, _recording, EDataFlow.Capture, profile.Audio?.RecordingId, profile.Audio?.RecordingName);
        stack.Children.Add(Ui.FieldRow("Speakers / headset", _playback, LabelWidth));
        stack.Children.Add(Ui.FieldRow("Microphone", _recording, LabelWidth));
        _comms.IsChecked = profile.Audio?.SetCommunications ?? true;
        stack.Children.Add(Ui.FieldRow("", _comms, LabelWidth));
        stack.Children.Add(Ui.FieldRow("Volume", AudioPicker.Volume(_volume, v => _volume = v), LabelWidth));

        stack.Children.Add(Ui.EditorSection("Wallpapers"));
        stack.Children.Add(Ui.FieldHint("One per monitor; empty keeps whatever it has. Saving the current setup fills these in.", 0));
        foreach (var (key, name) in WallpaperMonitors())
        {
            var box = new TextBox { Tag = "Keep its wallpaper", Text = profile.Wallpapers.FirstOrDefault(w => string.Equals(w.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "" };
            _wallpapers[key] = box;
            var row = new DockPanel();
            var clear = Ui.IconButton("I.X", "Keep its wallpaper", () => box.Text = "");
            var browse = Ui.IconButton("I.Folder", "Choose an image", () =>
            {
                if (ProgramPicker.BrowseExe("Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*") is { } path)
                    box.Text = path;
            });
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(clear);
            row.Children.Add(browse);
            row.Children.Add(box);
            stack.Children.Add(Ui.FieldRow(name, row, LabelWidth));
        }

        var buttons = Ui.DialogButtons(window, isNew ? "Save profile" : "Save changes", Validate);
        var delete = onDelete is null ? null : Ui.LabeledButton("I.Trash", "Delete profile", onDelete, "DangerButton");
        Card = Ui.Dialog(window, isNew ? "New display profile" : $"Edit “{profile.Name}”", "", stack, buttons, _error, 640, delete);
    }

    private IEnumerable<(string Key, string Name)> WallpaperMonitors()
    {
        List<Display.MonitorInfo> monitors;
        try { monitors = _app.Displays.GetMonitors(); }
        catch { monitors = new(); }
        var layout = _profile.Layout ?? _current;
        var listed = layout is null ? new() : _app.Displays.Describe(layout).Where(m => m.Key.Length > 0).Select(m => (m.Key, m.Name)).ToList();
        if (listed.Count == 0)
            listed = monitors.Where(m => m.Active).Select(m => (m.Key, m.Name)).ToList();
        var numbers = monitors.GroupBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Number, StringComparer.OrdinalIgnoreCase);
        var shared = listed.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return listed.Select(m => shared.Contains(m.Name) && numbers.TryGetValue(m.Key, out int n) && n > 0 ? (m.Key, $"{m.Name} ({n})") : m).ToList();
    }

    private bool Validate()
    {
        var problem = _app.DisplayProfiles.Validate(_name.Text.Trim(), _hotkey.Hotkey, _profile)
                      ?? _wallpapers.Values.Select(b => WallpaperPath(b.Text)).Where(p => p.Length > 0 && !File.Exists(p)).Select(p => $"Can't find {Path.GetFileName(p)}.").FirstOrDefault();
        _error.Text = problem ?? "";
        _error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        return problem is null;
    }

    internal static string WallpaperPath(string typed) => typed.Trim().Trim('"').Trim();

    public void SaveInto(DisplayProfile profile)
    {
        profile.Name = _name.Text.Trim();
        profile.Hotkey = _hotkey.Hotkey;
        profile.Hdr = _hdr;
        profile.Taskbar = _taskbar;
        profile.Layout = _layoutChoice switch { 1 => _current ?? profile.Layout, 2 => null, _ => profile.Layout };
        var wallpapers = profile.Wallpapers.Where(w => !_wallpapers.ContainsKey(w.Key)).ToDictionary(w => w.Key, w => w.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, box) in _wallpapers)
            if (WallpaperPath(box.Text) is { Length: > 0 } path) wallpapers[key] = path;
        profile.Wallpapers = wallpapers;
        var playback = _playback.SelectedItem as Choice;
        var recording = _recording.SelectedItem as Choice;
        profile.Audio = playback?.Id is null && recording?.Id is null ? null : new AudioSnapshot
        {
            PlaybackId = playback?.Id, PlaybackName = playback?.Extra,
            RecordingId = recording?.Id, RecordingName = recording?.Extra,
            SetCommunications = _comms.IsChecked == true,
        };
        profile.Volume = _volume;
    }
}
