using System.Windows;
using System.Windows.Controls;
using Core.Services;

namespace Core.Views;

internal static class OldAppDialog
{
    public static void CheckLater(MainWindow window, AppServices app)
    {
        if (app.Demo || app.Config.Ui.OldAppNoticeOff) return;
        _ = Run(window, app);
    }

    private static async Task Run(MainWindow window, AppServices app)
    {
        OldAppState state;
        try { state = await Task.Run(OldAppHandover.Check); }
        catch (Exception ex)
        {
            App.LogError(ex);
            return;
        }
        if (!state.Present) return;
        bool removeTask = await Show(window, app, state);
        if (!removeTask) return;
        bool removed = false;
        string output = "";
        await Task.Run(() => removed = StartupManager.RemoveTask(OldAppHandover.TaskName, out output));
        if (removed)
            app.Notify($"{OldAppHandover.OldName} won't start with Windows", "Its startup entry is removed.", NoticeKind.Success);
        else
            app.Notify($"Couldn't remove {OldAppHandover.OldName}'s startup entry", output.Trim(), NoticeKind.Error, important: true);
    }

    private static Task<bool> Show(MainWindow window, AppServices app, OldAppState state)
    {
        var kind = AppIdentity.Kind;
        var card = window.DialogCard($"{OldAppHandover.OldName} is still on this PC", OldAppHandover.Message(kind, state), out var buttons, width: 460);
        var never = new Button { Content = "Don't ask again", Margin = Ui.AfterButton };
        never.ToolTip = $"Stops this check; {AppIdentity.Name} won't look for {OldAppHandover.OldName} again";
        never.Click += (_, _) =>
        {
            app.Config.Ui.OldAppNoticeOff = true;
            app.Save();
            window.CloseModal(false);
        };
        buttons.Children.Add(never);
        if (OldAppHandover.CanRemoveTask(kind, state))
        {
            var later = new Button { Content = "Later", Margin = Ui.AfterButton, IsCancel = true };
            later.Click += (_, _) => window.CloseModal(false);
            var remove = new Button { Content = "Remove startup entry", Style = Ui.Res<Style>("PrimaryButton"), IsDefault = true };
            remove.Click += (_, _) => window.CloseModal(true);
            buttons.Children.Add(later);
            buttons.Children.Add(remove);
        }
        else
        {
            var close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
            close.Click += (_, _) => window.CloseModal(false);
            buttons.Children.Add(close);
        }
        return window.ShowModal(card);
    }
}
