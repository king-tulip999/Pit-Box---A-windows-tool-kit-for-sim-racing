using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PitBox.Services;
using PitBox.SimRacing.IracingFiles;

namespace PitBox.Views.Pages;

internal sealed class StorageView : ContentControl
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new();
    private int _version, _replayDays = 90, _paintDays = 90;

    public StorageView(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Grid.SetIsSharedSizeScope(_body, true);
        Content = _body;
        Ui.Anchor(this, "settings.storage");
        _body.Children.Add(Ui.Text("Measuring…", "Caption"));
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) _ = LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        int version = ++_version;
        var members = (Plugins.BuiltIn.SessionStatsPlugin.Of(_app)?.Store?.Db.Query("SELECT DISTINCT driver_id FROM sessions WHERE driver_id > 0") ?? new())
            .Select(r => Convert.ToInt64(r[0])).ToList();
        StorageReport report;
        try { report = await Task.Run(() => StorageCleanup.Scan(members, Config.ConfigStore.Folder, SessionStats.TraceStore.DefaultPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); return; }
        if (version != _version) return;
        Show(report);
    }

    private static string KindName(StorageKind kind) => kind switch
    {
        StorageKind.Replays => "iRacing replays",
        StorageKind.Paints => "iRacing paints",
        StorageKind.Telemetry => "iRacing telemetry",
        StorageKind.Traces => "Lap telemetry (Session Stats)",
        StorageKind.Backups => "iRacing file backups",
        _ => "Pit-Box settings, stats and logs",
    };

    private static string? Note(StorageKind kind) => kind switch
    {
        StorageKind.Paints => "Liveries downloaded for other drivers come back when you race them again. Yours and team paints are always kept.",
        StorageKind.Telemetry => "Clear the files Session Stats has read on its Telemetry files card.",
        StorageKind.Traces => "Kept as set under Session Stats → Lap telemetry.",
        StorageKind.Backups => "Pit-Box keeps the last 30 automatic backups and every one you made yourself.",
        _ => null,
    };

    private void Show(StorageReport r)
    {
        _body.Children.Clear();
        var list = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var part in r.Parts.OrderByDescending(p => p.Bytes))
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = TextFit.TipWhenTrimmed(new TextBlock { Text = KindName(part.Kind), Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var size = new TextBlock { Text = StorageCleanup.Size(part.Bytes), Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
            var open = Ui.IconButton("I.Folder", "Open the folder", () => OpenFolder(part.Folder), "QuietIconButton");
            open.IsEnabled = Directory.Exists(part.Folder);
            Grid.SetColumn(size, 1);
            Grid.SetColumn(open, 2);
            line.Children.Add(name);
            line.Children.Add(size);
            line.Children.Add(open);
            var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 6), Children = { line } };
            if (Note(part.Kind) is { } note)
                entry.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
            list.Children.Add(entry);
        }
        _body.Children.Add(list);
        _body.Children.Add(Clear("Replays", r.Replays, d => StorageCleanup.OldReplays(r.Replays, d, DateTime.UtcNow), _replayDays, d => _replayDays = d,
            "The newest replay is always kept."));
        if (r.Members.Count > 0)
            _body.Children.Add(Clear("Paints", r.Paints, d => StorageCleanup.OldPaints(r.Paints, d, DateTime.UtcNow, r.Members), _paintDays, d => _paintDays = d,
                "Only other drivers' downloaded paints. Yours (and team paints) stay."));
        else
            _body.Children.Add(Ui.Text("Paints can be cleared once Session Stats or a telemetry file knows which driver is you, so yours are never touched.", "Caption"));
    }

    private FrameworkElement Clear(string what, IReadOnlyList<FileInfo> all, Func<int, List<FileInfo>> pick, int days, Action<int> setDays, string rule)
    {
        var line = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var label = new TextBlock { Text = $"{what} older than", Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
        var button = Ui.LabeledButton("I.Trash", "", () => { }, "DangerButton");
        button.Margin = new Thickness(0, 0, 0, 6);
        List<FileInfo> chosen = new();
        void Count()
        {
            chosen = pick(days);
            long bytes = chosen.Sum(f => f.Length);
            button.Content = Ui.IconLabel("I.Trash", chosen.Count == 0 ? "Nothing to clear" : $"Move {SimRacing.Race.RaceFormat.Count(chosen.Count, "file")} to the Recycle Bin ({StorageCleanup.Size(bytes)})");
            button.IsEnabled = chosen.Count > 0 && !_app.Demo;
            button.Style = Ui.Res<Style>(chosen.Count > 0 ? "DangerButton" : "GhostButton");
        }
        var age = Ui.Combo(StorageCleanup.AgeChoices.Select(d => new Choice(d.ToString(), d >= 365 ? "1 year" : $"{d} days")), days.ToString(), c =>
        {
            if (!int.TryParse(c.Id, out int d)) return;
            days = d;
            setDays(d);
            Count();
        }, 104);
        age.Margin = new Thickness(0, 0, 0, 6);
        button.Click += (_, _) => _ = MoveAsync(what, chosen, rule);
        Count();
        var ageRow = new Grid { Margin = new Thickness(0, 0, Ui.GroupGap, 0) };
        ageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ClearLabel" });
        ageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(age, 1);
        ageRow.Children.Add(label);
        ageRow.Children.Add(age);
        line.Children.Add(ageRow);
        line.Children.Add(button);
        return new StackPanel { Children = { line, new TextBlock { Text = rule + " You can restore them from the Recycle Bin.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap } } };
    }

    private async Task MoveAsync(string what, List<FileInfo> files, string rule)
    {
        if (_app.Demo || files.Count == 0) return;
        long bytes = files.Sum(f => f.Length);
        if (!await _window.Confirm($"Move old {what.ToLowerInvariant()} to the Recycle Bin?",
                $"{SimRacing.Race.RaceFormat.Count(files.Count, "file")} ({StorageCleanup.Size(bytes)}). {rule}", "Move to Recycle Bin", danger: true))
            return;
        try
        {
            int moved = await Task.Run(() => RecycleBin.Send(files.Select(f => f.FullName).ToList()));
            _app.Notify("Space cleared", $"{SimRacing.Race.RaceFormat.Count(moved, "file")} moved to the Recycle Bin.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify($"{what} weren't moved", ex.Message, NoticeKind.Warning, important: true);
        }
        await LoadAsync();
    }

    private static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder) || !External.Allowed("open " + folder)) return;
        try { Process.Start(new ProcessStartInfo(App.Explorer, $"\"{folder}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { App.LogError(ex); }
    }
}
